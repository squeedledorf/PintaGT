//
// MainWindow.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Addins;
using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;
using Pinta.Resources;

namespace Pinta;

internal sealed class MainWindow
{
	readonly Adw.Application app;
	// NRT - Created in OnActivated
	WindowShell window_shell = null!;
	Gio.Menu menu_bar = null!;
	Gio.Menu image_menu = null!;
	Gio.Menu view_menu = null!;
	Gio.Menu help_menu = null!;

	DockNotebook canvas_notebook = null!;
	PanelArea panel_area = null!;
	ColorsPanel colors_panel = null!;
	ImageThumbnailStrip image_list = null!;

	// The floating windows' toggles (F5–F8), and those that View > Tool Windows (F12) hid.
	ToggleCommand[] panel_toggles = [];
	ToggleCommand[] hidden_by_tool_windows = [];

	private int main_thread_id = -1;
	private Gtk.DropTarget drop_target = null!;

	public MainWindow (Adw.Application app)
	{
		this.app = app;

		// Set the human-readable application name, used by e.g. gtk_recent_manager_add_item().
		GLib.Functions.SetApplicationName (Translations.GetString ("Pinta"));
	}

	/// <summary>
	/// Performs any initialization that is not related to showing a new
	/// window (i.e. the Activate () method).
	/// In particular, the application menu must be initialized here in order to
	/// appear in the application window later (Gtk.Application.Menubar).
	/// </summary>
	public void Startup ()
	{
		CreateMainMenu ();
	}

	public void Activate ()
	{
		// Build our window
		CreateWindow ();

		// Initialize interface things
		_ = new ActionHandlers ();

		PintaCore.Chrome.InitializeProgessDialog (ProgressDialog.New (PintaCore.Chrome));
		PintaCore.Chrome.InitializeErrorDialogHandler (ErrorDialog.ShowError);
		PintaCore.Chrome.InitializeMessageDialog (ErrorDialog.ShowMessage);
		PintaCore.Chrome.InitializeSimpleEffectDialog (SimpleEffectDialog.Launch);

		PintaCore.Initialize ();

		// Initialize extensions
		string addins_dir = System.IO.Path.Combine (PintaCore.Settings.GetUserSettingsDirectory (), "addins");
		AddinManager.Initialize (addins_dir);

		AddinManager.Registry.Update ();
		var setupService = new AddinSetupService (AddinManager.Registry);
		if (!setupService.AreRepositoriesRegistered ())
			setupService.RegisterRepositories (true);

		// Look out for any changes in extensions
		main_thread_id = Environment.CurrentManagedThreadId;
		AddinManager.AddExtensionNodeHandler (typeof (IExtension), OnExtensionChanged);

		// Load the user's previous settings
		LoadUserSettings ();
		PintaCore.Actions.App.BeforeQuit += delegate { SaveUserSettings (); };

		// We support drag and drop for URIs, which are converted into a Gdk.FileList.
		drop_target = Gtk.DropTarget.New (Gdk.FileList.GetGType (), Gdk.DragAction.Copy);
		drop_target.OnDrop += HandleDrop;
		window_shell.Window.AddController (drop_target);

		// Handle a few main window specific actions
		window_shell.Window.OnCloseRequest += HandleCloseRequest;

		// Add custom key handling during the capture phase. For example, this ensures that the "-" key
		// can be typed into the dash pattern box instead of being handled as a shortcut
		var key_controller = Gtk.EventControllerKey.New ();
		key_controller.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		key_controller.OnKeyPressed += HandleGlobalKeyPress;
		key_controller.OnKeyReleased += HandleGlobalKeyRelease;
		window_shell.Window.AddController (key_controller);

		PintaCore.Actions.View.PixelGrid.Toggled += PixelGrid_Toggled;

		// TODO: These need to be [re]moved when we redo zoom support
		PintaCore.Actions.View.ZoomToWindow.Activated += ZoomToWindowCommand_Activated;
		PintaCore.Actions.View.ZoomToSelection.Activated += ZoomToSelection_Activated;

		PintaCore.Workspace.ActiveDocumentChanged += ActiveDocumentChanged;

		PintaCore.Workspace.DocumentActivated += Workspace_DocumentCreated;
		PintaCore.Workspace.DocumentClosed += Workspace_DocumentClosed;

		// Alt+minus opens the image list's menu for the current image, as in Paint.NET.
		Gio.SimpleAction image_menu_action = Gio.SimpleAction.New ("image-menu", null);
		image_menu_action.OnActivate += (_, _) => image_list.PopupMenuForActiveImage ();
		window_shell.Window.AddAction (image_menu_action);
		app.SetAccelsForAction ("win.image-menu", ["<Alt>minus"]);

		DockNotebook notebook = canvas_notebook;
		notebook.TabClosed += DockNotebook_TabClosed;
		notebook.ActiveTabChanged += DockNotebook_ActiveTabChanged;
		// Keep OpenDocuments in tab order, which Ctrl+Tab and Ctrl+1..9 follow.
		notebook.TabReordered += (_, e) => PintaCore.Workspace.MoveDocument (
			PintaCore.Workspace.OpenDocuments.IndexOf (((DocumentViewContent) e.Item).Document), e.Position);
		// And the pages in document order when the image list is dragged, so the tab view's own
		// shortcuts (Ctrl+Shift+PgUp/PgDn) move the image the user sees.
		PintaCore.Workspace.DocumentsReordered += (_, _) => {
			IReadOnlyList<Document> documents = PintaCore.Workspace.OpenDocuments;
			for (int i = 0; i < documents.Count; i++)
				if (notebook.Items.FirstOrDefault (item => ((DocumentViewContent) item).Document == documents[i]) is IDockNotebookItem item)
					notebook.MoveTab (item, i);
		};
	}

