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

	// Paint.NET order: New, Open, Save | Cut, Copy, Paste, Crop, Deselect | Undo, Redo | Pixel Grid, Rulers
	private Gtk.Widget[] CreateToolBarItems () => [
		File.New.CreateToolBarItem (),
		File.Open.CreateToolBarItem (),
		File.Save.CreateToolBarItem (),
		// Printing is disabled for now until it is fully functional.
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

	public void CreateStatusBar (Gtk.Box statusbar, WorkspaceManager workspaceManager)
	{
		// Cursor position widget - left aligned with enough space to display coordinates up to tens of thousands (e.g. 10000, 10000).
		statusbar.Append (Gtk.Image.NewFromIconName (Resources.Icons.CursorPosition));
		var cursor = Gtk.Label.New ("0, 0");
		cursor.Xalign = 0.0f;
		cursor.Halign = Gtk.Align.Start;
		cursor.WidthChars = 11;
		statusbar.Append (cursor);

		chrome.LastCanvasCursorPointChanged += delegate {
			var pt = chrome.LastCanvasCursorPoint;
			cursor.SetText ($"{pt.X}, {pt.Y}");
		};

		// Selection size widget - left aligned with enough space to display coordinates up to tens of thousands (e.g. 10000, 10000).
		statusbar.Append (Gtk.Image.NewFromIconName (Resources.Icons.ToolSelectRectangle));
		var selection_size = Gtk.Label.New ("0, 0");
		selection_size.Xalign = 0.0f;
		selection_size.Halign = Gtk.Align.Start;
		selection_size.WidthChars = 11;
		statusbar.Append (selection_size);

		workspaceManager.SelectionChanged += delegate {
			var bounds = workspaceManager.HasOpenDocuments ? workspaceManager.ActiveDocument.Selection.GetBounds () : new RectangleD ();
			selection_size.SetText ($"{bounds.Width}, {bounds.Height}");
		};

		// Document zoom widget
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
