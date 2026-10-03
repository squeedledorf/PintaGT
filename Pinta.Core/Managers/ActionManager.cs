//
// ActionManager.cs
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

namespace Pinta.Core;

public sealed class ActionManager
{
	public AppActions App { get; }
	public FileActions File { get; }
	public EditActions Edit { get; }
	public ViewActions View { get; }
	public ImageActions Image { get; }
	public LayerActions Layers { get; }
	public AdjustmentsActions Adjustments { get; }
	public EffectsActions Effects { get; }
	public WindowActions Window { get; }
	public HelpActions Help { get; }
	public AddinActions Addins { get; }

	private readonly ChromeManager chrome;
	public ActionManager (
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		PaletteFormatManager paletteFormats,
		PaletteManager palette,
		RecentFileManager recentFiles,
		SystemManager system,
		ToolManager tools,
		WorkspaceManager workspace)
	{
		// --- Action handlers that don't depend on other handlers

		AddinActions addins = new ();
		AdjustmentsActions adjustments = new ();
		AppActions app = new ();
		EditActions edit = new (chrome, paletteFormats, palette, tools, workspace);
		EffectsActions effects = new (chrome);
		ViewActions view = new (chrome, workspace);
		WindowActions window = new (workspace);

		// --- Action handlers that depend on other handlers

		FileActions file = new (system, app, window);
		HelpActions help = new (system, app, addins);
		ImageActions image = new (tools, workspace, view);
		LayerActions layers = new (chrome, imageFormats, recentFiles, tools, workspace, image);

		// --- References to keep

		App = app;
		File = file;
		Edit = edit;
		View = view;
		Image = image;
		Layers = layers;
		Adjustments = adjustments;
		Effects = effects;
		Window = window;
		Help = help;
		Addins = addins;

		this.chrome = chrome;
	}

	public void CreateToolBar (Gtk.Box toolbar)
	{
		foreach (Gtk.Widget item in CreateToolBarItems ())
			toolbar.Append (item);
	}

	public void CreateHeaderToolBar (Adw.HeaderBar header)
	{
		foreach (Gtk.Widget item in CreateToolBarItems ())
			header.PackStart (item);
	}

	// Paint.NET order: New, Open, Save | Print | Cut, Copy, Paste, Crop, Deselect | Undo, Redo | Pixel Grid, Rulers
	private Gtk.Widget[] CreateToolBarItems () => [
		File.New.CreateToolBarItem (),
		File.Open.CreateToolBarItem (),
		File.Save.CreateToolBarItem (),
		GtkExtensions.CreateToolBarSeparator (),
		File.Print.CreateToolBarItem (),
		GtkExtensions.CreateToolBarSeparator (),
		Edit.Cut.CreateToolBarItem (),
		Edit.Copy.CreateToolBarItem (),
		Edit.Paste.CreateToolBarItem (),
		Image.CropToSelection.CreateToolBarItem (),
		Edit.Deselect.CreateToolBarItem (),
		GtkExtensions.CreateToolBarSeparator (),
		Edit.Undo.CreateToolBarItem (),
		Edit.Redo.CreateToolBarItem (),
		GtkExtensions.CreateToolBarSeparator (),
		CreateToggleToolBarItem (View.PixelGrid),
		CreateToggleToolBarItem (View.Rulers),
	];