	private void Workspace_DocumentClosed (object? sender, DocumentEventArgs e)
	{
		var tab = FindTabWithCanvas ((CanvasWindow) e.Document.Workspace.CanvasWindow);

		if (tab != null)
			canvas_notebook.RemoveTab (tab);
	}

	private void DockNotebook_TabClosed (object? sender, TabClosedEventArgs e)
	{
		var view = (DocumentViewContent) e.Item;

		int index = PintaCore.Workspace.OpenDocuments.IndexOf (view.Document);
		if (index < 0)
			return;

		PintaCore.Workspace.SetActiveDocument (index);
		PintaCore.Actions.File.Close.Activate ();

		if (PintaCore.Workspace.OpenDocuments.IndexOf (view.Document) < 0)
			return;

		// User must have canceled the close
		e.Cancel = true;
	}

	private void DockNotebook_ActiveTabChanged (object? sender, EventArgs e)
	{
		var item = canvas_notebook.ActiveItem;

		if (item == null)
			return;

		var view = (DocumentViewContent) item;

		int index = PintaCore.Workspace.OpenDocuments.IndexOf (view.Document);
		PintaCore.Workspace.SetActiveDocument (index);
		((CanvasWindow) view.Widget).Canvas.Cursor = PintaCore.Tools.CurrentTool?.CurrentCursor;
	}

	private void Workspace_DocumentCreated (object? sender, DocumentEventArgs e)
	{
		var doc = e.Document;

		var notebook = canvas_notebook;

		CanvasWindow canvas = CanvasWindow.New (
			PintaCore.Chrome,
			PintaCore.Tools,
			doc, PintaCore.CanvasGrid, PintaCore.Settings);
		canvas.RulersVisible = PintaCore.Actions.View.Rulers.Value;
		canvas.RulerMetric = GetCurrentRulerMetric ();
		doc.Workspace.CanvasWindow = canvas;
		doc.Workspace.Canvas = canvas.Canvas;

		DocumentViewContent my_content = new (doc, canvas);

		notebook.AppendTab (my_content);

		// Zoom to window only on first show (if we do it always, it will be called on every resize)
		// Note: this does seem to allow a small flicker where large images are shown at 100% zoom before
		// zooming out (Bug 1959673)
		// If the canvas is turned into a custom Gtk.Widget subclass in the future, this could
		// perhaps be done on the first measurement request

		bool canvasHasBeenShown = false;

		Gtk.Viewport view = (Gtk.Viewport) doc.Workspace.Canvas.Parent!;
		view.Hadjustment!.OnChanged += (o, e2) => {
			if (canvasHasBeenShown)
				return;

			GLib.Functions.IdleAdd (
				0,
				() => {
					ZoomToWindow_Activated (o, e);
					PintaCore.Workspace.Invalidate ();
					return false;
				}
			);

			canvasHasBeenShown = true;
		};

		PintaCore.Actions.View.Rulers.Toggled += (active, _) => { canvas.RulersVisible = active; };
		PintaCore.Actions.View.RulerMetric.OnActivate += (o, args) => {
			PintaCore.Actions.View.RulerMetric.ChangeState (args.Parameter!);
			canvas.RulerMetric = GetCurrentRulerMetric ();
		};
	}

	private static MetricType GetCurrentRulerMetric ()
	{
		GLib.Variant state = PintaCore.Actions.View.RulerMetric.GetState () ??
			throw new InvalidOperationException ("action should not be stateless");

		return (MetricType) state.GetInt32 ();
	}

	private bool HandleGlobalKeyPress (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyPressedSignalArgs args)
	{
		// Give the widget that has focus a first shot at handling the event.
		// Otherwise, key presses may be intercepted by shortcuts for menu items.
		if (SendToFocusWidget (controller))
			return true;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		if (PintaCore.Workspace.HasOpenDocuments) {
			var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

			if ((canvas_window.HasFocus || canvas_window.IsMouseOnCanvas) &&
				 canvas_window.DoKeyPressEvent (controller, args)) {
				return true;
			}
		}

		// Enter deselects (Paint.NET), once the tool has had a chance to use it.
		if (IsPlainEnter (args) && PintaCore.Actions.Edit.Deselect.Sensitive) {
			PintaCore.Actions.Edit.Deselect.Activate ();
			return true;
		}

		// If the canvas/tool didn't consume it, see if its a toolbox shortcut
		// As in Paint.NET, Shift with a tool letter cycles backwards through the tools sharing it.
		bool shiftOnly = args.State.IsShiftPressed () && !args.State.IsControlPressed () && !args.State.IsAltPressed ();
		if ((!args.State.HasModifierKey () || shiftOnly) && PintaCore.Tools.SetCurrentTool (args.GetKey (), reverse: shiftOnly))
			return true;

		// Finally, see if the palette widget wants it.
		bool shouldSwapColors = !args.State.HasModifierKey () && args.GetKey ().ToUpper ().Value == Gdk.Constants.KEY_X;

		if (shouldSwapColors) {
			PintaCore.Palette.SwapColors ();
			return true;
		}

		// C moves the Colors window's notch to the other colour, as in Paint.NET.
		if (!args.State.HasModifierKey () && args.GetKey ().ToUpper ().Value == Gdk.Constants.KEY_C) {
			colors_panel.ToggleActiveSlot ();
			return true;
		}

		// We return 'false' to indicate that nobody consumed it
		return false;
	}

