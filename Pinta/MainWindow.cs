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
	Dock dock = null!;
	Gio.Menu menu_bar = null!;
	Gio.Menu image_menu = null!;
	Gio.Menu view_menu = null!;

	CanvasPad canvas_pad = null!;

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
		PintaCore.Actions.View.ZoomToWindow.Activated += ZoomToWindow_Activated;
		PintaCore.Actions.View.ZoomToSelection.Activated += ZoomToSelection_Activated;

		PintaCore.Workspace.ActiveDocumentChanged += ActiveDocumentChanged;

		PintaCore.Workspace.DocumentActivated += Workspace_DocumentCreated;
		PintaCore.Workspace.DocumentClosed += Workspace_DocumentClosed;

		DockNotebook notebook = canvas_pad.Notebook;
		notebook.TabClosed += DockNotebook_TabClosed;
		notebook.ActiveTabChanged += DockNotebook_ActiveTabChanged;
	}

	private void Workspace_DocumentClosed (object? sender, DocumentEventArgs e)
	{
		var tab = FindTabWithCanvas ((CanvasWindow) e.Document.Workspace.CanvasWindow);

		if (tab != null)
			canvas_pad.Notebook.RemoveTab (tab);
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
		var item = canvas_pad.Notebook.ActiveItem;

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

		var notebook = canvas_pad.Notebook;
		int selected_index = notebook.ActiveItemIndex;

		CanvasWindow canvas = CanvasWindow.New (
			PintaCore.Chrome,
			PintaCore.Tools,
			doc, PintaCore.CanvasGrid, PintaCore.Settings);
		canvas.RulersVisible = PintaCore.Actions.View.Rulers.Value;
		canvas.RulerMetric = GetCurrentRulerMetric ();
		doc.Workspace.CanvasWindow = canvas;
		doc.Workspace.Canvas = canvas.Canvas;

		DocumentViewContent my_content = new (doc, canvas);

		// Insert our tab to the right of the currently selected tab
		notebook.InsertTab (my_content, selected_index + 1);

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

		CreateMainToolBar ();
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
		menuBar.AppendSubmenu (Translations.GetString ("_Help"), helpMenu);

		// --- Global initializations

		if (usingMenuBar)
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
	}

	private void CreateMainToolBar ()
	{
		if (window_shell.HeaderBar is not null) {
			var headerBar = window_shell.HeaderBar;
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
		} else {
			var main_toolbar = window_shell.CreateToolBar ("main_toolbar");
			PintaCore.Actions.CreateToolBar (main_toolbar);
		}
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

		// The palette stays here until the Colors window replaces it.
		StatusBarColorPaletteWidget widget = StatusBarColorPaletteWidget.New (PintaCore.Chrome, PintaCore.Palette, PintaCore.System);

		// Without Hexpand the drawing area has no natural width and would be allocated 0px, so request what it draws.
		// ponytail: mirrors PaletteWidget's layout (50px color area, 19px swatches in 2 rows, 10px margin); goes away when 2A removes this widget.
		void UpdatePaletteWidth ()
		{
			int recentColumns = PintaCore.Palette.MaxRecentlyUsedColor / 2;
			int paletteColumns = (PintaCore.Palette.CurrentPalette.Colors.Count + 1) / 2;
			widget.WidthRequest = 50 + 19 * (recentColumns + paletteColumns) + 10 + 2;
		}
		UpdatePaletteWidth ();
		PintaCore.Palette.CurrentPalette.PaletteChanged += (_, _) => UpdatePaletteWidth ();

		statusbar.Append (widget);

		PintaCore.Actions.CreateStatusBar (statusbar, PintaCore.Workspace);

		PintaCore.Chrome.InitializeStatusBar (statusbar);
	}

	private void CreatePanels ()
	{
		Gtk.Box panel_container = window_shell.CreateWorkspace ();
		CreateDockAndPads (panel_container);
	}

	private void CreateDockAndPads (Gtk.Box container)
	{
		ToolBoxWidget toolbox = ToolBoxWidget.New (PintaCore.Tools);

		Gtk.ScrolledWindow toolbox_scroll = Gtk.ScrolledWindow.New ();
		toolbox_scroll.Child = toolbox;
		toolbox_scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		toolbox_scroll.VscrollbarPolicy = Gtk.PolicyType.Never;
		toolbox_scroll.HasFrame = false;
		toolbox_scroll.OverlayScrolling = true;
		toolbox_scroll.WindowPlacement = Gtk.CornerType.BottomRight;

		container.Append (toolbox_scroll);
		PintaCore.Chrome.InitializeToolBox (toolbox);

		// Dock widget
		dock = Dock.New ();
		dock.Hexpand = true;
		dock.Halign = Gtk.Align.Fill;
		PintaCore.Chrome.InitializeDock (dock);

		// Canvas pad
		canvas_pad = new CanvasPad ();
		canvas_pad.Initialize (dock);
		PintaCore.Chrome.InitializeImageTabsNotebook (canvas_pad.Notebook);

		// History pad (above Layers, as in Paint.NET)
		HistoryPad history_pad = new (PintaCore.Actions.Edit);
		history_pad.Initialize (dock);

		// Layer pad
		LayersPad layers_pad = new (PintaCore.Actions.Layers);
		layers_pad.Initialize (dock);

		container.Append (dock);
	}

	private void LoadUserSettings ()
	{
		dock.LoadSettings (PintaCore.Settings);

		// Set selected tool to last selected or default to the PaintBrush
		PintaCore.Tools.SetCurrentTool (PintaCore.Settings.GetSetting (SettingNames.LAST_SELECTED_TOOL, "PaintBrushTool"));

		PintaCore.Actions.View.Rulers.Value = PintaCore.Settings.GetSetting (SettingNames.RULER_SHOWN, false);
		PintaCore.Actions.View.ToolBar.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBAR_SHOWN, true);
		PintaCore.Actions.View.StatusBar.Value = PintaCore.Settings.GetSetting (SettingNames.STATUSBAR_SHOWN, true);
		PintaCore.Actions.View.ToolBox.Value = PintaCore.Settings.GetSetting (SettingNames.TOOLBOX_SHOWN, true);
		PintaCore.Actions.View.ImageTabs.Value = PintaCore.Settings.GetSetting (SettingNames.IMAGE_TABS_SHOWN, true);
		PintaCore.Actions.View.ToolWindows.Value = PintaCore.Settings.GetSetting (SettingNames.TOOL_WINDOWS_SHOWN, true);

		string dialog_uri = PintaCore.Settings.GetSetting (SettingNames.LAST_DIALOG_DIRECTORY, PintaCore.RecentFiles.DefaultDialogDirectory?.GetUri () ?? "");
		PintaCore.RecentFiles.LastDialogDirectory = Gio.FileHelper.NewForUri (dialog_uri);

		MetricType ruler_metric = (MetricType) PintaCore.Settings.GetSetting (SettingNames.RULER_METRIC, (int) MetricType.Pixels);
		PintaCore.Actions.View.RulerMetric.Activate (GLib.Variant.NewInt32 ((int) ruler_metric));
	}

	private void SaveUserSettings ()
	{
		dock.SaveSettings (PintaCore.Settings);

		// Don't store the maximized height if the window is maximized
		if (!window_shell.Window.IsMaximized ()) {
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_WIDTH, window_shell.Window.GetWidth ());
			PintaCore.Settings.PutSetting (SettingNames.WINDOW_SIZE_HEIGHT, window_shell.Window.GetHeight ());
		}

		PintaCore.Settings.PutSetting (SettingNames.RULER_METRIC, (int) GetCurrentRulerMetric ());
		PintaCore.Settings.PutSetting (SettingNames.WINDOW_MAXIMIZED, window_shell.Window.IsMaximized ());
		PintaCore.Settings.PutSetting (SettingNames.RULER_SHOWN, PintaCore.Actions.View.Rulers.Value);
		PintaCore.Settings.PutSetting (SettingNames.IMAGE_TABS_SHOWN, PintaCore.Actions.View.ImageTabs.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOL_WINDOWS_SHOWN, PintaCore.Actions.View.ToolWindows.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBAR_SHOWN, PintaCore.Actions.View.ToolBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.STATUSBAR_SHOWN, PintaCore.Actions.View.StatusBar.Value);
		PintaCore.Settings.PutSetting (SettingNames.TOOLBOX_SHOWN, PintaCore.Actions.View.ToolBox.Value);
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

	private void ZoomToWindow_Activated (object sender, EventArgs e)
	{
		// The image is small enough to fit in the window
		if (PintaCore.Workspace.ImageFitsInWindow) {
			PintaCore.Actions.View.ActualSize.Activate ();
		} else {
			int image_x = PintaCore.Workspace.ImageSize.Width;
			int image_y = PintaCore.Workspace.ImageSize.Height;

			var canvas_viewport = PintaCore.Workspace.ActiveWorkspace.Canvas.Parent!;

			int window_x = canvas_viewport.GetAllocatedWidth ();
			int window_y = canvas_viewport.GetAllocatedHeight ();

			double ratio =
				(image_x / (double) window_x >= image_y / (double) window_y)
				? (window_x - 20) / (double) image_x
				: (window_y - 20) / (double) image_y;

			// The image is more constrained by width than height

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
			canvas_pad.Notebook.ActiveItem = tab;

		doc.Workspace.GrabFocusToCanvas ();
	}

	private IDockNotebookItem? FindTabWithCanvas (CanvasWindow canvas_window) =>
		canvas_pad.Notebook.Items
		.Where (i => ((CanvasWindow) i.Widget) == canvas_window)
		.FirstOrDefault ();
}