	// A toggle button reflects the boolean state of the action, unlike the plain button from CreateToolBarItem.
	private static Gtk.ToggleButton CreateToggleToolBarItem (ToggleCommand command)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.ActionName = command.FullName;
		button.IconName = command.IconName;
		button.TooltipText = command.Label;
		return button;
	}

	/// <summary>
	/// The right side of Paint.NET's status bar: image size, cursor position, selection size,
	/// units, and the zoom controls.
	/// </summary>
	public void CreateStatusBar (Gtk.Box statusbar, WorkspaceManager workspaceManager)
	{
		// Each readout has enough room for sizes and coordinates up to tens of thousands (e.g. 10000 × 10000).
		static Gtk.Label AppendReadout (Gtk.Box box, string icon, string tooltip)
		{
			Gtk.Box item = Gtk.Box.New (Gtk.Orientation.Horizontal, 3);
			item.TooltipText = tooltip;
			item.Append (Gtk.Image.NewFromIconName (icon));
			Gtk.Label label = Gtk.Label.New (string.Empty);
			label.Xalign = 0.0f;
			label.WidthChars = 11;
			item.Append (label);
			box.Append (item);
			return label;
		}

		Gtk.Label image_size = AppendReadout (statusbar, Resources.Icons.ImageResize, Translations.GetString ("Image Size"));
		Gtk.Label cursor = AppendReadout (statusbar, Resources.Icons.CursorPosition, Translations.GetString ("Cursor Position"));
		Gtk.Label selection_size = AppendReadout (statusbar, Resources.Icons.ToolSelectRectangle, Translations.GetString ("Selection Size"));

		// The readouts use the View menu's units, as in Paint.NET.
		int units = 0;
		string Length (double pixels) => ViewActions.FormatLength (pixels, units);

		void UpdateImageSize ()
		{
			Size? size = workspaceManager.ActiveDocumentOrDefault?.ImageSize;
			image_size.SetText (size is Size s ? $"{Length (s.Width)} × {Length (s.Height)}" : string.Empty);
		}

		void UpdateCursor ()
		{
			var pt = chrome.LastCanvasCursorPoint;
			cursor.SetText ($"{Length (pt.X)}, {Length (pt.Y)}");
		}

		// As in Paint.NET, the selection size only shows while there is a selection.
		void UpdateSelectionSize ()
		{
			Document? document = workspaceManager.ActiveDocumentOrDefault;
			bool visible = document is not null && document.Selection.Visible;
			selection_size.Parent!.Visible = visible;
			if (visible) {
				RectangleD bounds = document!.Selection.GetBounds ();
				selection_size.SetText ($"{Length (bounds.Width)} × {Length (bounds.Height)}");
			}
		}

		chrome.LastCanvasCursorPointChanged += delegate { UpdateCursor (); };
		workspaceManager.SelectionChanged += (_, _) => UpdateSelectionSize ();
		workspaceManager.ActiveDocumentChanged += (_, _) => { UpdateImageSize (); UpdateSelectionSize (); };
		workspaceManager.ViewSizeChanged += (_, _) => UpdateImageSize ();
		UpdateImageSize ();
		UpdateSelectionSize ();

		// Units, as View > Pixels / Inches / Centimeters.
		Gio.Menu units_menu = Gio.Menu.New ();
		units_menu.Append (Translations.GetString ("Pixels"), $"app.{View.RulerMetric.Name}(0)");
		units_menu.Append (Translations.GetString ("Inches"), $"app.{View.RulerMetric.Name}(1)");
		units_menu.Append (Translations.GetString ("Centimeters"), $"app.{View.RulerMetric.Name}(2)");

		Gtk.MenuButton unitsButton = Gtk.MenuButton.New ();
		unitsButton.MenuModel = units_menu;
		unitsButton.Direction = Gtk.ArrowType.Up;
		unitsButton.TooltipText = Translations.GetString ("Units");
		unitsButton.AddCssClass (AdwaitaStyles.Flat);
		statusbar.Append (unitsButton);

		void UpdateUnits (GLib.Variant? state)
		{
			units = state?.GetInt32 () ?? 0;
			unitsButton.Label = units switch {
				1 => Translations.GetString ("in"),
				2 => Translations.GetString ("cm"),
				_ => Translations.GetString ("px"),
			};
			UpdateImageSize ();
			UpdateCursor ();
			UpdateSelectionSize ();
		}
		View.RulerMetric.OnActivate += (_, args) => UpdateUnits (args.Parameter);
		UpdateUnits (View.RulerMetric.GetState ());

		// Document zoom widgets
		View.CreateStatusBar (statusbar);
	}

	public void RegisterHandlers ()
	{
		File.RegisterHandlers ();
		Edit.RegisterHandlers ();
		Image.RegisterHandlers ();
		Layers.RegisterHandlers ();
		View.RegisterHandlers ();
		Help.RegisterHandlers ();
	}
}