	private static bool IsPlainEnter (Gtk.EventControllerKey.KeyPressedSignalArgs args)
	{
		if (args.State.HasModifierKey ())
			return false;

		uint key = args.Keyval;
		return key == Gdk.Constants.KEY_Return || key == Gdk.Constants.KEY_KP_Enter || key == Gdk.Constants.KEY_ISO_Enter;
	}

	private void HandleGlobalKeyRelease (
		Gtk.EventControllerKey controller,
		Gtk.EventControllerKey.KeyReleasedSignalArgs args)
	{
		if (SendToFocusWidget (controller) || !PintaCore.Workspace.HasOpenDocuments)
			return;

		// Give the Canvas (and by extension the tools)
		// first shot at handling the event if
		// the mouse pointer is on the canvas
		var canvas_window = (CanvasWindow) PintaCore.Workspace.ActiveWorkspace.CanvasWindow;

		if (canvas_window.HasFocus || canvas_window.IsMouseOnCanvas)
			canvas_window.DoKeyReleaseEvent (controller, args);
	}

	private bool SendToFocusWidget (Gtk.EventControllerKey key_controller)
	{
		var widget = window_shell.Window.FocusWidget;
		if (widget != null && key_controller.Forward (widget)) return true;
		return false;
	}

	// Called when an extension node is added or removed
	// Note this may be called from any thread, not just the main UI thread!
	private void OnExtensionChanged (object s, ExtensionNodeEventArgs args)
	{
		// Run synchronously if invoked from the main thread, e.g. when loading
		// addins at startup we require them to be immediately loaded.
		if (Environment.CurrentManagedThreadId == main_thread_id)
			UpdateExtension (args);
		else {
			// Otherwise, schedule the addin to be loaded/unloaded from the main thread
			// in case it touches the UI, e.g. to update menu items.
			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
				UpdateExtension (args);
				return false;
			});
		}
	}

	private static void UpdateExtension (ExtensionNodeEventArgs args)
	{
		if (args.Change == ExtensionChange.Add) {
			try {
				IExtension extension = (IExtension) args.ExtensionObject;
				extension.Initialize ();
			} catch (Exception e) {
				// Translators: {0} is the name of an add-in.
				string body = Translations.GetString ("The '{0}' add-in may not be compatible with this version of Pinta", args.ExtensionNode.Addin.Id);
				_ = PintaCore.Chrome.ShowErrorDialog (
					PintaCore.Chrome.MainWindow,
					Translations.GetString ("Failed to initialize add-in"),
					body, e.ToString ());
			}
		} else {
			IExtension extension = (IExtension) args.ExtensionObject;
			extension.Uninitialize ();
		}
	}

	private void CreateWindow ()
	{
		// Check for stored window settings
		int width = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_WIDTH, 1100);
		int height = PintaCore.Settings.GetSetting (SettingNames.WINDOW_SIZE_HEIGHT, 750);
		bool maximize = PintaCore.Settings.GetSetting (SettingNames.WINDOW_MAXIMIZED, false);

		ResourceLoader.LoadCssStyles ();

		window_shell = new WindowShell (
			app,
			"Pinta.GenericWindow",
			"Pinta",
			width,
			height,
			useMenuBar: IsUsingMenuBar (),
			maximize);

		// Paint.NET 5's rows: menu bar + image list + window toggles, the main toolbar,
		// the tool options, then the canvas with the floating windows, and the status bar.
		CreateTopBar ();
		CreateToolToolBar ();

		CreatePanels ();
		CreateStatusBar ();

		app.AddWindow (window_shell.Window);

		PintaCore.Chrome.InitializeApplication (app);
		PintaCore.Chrome.InitializeWindowShell (window_shell.Window);
	}

	private bool IsUsingMenuBar ()
	{
		return PintaCore.Settings.GetSetting (SettingNames.MENUBAR_SHOWN, SettingDefaults.MenuBarShown ());
	}

	private void CreateMainMenu ()
	{
		bool usingMenuBar = IsUsingMenuBar ();

		// When using a header bar, the View, Image, Effects, and Adjustments menus
		// are shown as menu buttons in the toolbar (see CreateMainToolBar ())

		Gio.Menu fileMenu = Gio.Menu.New ();
		Gio.Menu editMenu = Gio.Menu.New ();
		Gio.Menu imageMenu = Gio.Menu.New ();
		Gio.Menu layersMenu = Gio.Menu.New ();
		Gio.Menu adjustmentsMenu = Gio.Menu.New ();
		Gio.Menu effectsMenu = Gio.Menu.New ();
		// Paint.NET has no Window menu. Its commands stay registered (and keep the
		// document list and window title updated), but the menu itself is not shown.
		Gio.Menu windowMenu = Gio.Menu.New ();
		Gio.Menu helpMenu = Gio.Menu.New ();
		Gio.Menu padSection = Gio.Menu.New ();

		Gio.Menu viewMenu = Gio.Menu.New ();
		viewMenu.AppendSection (null, padSection);

		Gio.Menu menuBar = Gio.Menu.New ();
		menuBar.AppendSubmenu (Translations.GetString ("_File"), fileMenu);
		menuBar.AppendSubmenu (Translations.GetString ("_Edit"), editMenu);
		if (usingMenuBar) {
			menuBar.AppendSubmenu (Translations.GetString ("_View"), viewMenu);
			menuBar.AppendSubmenu (Translations.GetString ("_Image"), imageMenu);
		}
		menuBar.AppendSubmenu (Translations.GetString ("_Layers"), layersMenu);
		if (usingMenuBar) {
			menuBar.AppendSubmenu (Translations.GetString ("_Adjustments"), adjustmentsMenu);
			menuBar.AppendSubmenu (Translations.GetString ("Effe_cts"), effectsMenu);
		}
		// Paint.NET has no Help menu: help lives behind the ? button at the far right of the top row.
		// The macOS application menu bar keeps it.
		if (SystemManager.GetOperatingSystem () == OS.Mac)
			menuBar.AppendSubmenu (Translations.GetString ("_Help"), helpMenu);

		// --- Global initializations

		// Elsewhere the menu bar is drawn in the window's top row (see CreateTopBar).
		if (usingMenuBar && SystemManager.GetOperatingSystem () == OS.Mac)
			app.Menubar = menuBar;

		// Since GTK 4.14 there is an autogenerated Application menu, so we just need
		// to register actions with matching names for About, Quit, etc
		// https://gitlab.gnome.org/GNOME/gtk/-/issues/6762
		// For non-macOS, we still register the commands here and then other menus add the items.
		PintaCore.Actions.App.RegisterActions (app);

		PintaCore.Actions.File.RegisterActions (app, fileMenu);
		PintaCore.Actions.Edit.RegisterActions (app, editMenu);
		PintaCore.Actions.View.RegisterActions (app, viewMenu);
		PintaCore.Actions.Image.RegisterActions (app, imageMenu);
		PintaCore.Actions.Layers.RegisterActions (app, layersMenu);
		PintaCore.Actions.Window.RegisterActions (app, windowMenu);
		// This also registers the add-in manager and mounts the add-ins section inside Help.
		PintaCore.Actions.Help.RegisterActions (app, helpMenu);

		PintaCore.Chrome.InitializeMainMenu (adjustmentsMenu, effectsMenu);

		// --- References to keep

		image_menu = imageMenu;
		menu_bar = menuBar;
		view_menu = viewMenu;
		help_menu = helpMenu;
	}

	/// <summary>
	/// Paint.NET 5's top rows: the window title, the menu bar and the main toolbar on the left,
	/// the image list beside all three, and at the far right the Tools/History/Layers/Colors
	/// toggles, Settings and Help.
	/// </summary>
	private void CreateTopBar ()
	{
		Gtk.Grid top = Gtk.Grid.New ();
		top.AddCssClass ("pdn-top");
		int rows = 1;

		if (window_shell.HeaderBar is null) {
			if (SystemManager.GetOperatingSystem () != OS.Mac) {
				// Paint.NET's title row ("*name - paint.net"). Tiling window managers draw no title bar,
				// and the image list needs this row to reach Paint.NET's thumbnail height.
				Gtk.Box title = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
				title.AddCssClass ("pdn-title");
				Gtk.Image icon = Gtk.Image.NewFromIconName ("com.github.PintaProject.Pinta");
				icon.PixelSize = 16;
				title.Append (icon);
				Gtk.Label titleLabel = Gtk.Label.New (null);
				titleLabel.Ellipsize = Pango.EllipsizeMode.End;
				window_shell.Window.BindProperty (
					Gtk.Window.TitlePropertyDefinition.UnmanagedName,
					titleLabel,
					"label",
					GObject.BindingFlags.SyncCreate);
				title.Append (titleLabel);
				top.Attach (title, 0, 0, 1, 1);

				// The menus sit in a tab-shaped box; a rule runs from its bottom-right corner to the
				// window's right edge, behind the image list and the window toggles. The row spans every
				// column and is attached first, so the thumbnails draw over the rule.
				Gtk.Box menuRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
				Gtk.PopoverMenuBar menus = Gtk.PopoverMenuBar.NewFromModel (menu_bar);
				PdnMenus.Attach (menus);
				menuRow.Append (menus);
				Gtk.Box rule = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
				rule.AddCssClass ("pdn-top-rule");
				rule.Hexpand = true;
				rule.Valign = Gtk.Align.End;
				menuRow.Append (rule);
				top.Attach (menuRow, 0, 1, 3, 1);
			}

			Gtk.Box main_toolbar = GtkExtensions.CreateToolBar ();
			main_toolbar.Name = "main_toolbar";
			PintaCore.Actions.CreateToolBar (main_toolbar);
			top.Attach (main_toolbar, 0, 2, 1, 1);
			rows = 3;
		} else
			CreateHeaderBarMenus (window_shell.HeaderBar);

		image_list = ImageThumbnailStrip.New ();
		image_list.Hexpand = true;
		image_list.MarginStart = 6;
		image_list.Valign = Gtk.Align.End; // Level with the main toolbar's bottom, across the menu rule.
		image_list.ThumbnailHeight = rows == 3 ? 66 : 40; // 76px cells spanning the rows beside it, as in Paint.NET.
		PintaCore.Chrome.InitializeImageTabsNotebook (image_list);
		top.Attach (image_list, 1, 0, 1, rows);

		ViewActions view = PintaCore.Actions.View;
		panel_toggles = [view.ToolsWindow, view.HistoryWindow, view.LayersWindow, view.ColorsWindow];

		Gtk.Box window_buttons = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		window_buttons.Name = "window_buttons";
		window_buttons.Valign = Gtk.Align.Center;
		Dictionary<Gtk.Widget, ToggleCommand> toggle_buttons = [];
		foreach (ToggleCommand toggle in panel_toggles) {
			Gtk.ToggleButton button = CreateWindowToggle (toggle);
			toggle_buttons[button] = toggle;
			window_buttons.Append (button);
		}

		// Ctrl+Shift+click on a window's toggle resets the window, as in Paint.NET. The box catches
		// the press before the button does, so the button doesn't also toggle.
		Gtk.GestureClick reset_click = Gtk.GestureClick.New ();
		reset_click.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		reset_click.OnPressed += (gesture, args) => {
			ToggleCommand? toggle = null;
			Gdk.ModifierType state = gesture.GetCurrentEventState ();
			if (state.IsControlPressed () && state.IsShiftPressed ())
				for (Gtk.Widget? w = window_buttons.Pick (args.X, args.Y, Gtk.PickFlags.Default); w is not null && toggle is null; w = w.Parent)
					toggle_buttons.TryGetValue (w, out toggle);

			if (toggle is null) {
				gesture.SetState (Gtk.EventSequenceState.Denied);
				return;
			}
			gesture.SetState (Gtk.EventSequenceState.Claimed);
			ResetPanel (toggle);
		};
		window_buttons.AddController (reset_click);

		window_buttons.Append (GtkExtensions.CreateToolBarSeparator ());

		Gtk.Button settings = PintaCore.Actions.App.Preferences.CreateToolBarItem (force_icon_only: true);
		settings.Label = null;
		settings.IconName = PintaCore.Actions.App.Preferences.IconName;
		settings.AddCssClass (AdwaitaStyles.Flat);
		settings.TooltipText = PintaCore.Actions.App.Preferences.Label.Replace ("_", "");
		window_buttons.Append (settings);

		Gtk.MenuButton help = GtkExtensions.CreateMenuButton (help_menu, StandardIcons.HelpBrowser, Translations.GetString ("Help"));
		help.AddCssClass (AdwaitaStyles.Flat);
		PdnMenus.Attach (help);
		window_buttons.Append (help);

		top.Attach (window_buttons, 2, rows == 3 ? 1 : 0, 1, 1);

		window_shell.Append (top);
	}

	// A flat icon button that is pressed while its window is shown.
	// The pressed state is bound to the command by hand rather than through ActionName: the action
	// helper sometimes left the Tools button unpressed while its window was shown.
	private static Gtk.ToggleButton CreateWindowToggle (ToggleCommand command)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.Active = command.Value;
		button.OnToggled += (_, _) => command.Value = button.Active;
		command.Action.OnNotify += (_, e) => {
			if (e.Pspec.GetName () == "state")
				button.Active = command.Value;
		};
		button.IconName = command.IconName;
		button.TooltipText = $"{command.Label} ({command.Shortcuts[0]})"; // F5–F8
		button.Valign = Gtk.Align.Center;
		button.FocusOnClick = false;
		button.AddCssClass (AdwaitaStyles.Flat);
		return button;
	}

	// The floating windows' ids, in the order of panel_toggles.
	private static readonly string[] panel_ids = ["tools", "history", "layers", "colors"];

	// Paint.NET's Ctrl+Shift+F5–F8: put the window back at its default place and show it.
	private void ResetPanel (ToggleCommand toggle)
	{
		panel_area.ResetPanel (panel_ids[Array.IndexOf (panel_toggles, toggle)]);
		toggle.Value = true;
	}

	private void CreateHeaderBarMenus (Adw.HeaderBar headerBar)
	{
		headerBar.PackEnd (GtkExtensions.CreateMenuButton (
			this.menu_bar,
			Resources.StandardIcons.OpenMenu,
			Translations.GetString ("Main Menu")));

		headerBar.PackEnd (GtkExtensions.CreateMenuButton (
			PintaCore.Chrome.EffectsMenu,
			Resources.Icons.EffectsDefault,
			Translations.GetString ("Effects")));

		headerBar.PackEnd (GtkExtensions.CreateMenuButton (
			PintaCore.Chrome.AdjustmentsMenu,
			Resources.Icons.AdjustmentsDefault,
			Translations.GetString ("Adjustments")));

		headerBar.PackEnd (GtkExtensions.CreateMenuButton (
			this.image_menu,
			Resources.StandardIcons.ImageGeneric,
			Translations.GetString ("Image")));

		headerBar.PackEnd (GtkExtensions.CreateMenuButton (
			this.view_menu,
			Resources.StandardIcons.ViewReveal,
			Translations.GetString ("View")));

		PintaCore.Actions.CreateHeaderToolBar (headerBar);
	}

	private void CreateToolToolBar ()
	{
		Gtk.Box tool_toolbar = window_shell.CreateToolBar ("tool_toolbar");

		PintaCore.Chrome.InitializeToolToolBar (tool_toolbar);
	}

	private void CreateStatusBar ()
	{
		Gtk.Box statusbar = window_shell.CreateStatusBar ("statusbar");

		// Tool hint at the left, as in Paint.NET: the active tool's icon and "Name: hint".
		Gtk.Image tool_icon = Gtk.Image.New ();
		statusbar.Append (tool_icon);

		Gtk.Label tool_hint = Gtk.Label.New (string.Empty);
		tool_hint.Xalign = 0.0f;
		tool_hint.Hexpand = true;
		tool_hint.Halign = Gtk.Align.Fill;
		tool_hint.Ellipsize = Pango.EllipsizeMode.End;
		tool_hint.SingleLineMode = true;
		statusbar.Append (tool_hint);

		PintaCore.Chrome.StatusBarTextChanged += (_, e) => {
			string text = e.Text.Trim ().ReplaceLineEndings (" ");
			tool_hint.SetText (text);
			tool_hint.TooltipText = text;
		};
		PintaCore.Tools.ToolActivated += (_, e) => tool_icon.SetFromIconName (e.Tool.Icon);

		// Image size, cursor position, selection size, units and zoom at the right.
		PintaCore.Actions.CreateStatusBar (statusbar, PintaCore.Workspace);
		statusbar.Append (CreateSizeGrip ());

		PintaCore.Chrome.InitializeStatusBar (statusbar);
	}

	// Paint.NET's dotted size grip in the status bar's corner; dragging it resizes the window.
	private Gtk.DrawingArea CreateSizeGrip ()
	{
		Gtk.DrawingArea grip = Gtk.DrawingArea.New ();
		grip.SetSizeRequest (14, 14);
		grip.Valign = Gtk.Align.End;
		grip.Cursor = Gdk.Cursor.NewFromName (StandardCursors.ResizeSE, null);
		grip.SetDrawFunc ((_, g, width, height) => {
			// Six dots in a triangle.
			g.SetSourceRgba (0.55, 0.55, 0.55, 1);
			for (int row = 0; row < 3; row++)
				for (int col = 2 - row; col < 3; col++)
					g.Rectangle (width - 4 - (2 - col) * 4, height - 4 - (2 - row) * 4, 2, 2);
			g.Fill ();
		});

		Gtk.GestureClick press = Gtk.GestureClick.New ();
		press.OnPressed += (gesture, args) => {
			if (window_shell.Window.GetSurface () is not Gdk.Toplevel toplevel || gesture.GetDevice () is not Gdk.Device device)
				return;
			grip.TranslateCoordinates (window_shell.Window, args.X, args.Y, out double x, out double y);
			toplevel.BeginResize (Gdk.SurfaceEdge.SouthEast, device, (int) gesture.GetCurrentButton (), x, y, gesture.GetCurrentEventTime ());
		};
		grip.AddController (press);
		return grip;
	}

	/// <summary>
	/// The canvas fills the area below the tool options; Paint.NET's Tools, History, Layers and
	/// Colors windows float over it.
	/// </summary>
	private void CreatePanels ()
	{
		canvas_notebook = DockNotebook.New ();
		panel_area = PanelArea.New (canvas_notebook, PintaCore.Settings);
		window_shell.Append (panel_area);

		ToolBoxWidget toolbox = ToolBoxWidget.New (PintaCore.Tools);
		PintaCore.Chrome.InitializeToolBox (toolbox);
		colors_panel = ColorsPanel.New ();

		FloatingPanel tools = FloatingPanel.New ("tools", Translations.GetString ("Tools"), toolbox, resizable: false);
		FloatingPanel history = HistoryPad.Create (PintaCore.Actions.Edit);
		FloatingPanel layers = LayersPad.Create (PintaCore.Actions.Layers);
		FloatingPanel colors = FloatingPanel.New ("colors", Translations.GetString ("Colors"), colors_panel, resizable: false);

		// Paint.NET 5's default places: Tools top-left, Colors bottom-left, History top-right, Layers bottom-right.
		const int GAP = 6;
		panel_area.AddPanel (tools, new PanelAnchor (Right: false, Bottom: false, GAP, GAP), Size.Empty);
		panel_area.AddPanel (colors, new PanelAnchor (Right: false, Bottom: true, GAP, GAP), Size.Empty);
		panel_area.AddPanel (history, new PanelAnchor (Right: true, Bottom: false, GAP, GAP), new Size (178, 345));
		panel_area.AddPanel (layers, new PanelAnchor (Right: true, Bottom: true, GAP, GAP), new Size (178, 300));

		ViewActions view = PintaCore.Actions.View;
		BindPanel (view.ToolsWindow, tools);
		BindPanel (view.HistoryWindow, history);
		BindPanel (view.LayersWindow, layers);
		BindPanel (view.ColorsWindow, colors);

		foreach (ToggleCommand toggle in panel_toggles) {
			Gio.SimpleAction reset = Gio.SimpleAction.New ($"reset-{toggle.Name}", null);
			reset.OnActivate += (_, _) => ResetPanel (toggle);
			window_shell.Window.AddAction (reset);
			app.SetAccelsForAction ($"win.reset-{toggle.Name}", [$"<Control><Shift>{toggle.Shortcuts[0]}"]);
		}

		// View > Tool Windows (F12) hides all of them, and brings back the ones it hid.
		view.ToolWindows.Toggled += (shown, _) => {
			if (!shown) {
				hidden_by_tool_windows = panel_toggles.Where (t => t.Value).ToArray ();
				foreach (ToggleCommand toggle in hidden_by_tool_windows)
					toggle.Value = false;
			} else {
				foreach (ToggleCommand toggle in hidden_by_tool_windows.Length > 0 ? hidden_by_tool_windows : panel_toggles)
					toggle.Value = true;
				hidden_by_tool_windows = [];
			}
		};
	}

	// The window's toggle (F5–F8, the menu-row button, View > Show/Hide) and its close button stay in step.
	private static void BindPanel (ToggleCommand toggle, FloatingPanel panel)
	{
		panel.Visible = toggle.Value;
		toggle.Toggled += (shown, _) => panel.Visible = shown;
		panel.CloseClicked += (_, _) => toggle.Value = false;
	}

	private void LoadUserSettings ()
	{
		// Set selected tool to last selected or default to the PaintBrush
		PintaCore.Tools.SetCurrentTool (PintaCore.Settings.GetSetting (SettingNames.LAST_SELECTED_TOOL, "PaintBrushTool"));

		ViewActions view = PintaCore.Actions.View;
		view.Rulers.Value = PintaCore.Settings.GetSetting (SettingNames.RULER_SHOWN, false);
		view.ToolBar.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true);
		view.StatusBar.Value = PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOWN, true);
		view.ImageTabs.Value = PintaCore.Settings.GetSetting (SettingNames.IMAGE_TABS_SHOWN, true);
		view.ToolsWindow.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLS_WINDOW_SHOWN, true);
		view.HistoryWindow.Value = PintaCore.Settings.GetSetting (SettingNames.HISTORY_WINDOW_SHOWN, true);
		view.LayersWindow.Value = PintaCore.Settings.GetSetting (SettingNames.LAYERS_WINDOW_SHOWN, true);
		view.ColorsWindow.Value = PintaCore.Settings.GetSetting (SettingNames.COLORS_WINDOW_SHOWN, true);
		// Quit with every window hidden (e.g. by F12): one F12 brings them all back.
		view.ToolWindows.Value = panel_toggles.Any (t => t.Value);

		string dialog_uri = PintaCore.Settings.GetSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.DefaultDialogDirectory?.GetUri () ?? "");
		PintaCore.RecentFiles.LastDialogDirectory = Gio.FileHelper.NewForUri (dialog_uri);

		MetricType ruler_metric = (MetricType) PintaCore.Settings.GetSetting (SettingNames.RULER_METRIC, (int) MetricType.Pixels);
		PintaCore.Actions.View.RulerMetric.Activate (GLib.Variant.NewInt32 ((int) ruler_metric));
	}

	private void SaveUserSettings ()
	{
		panel_area.SaveSettings ();

		// Don't store the maximized height if the window is maximized
		if (!window_shell.Window.IsMaximized ()) {
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_WIDTH, window_shell.Window.GetWidth ());
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_HEIGHT, window_shell.Window.GetHeight ());
		}

		PintaCore.Settings.PutSetting (SettingNames.RULER_METRIC, (int) GetCurrentRulerMetric ());
		PintaCore.Settings.PutSetting (SettingNames.WINDOW_MAXIMIZED, window_shell.Window.IsMaximized ());
		PintaCore.Settings.PutSetting (SettingNames.RULER_SHOWN, PintaCore.Actions.View.Rulers.Value);
		PintaCore.Settings.PutSetting (SettingNames.IMAGE_TABS_SHOWN, PintaCore.Actions.View.ImageTabs.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBAR_SHOWN, PintaCore.Actions.View.ToolBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.STATUSBAR_SHOWN, PintaCore.Actions.View.StatusBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLS_WINDOW_SHOWN, PintaCore.Actions.View.ToolsWindow.Value);
		PintaCore.Settings.PutSetting (SettingNames.HISTORY_WINDOW_SHOWN, PintaCore.Actions.View.HistoryWindow.Value);
		PintaCore.Settings.PutSetting (SettingNames.LAYERS_WINDOW_SHOWN, PintaCore.Actions.View.LayersWindow.Value);
		PintaCore.Settings.PutSetting (SettingNames.COLORS_WINDOW_SHOWN, PintaCore.Actions.View.ColorsWindow.Value);
		PintaCore.Settings.PutSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.LastDialogDirectory?.GetUri () ?? "");

		if (PintaCore.Tools.CurrentTool is BaseTool tool)
			PintaCore.Settings.PutSetting (SettingNames.LAST_SELECTED_TOOL, tool.GetType ().Name);

		PintaCore.Settings.DoSaveSettingsBeforeQuit ();
	}

	#region Action Handlers
	private bool HandleCloseRequest (object o, EventArgs args)
	{
		PintaCore.Actions.App.Exit.Activate ();

		// Stop the default handler from running so the user can cancel quitting.
		return true;
	}

	private bool HandleDrop (Gtk.DropTarget sender, Gtk.DropTarget.DropSignalArgs args)
	{
		if (args.Value.GetBoxed (Gdk.FileList.GetGType ()) is not Gdk.FileList file_list)
			return false;

		foreach (Gio.File file_dropped in file_list.GetFilesHelper ()) {
			Gio.File file = file_dropped;

			// On macOS, GTK4 pasteboard currently provides malformed URIs where the scheme is URL-encoded
			// (e.g., "file%3A///" instead of "file:///"). Because of this, GIO fails to recognize it as a local file.
			// This was fixed in GTK 4.23.1, so this workaround can be removed once Pinta requires GTK >= 4.23.1.
			string parseName = file_dropped.GetParseName ();
			if (parseName.StartsWith ("file%3A///", StringComparison.OrdinalIgnoreCase)) {
				string decodedUri = Uri.UnescapeDataString (parseName);
				file = Gio.FileHelper.NewForUri (decodedUri);
			}

			PintaCore.Workspace.OpenFile (file);

			if (file.GetUriScheme () is string scheme &&
			   (scheme.StartsWith ("http") || scheme.StartsWith ("ftp"))) {
				// If the file was likely dragged from a browser, mark as not having a file
				// so that the user must choose a new file to save to instead of hitting a permission error.
				PintaCore.Workspace.ActiveDocument.ClearFileReference ();
			}
		}

		return true;
	}

	private (bool Show, int Width, int Height) grid_before_pixel_grid;

	// The pixel grid reuses the canvas grid with 1x1 cells, and restores the previous grid when turned off.
	private void PixelGrid_Toggled (bool active, bool interactive)
	{
		CanvasGridManager grid = PintaCore.CanvasGrid;

		if (active) {
			grid_before_pixel_grid = (grid.ShowGrid, grid.CellWidth, grid.CellHeight);
			grid.CellWidth = 1;
			grid.CellHeight = 1;
			grid.ShowGrid = true;
		} else {
			grid.ShowGrid = grid_before_pixel_grid.Show;
			grid.CellWidth = grid_before_pixel_grid.Width;
			grid.CellHeight = grid_before_pixel_grid.Height;
		}
	}

	private void ZoomToSelection_Activated (object sender, EventArgs e)
	{
		PintaCore.Workspace.ActiveWorkspace.ZoomToCanvasRectangle (PintaCore.Workspace.ActiveDocument.Selection.GetBounds ());
	}

	// The zoom and scroll position before Ctrl+B, and the zoom it fitted to.
	private (Document Document, double Scale, double X, double Y, double FitScale)? zoom_before_fit;

	// As in Paint.NET, Ctrl+B a second time goes back to the zoom and position from before the first.
	private void ZoomToWindowCommand_Activated (object sender, EventArgs e)
	{
		ViewActions view = PintaCore.Actions.View;
		DocumentWorkspace workspace = PintaCore.Workspace.ActiveWorkspace;
		Document document = PintaCore.Workspace.ActiveDocument;
		Gtk.Viewport viewport = (Gtk.Viewport) workspace.Canvas.Parent!;

		// Re-fitting after an edit (e.g. a crop) while in Zoom to Window mode is not a second Ctrl+B.
		if (view.RefittingToWindow) {
			zoom_before_fit = null;
			ZoomToWindow_Activated (sender, e);
			return;
		}

		if (zoom_before_fit is var (fit_document, scale, x, y, fit_scale) && fit_document == document && workspace.Scale == fit_scale) {
			zoom_before_fit = null;
			view.ZoomToWindowActivated = false;
			workspace.Scale = scale;
			view.SuspendZoomUpdate ();
			view.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (scale));
			view.ResumeZoomUpdate ();
			// Scroll once the scrollbars have caught up with the new size, which can take a few
			// layout passes at high zoom; until then the values get clamped to the old range.
			int tries = 0;
			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
				Gtk.Adjustment h = viewport.Hadjustment!, v = viewport.Vadjustment!;
				h.Value = x;
				v.Value = y;
				return (h.Value != x || v.Value != y) && ++tries < 20;
			});
			return;
		}

		(double Scale, double X, double Y) before = (workspace.Scale, viewport.Hadjustment!.Value, viewport.Vadjustment!.Value);
		ZoomToWindow_Activated (sender, e);
		zoom_before_fit = (document, before.Scale, before.X, before.Y, workspace.Scale);
	}

	private void ZoomToWindow_Activated (object sender, EventArgs e)
	{
		// The image is small enough to fit in the window
		if (PintaCore.Workspace.ImageFitsInWindow) {
			PintaCore.Actions.View.ActualSize.Activate ();
		} else {
			Size image = PintaCore.Workspace.ImageSize;
			double ratio = DocumentWorkspace.GetFitScale (image.Width, image.Height, PintaCore.Workspace.ActiveWorkspace.WindowSize, DocumentWorkspace.FitMargin);

			PintaCore.Workspace.Scale = ratio;
			PintaCore.Actions.View.SuspendZoomUpdate ();
			PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
			PintaCore.Actions.View.ResumeZoomUpdate ();
		}

		PintaCore.Actions.View.ZoomToWindowActivated = true;
	}
	#endregion


	private void ActiveDocumentChanged (object? sender, EventArgs e)
	{
		if (!PintaCore.Workspace.HasOpenDocuments)
			return;

		PintaCore.Actions.View.SuspendZoomUpdate ();
		PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry ().SetText (ViewActions.ToPercent (PintaCore.Workspace.Scale));
		PintaCore.Actions.View.ResumeZoomUpdate ();

		var doc = PintaCore.Workspace.ActiveDocument;
		var tab = FindTabWithCanvas ((CanvasWindow) doc.Workspace.CanvasWindow);

		if (tab != null)
			canvas_notebook.ActiveItem = tab;

		doc.Workspace.GrabFocusToCanvas ();
	}

	private IDockNotebookItem? FindTabWithCanvas (CanvasWindow canvas_window) =>
		canvas_notebook.Items
		.Where (i => ((CanvasWindow) i.Widget) == canvas_window)
		.FirstOrDefault ();
}
