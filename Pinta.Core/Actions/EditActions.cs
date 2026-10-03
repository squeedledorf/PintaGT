//
// EditActions.cs
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
using Cairo;

namespace Pinta.Core;

public sealed class EditActions
{
	public Command Undo { get; }
	public Command Redo { get; }
	public Command Cut { get; }
	public Command Copy { get; }
	public Command CopyMerged { get; }
	public Command Paste { get; }
	public Command PasteIntoNewLayer { get; }
	public Command PasteIntoNewImage { get; }
	public Command CopySelection { get; }
	public Command PasteSelectionReplace { get; }
	public Command PasteSelectionUnion { get; }
	public Command PasteSelectionExclude { get; }
	public Command PasteSelectionIntersect { get; }
	public Command PasteSelectionXor { get; }
	public Command EraseSelection { get; }
	public Command FillSelection { get; }
	public Command FillSelectionSecondary { get; }
	public Command InvertSelection { get; }
	public Command OffsetSelection { get; }
	public Command SelectAll { get; }
	public Command Deselect { get; }
	public Command LoadPalette { get; }
	public Command SavePalette { get; }
	public Command ResetPalette { get; }
	public Command ResizePalette { get; }

	private Gio.File? last_palette_dir = null;
	private Document? active_document = null;

	private readonly ChromeManager chrome;
	private readonly PaletteFormatManager palette_formats;
	private readonly PaletteManager palette;
	private readonly ToolManager tools;
	private readonly WorkspaceManager workspace;
	public EditActions (
		ChromeManager chrome,
		PaletteFormatManager paletteFormats,
		PaletteManager palette,
		ToolManager tools,
		WorkspaceManager workspace)
	{
		Undo = new Command (
			"undo",
			Translations.GetString ("Undo"),
			null,
			Resources.StandardIcons.EditUndo,
			shortcuts: ["<Primary>Z"]);

		Redo = new Command (
			"redo",
			Translations.GetString ("Redo"),
			null,
			Resources.StandardIcons.EditRedo,
			shortcuts: ["<Primary>Y"]);

		Cut = new Command (
			"cut",
			Translations.GetString ("Cut"),
			null,
			Resources.StandardIcons.EditCut,
			shortcuts: ["<Primary>X", "<Shift>Delete"]);

		Copy = new Command (
			"copy",
			Translations.GetString ("Copy"),
			null,
			Resources.StandardIcons.EditCopy,
			shortcuts: ["<Primary>C", "<Primary>Insert"]);

		CopyMerged = new Command (
			"copymerged",
			Translations.GetString ("Copy Merged"),
			null,
			Resources.StandardIcons.EditCopy,
			shortcuts: ["<Primary><Shift>C"]);

		Paste = new Command (
			"paste",
			Translations.GetString ("Paste"),
			null,
			Resources.StandardIcons.EditPaste,
			shortcuts: ["<Primary>V", "<Shift>Insert"]);

		PasteIntoNewLayer = new Command (
			"pasteintonewlayer",
			Translations.GetString ("Paste into New Layer"),
			null,
			Resources.StandardIcons.EditPaste,
			shortcuts: ["<Primary><Shift>V"]);

		// Note: <Ctrl><Alt>V shortcut doesn't seem to work on Windows & macOS (bug 2047921).
		PasteIntoNewImage = new Command (
			"pasteintonewimage",
			Translations.GetString ("Paste into New Image"),
			null,
			Resources.StandardIcons.EditPaste,
			shortcuts: ["<Primary><Alt>V"]);

		CopySelection = new Command (
			"copyselection",
			Translations.GetString ("Copy Selection"),
			null,
			Resources.StandardIcons.EditCopy,
			shortcuts: ["<Primary><Alt><Shift>C"]);

		PasteSelectionReplace = new Command (
			"pasteselectionreplace",
			Translations.GetString ("Replace"),
			null,
			Resources.StandardIcons.EditPaste,
			shortcuts: ["<Primary><Alt><Shift>V"]);

		PasteSelectionUnion = new Command (
			"pasteselectionunion",
			Translations.GetString ("Add (union)"),
			null,
			Resources.StandardIcons.EditPaste);

		PasteSelectionExclude = new Command (
			"pasteselectionexclude",
			Translations.GetString ("Exclude"),
			null,
			Resources.StandardIcons.EditPaste);

		PasteSelectionIntersect = new Command (
			"pasteselectionintersect",
			Translations.GetString ("Intersect"),
			null,
			Resources.StandardIcons.EditPaste);

		PasteSelectionXor = new Command (
			"pasteselectionxor",
			Translations.GetString ("Invert (\"xor\")"),
			null,
			Resources.StandardIcons.EditPaste);

		EraseSelection = new Command (
			"eraseselection",
			Translations.GetString ("Erase Selection"),
			null,
			Resources.Icons.EditSelectionErase,
			shortcuts: ["Delete"]);

		FillSelection = new Command (
			"fillselection",
			Translations.GetString ("Fill Selection"),
			null,
			Resources.Icons.EditSelectionFill,
			shortcuts: ["BackSpace"]);

		// Not shown in any menu; Paint.NET fills with the secondary color on Shift+Backspace.
		FillSelectionSecondary = new Command (
			"fillselectionsecondary",
			Translations.GetString ("Fill Selection with Secondary Color"),
			null,
			Resources.Icons.EditSelectionFill,
			shortcuts: ["<Shift>BackSpace"]);

		InvertSelection = new Command (
			"invertselection",
			Translations.GetString ("Invert Selection"),
			null,
			Resources.Icons.EditSelectionFill,
			shortcuts: ["<Primary>I"]);

		OffsetSelection = new Command (
			"offsetselection",
			Translations.GetString ("Offset Selection"),
			null,
			Resources.Icons.EditSelectionOffset,
			shortcuts: ["<Primary><Shift>O"]);

		SelectAll = new Command (
			"selectall",
			Translations.GetString ("Select All"),
			null,
			Resources.StandardIcons.EditSelectAll,
			shortcuts: ["<Primary>A"]);

		Deselect = new Command (
			"deselect",
			Translations.GetString ("Deselect"),
			null,
			Resources.Icons.EditSelectionNone,
			shortcuts: ["<Primary>D"]);

		LoadPalette = new Command (
			"loadpalette",
			Translations.GetString ("Open..."),
			null,
			Resources.StandardIcons.DocumentOpen);

		SavePalette = new Command (
			"savepalette",
			Translations.GetString ("Save As..."),
			null,
			Resources.StandardIcons.DocumentSave);

		ResetPalette = new Command (
			"resetpalette",
			Translations.GetString ("Reset to Default"),
			null,
			Resources.StandardIcons.DocumentRevert);

		ResizePalette = new Command (
			"resizepalette",
			Translations.GetString ("Set Number of Colors"),
			null,
			Resources.Icons.ImageResize);

		Undo.Sensitive = false;
		Redo.Sensitive = false;
		FillSelectionSecondary.Sensitive = false;

		this.chrome = chrome;
		palette_formats = paletteFormats;
		this.palette = palette;
		this.tools = tools;
		this.workspace = workspace;
	}

	public void RegisterActions (Gtk.Application app, Gio.Menu menu)
	{
		Gio.Menu paste_section = Gio.Menu.New ();
		paste_section.AppendItem (Cut.CreateMenuItem ());
		paste_section.AppendItem (Copy.CreateMenuItem ());
		paste_section.AppendItem (CopyMerged.CreateMenuItem ());
		paste_section.AppendItem (Paste.CreateMenuItem ());
		paste_section.AppendItem (PasteIntoNewLayer.CreateMenuItem ());
		paste_section.AppendItem (PasteIntoNewImage.CreateMenuItem ());

		// Paint.NET: Copy Selection, then a Paste Selection submenu of combine modes.
		Gio.Menu paste_selection_menu = Gio.Menu.New ();
		paste_selection_menu.AppendItem (PasteSelectionReplace.CreateMenuItem ());
		paste_selection_menu.AppendItem (PasteSelectionUnion.CreateMenuItem ());
		paste_selection_menu.AppendItem (PasteSelectionExclude.CreateMenuItem ());
		paste_selection_menu.AppendItem (PasteSelectionIntersect.CreateMenuItem ());
		paste_selection_menu.AppendItem (PasteSelectionXor.CreateMenuItem ());

		Gio.Menu geometry_section = Gio.Menu.New ();
		geometry_section.AppendItem (CopySelection.CreateMenuItem ());
		geometry_section.AppendSubmenu (Translations.GetString ("Paste Selection"), paste_selection_menu);

		// Paint.NET order. Offset Selection is a Pinta extra kept after Invert Selection.
		// The palette commands stay registered but are not in this menu (Paint.NET keeps them in the Colors window).
		Gio.Menu sel_section = Gio.Menu.New ();
		sel_section.AppendItem (EraseSelection.CreateMenuItem ());
		sel_section.AppendItem (FillSelection.CreateMenuItem ());
		sel_section.AppendItem (InvertSelection.CreateMenuItem ());
		sel_section.AppendItem (OffsetSelection.CreateMenuItem ());
		sel_section.AppendItem (SelectAll.CreateMenuItem ());
		sel_section.AppendItem (Deselect.CreateMenuItem ());

		menu.AppendItem (Undo.CreateMenuItem ());
		menu.AppendItem (Redo.CreateMenuItem ());
		menu.AppendSection (null, paste_section);
		menu.AppendSection (null, geometry_section);
		menu.AppendSection (null, sel_section);

		app.AddCommands ([

			Undo,
			Redo,

			Cut,
			Copy,
			CopyMerged,
			Paste,
			PasteIntoNewLayer,
			PasteIntoNewImage,

			CopySelection,
			PasteSelectionReplace,
			PasteSelectionUnion,
			PasteSelectionExclude,
			PasteSelectionIntersect,
			PasteSelectionXor,

			SelectAll,
			Deselect,

			EraseSelection,
			FillSelection,
			FillSelectionSecondary,
			InvertSelection,
			OffsetSelection,
			LoadPalette,
			SavePalette,
			ResetPalette,
			ResizePalette]);
	}

	public void CreateHistoryWindowToolBar (Gtk.Box toolbar)
	{
		toolbar.Append (Undo.CreateToolBarItem ());
		toolbar.Append (Redo.CreateToolBarItem ());
	}

	public void RegisterHandlers ()
	{
		Deselect.Activated += HandlePintaCoreActionsEditDeselectActivated;
		EraseSelection.Activated += HandlePintaCoreActionsEditEraseSelectionActivated;
		SelectAll.Activated += HandlePintaCoreActionsEditSelectAllActivated;
		FillSelection.Activated += HandlePintaCoreActionsEditFillSelectionActivated;
		FillSelectionSecondary.Activated += HandleFillSelectionSecondaryActivated;
		Copy.Activated += HandlerPintaCoreActionsEditCopyActivated;
		CopyMerged.Activated += HandlerPintaCoreActionsEditCopyMergedActivated;
		Undo.Activated += HandlerPintaCoreActionsEditUndoActivated;
		Redo.Activated += HandlerPintaCoreActionsEditRedoActivated;
		Cut.Activated += HandlerPintaCoreActionsEditCutActivated;
		LoadPalette.Activated += HandlerPintaCoreActionsEditLoadPaletteActivated;
		SavePalette.Activated += HandlerPintaCoreActionsEditSavePaletteActivated;
		ResetPalette.Activated += HandlerPintaCoreActionsEditResetPaletteActivated;
		InvertSelection.Activated += HandleInvertSelectionActivated;
		CopySelection.Activated += HandleCopySelectionActivated;
		PasteSelectionReplace.Activated += (_, _) => PasteSelection (CombineMode.Replace);
		PasteSelectionUnion.Activated += (_, _) => PasteSelection (CombineMode.Union);
		PasteSelectionExclude.Activated += (_, _) => PasteSelection (CombineMode.Exclude);
		PasteSelectionIntersect.Activated += (_, _) => PasteSelection (CombineMode.Intersect);
		PasteSelectionXor.Activated += (_, _) => PasteSelection (CombineMode.Xor);

		workspace.ActiveDocumentChanged += WorkspaceActiveDocumentChanged;

		workspace.SelectionChanged += (o, _) => {
			var visible = false;
			if (workspace.HasOpenDocuments)
				visible = workspace.ActiveDocument.Selection.Visible;

			// As in Paint.NET, with no selection Cut, Copy and Copy Merged act on the whole layer or image
			// (a hidden selection covers the whole canvas).
			bool hasDocument = workspace.HasOpenDocuments;
			Cut.Sensitive = hasDocument;
			Copy.Sensitive = hasDocument;
			CopyMerged.Sensitive = hasDocument;
			Deselect.Sensitive = visible;
			EraseSelection.Sensitive = visible;
			FillSelection.Sensitive = visible;
			FillSelectionSecondary.Sensitive = visible;
			InvertSelection.Sensitive = visible;
			OffsetSelection.Sensitive = visible;
			CopySelection.Sensitive = visible;
		};
	}

	#region Action Handlers
	private void HandlePintaCoreActionsEditFillSelectionActivated (object sender, EventArgs e)
		=> FillSelectionWith (palette.PrimaryColor);

	private void HandleFillSelectionSecondaryActivated (object sender, EventArgs e)
		=> FillSelectionWith (palette.SecondaryColor);

	private void FillSelectionWith (Color color)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		ImageSurface old = doc.Layers.CurrentUserLayer.Surface.Clone ();

		using Context g = new (doc.Layers.CurrentUserLayer.Surface);

		g.AppendPath (doc.Selection.SelectionPath);
		g.FillRule = FillRule.EvenOdd;

		g.SetSourceColor (color);
		g.Fill ();

		doc.Workspace.Invalidate ();
		doc.History.PushNewItem (
			new SimpleHistoryItem (
				Resources.Icons.EditSelectionFill,
				Translations.GetString ("Fill Selection"),
				old,
				doc.Layers.CurrentUserLayerIndex
			)
		);
	}

	private void HandlePintaCoreActionsEditSelectAllActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		SelectionHistoryItem hist = new (
			workspace,
			Resources.StandardIcons.EditSelectAll,
			Translations.GetString ("Select All"));

		hist.TakeSnapshot ();

		doc.ResetSelectionPaths ();
		doc.Selection.Visible = true;

		doc.History.PushNewItem (hist);
		doc.Workspace.Invalidate ();
	}

	private void HandlePintaCoreActionsEditEraseSelectionActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		ImageSurface old = doc.Layers.CurrentUserLayer.Surface.Clone ();

		using Context g = new (doc.Layers.CurrentUserLayer.Surface);

		g.AppendPath (doc.Selection.SelectionPath);
		g.FillRule = FillRule.EvenOdd;

		g.Operator = Cairo.Operator.Clear;
		g.Fill ();

		doc.Workspace.Invalidate ();

		doc.History.PushNewItem (
			sender switch {
				string and "Cut" => new SimpleHistoryItem (Resources.StandardIcons.EditCut, Translations.GetString ("Cut"), old, doc.Layers.CurrentUserLayerIndex),
				_ => new SimpleHistoryItem (Resources.Icons.EditSelectionErase, Translations.GetString ("Erase Selection"), old, doc.Layers.CurrentUserLayerIndex),
			}
		);
	}

	private void HandlePintaCoreActionsEditDeselectActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		SelectionHistoryItem hist = new (
			workspace,
			Resources.Icons.EditSelectionNone,
			Translations.GetString ("Deselect"));

		hist.TakeSnapshot ();

		doc.ResetSelectionPaths ();

		doc.History.PushNewItem (hist);
		doc.Workspace.Invalidate ();
	}

	private void HandlerPintaCoreActionsEditCopyActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;
		Gdk.Clipboard cb = GdkExtensions.GetDefaultClipboard ();
		if (tools.CurrentTool?.DoHandleCopy (doc, cb) == true)
			return;

		tools.Commit ();

		CopyLayersToClipboard (doc, doc.Layers.CurrentUserLayer.GetLayersToPaint (), cb);
	}

	private void HandlerPintaCoreActionsEditCopyMergedActivated (object sender, EventArgs e)
	{
		tools.Commit ();

		Document doc = workspace.ActiveDocument;
		Gdk.Clipboard cb = GdkExtensions.GetDefaultClipboard ();
		CopyLayersToClipboard (doc, doc.Layers.GetLayersToPaint (), cb);
	}

	private static void CopyLayersToClipboard (Document doc, IEnumerable<Layer> layers, Gdk.Clipboard clipboard)
	{
		RectangleI rect = doc.GetSelectedBounds (true);
		if (rect.IsEmpty)
			return;

		using ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, rect.Width, rect.Height);
		using Context g = new (dest);

		// Move the selected region to the upper left of the target surface, and clip to the original selection.
		g.Translate (-rect.X, -rect.Y);
		doc.Selection.Clip (g);

		foreach (Layer layer in layers) {
			layer.Draw (g);
		}

		CopyImageToClipboard (clipboard, dest, new PointI (rect.X, rect.Y), doc.Selection);
	}

	/// <summary>
	/// Copy an image to the clipboard.
	/// This records two content providers: one with the image (equivalent to Clipboard.SetTexture()), and
	/// one storing additional custom data that Pinta can check for when pasting.
	/// </summary>
	private static void CopyImageToClipboard (Gdk.Clipboard clipboard, ImageSurface image, PointI srcPos, DocumentSelection selection)
	{
		Gdk.Texture texture = image.ToTexture ();

		ClipboardImageMetadata customImageMetadata = ClipboardImageMetadata.NewWithProperties ([]);
		// Store the original position so we can later paste at the same location,
		// e.g. to cut and paste into a different layer.
		customImageMetadata.Position = srcPos;
		// Store the original selection so we can paste with the correct mask, e.g. if an ellipse
		// selection was used.
		customImageMetadata.Selection = selection.Clone ();

		// This is equivalent to gdk_clipboard_set_texture(), and will copy the image to the clipboard
		// with suitable MIME types etc.
		GObject.Value textureValue = new (Gdk.Texture.GetGType ());
		textureValue.SetObject (texture);
		Gdk.ContentProvider textureProvider = Gdk.ContentProvider.NewForValue (textureValue);

		// Wrap our custom data in a content provider.
		GObject.Value customDataValue = new (ClipboardImageMetadata.GetGType ());
		customDataValue.SetObject (customImageMetadata);
		Gdk.ContentProvider customProvider = Gdk.ContentProvider.NewForValue (customDataValue);

		// Combine the two providers and add it to the clipboard.
		clipboard.SetContent (
			GdkExtensions.CreateContentProviderUnion ([textureProvider, customProvider]));
	}

	private void HandlerPintaCoreActionsEditCutActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		Gdk.Clipboard cb = GdkExtensions.GetDefaultClipboard ();

		if (tools.CurrentTool?.DoHandleCut (doc, cb) == true)
			return;

		tools.Commit ();

		// Copy selection
		HandlerPintaCoreActionsEditCopyActivated (sender, e);

		// Erase selection
		HandlePintaCoreActionsEditEraseSelectionActivated ("Cut", e);
	}

	private void HandlerPintaCoreActionsEditUndoActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		if (tools.CurrentTool?.DoHandleUndo (doc) == true)
			return;

		doc.History.Undo ();

		tools.CurrentTool?.DoAfterUndo (doc);
	}

	private void HandlerPintaCoreActionsEditRedoActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		if (tools.CurrentTool?.DoHandleRedo (doc) == true)
			return;

		doc.History.Redo ();

		tools.CurrentTool?.DoAfterRedo (doc);
	}

	private async void HandlerPintaCoreActionsEditLoadPaletteActivated (object sender, EventArgs e)
	{

		using Gtk.FileFilter palettesFilter = CreatePalettesFilter ();
		using Gtk.FileFilter catchAllFilter = CreateCatchAllFilter ();

		using Gio.ListStore filters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		filters.Append (palettesFilter);
		filters.Append (catchAllFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Open Palette File"));
		fileDialog.SetFilters (filters);
		if (last_palette_dir != null)
			fileDialog.SetInitialFolder (last_palette_dir);

		var choice = await fileDialog.OpenFileAsync (chrome.MainWindow);

		if (choice is null)
			return;

		last_palette_dir = choice.GetParent ();

		try {
			palette.CurrentPalette.Load (palette_formats, choice);

		} catch (PaletteLoadException ex) {

			var parent = chrome.MainWindow;

			await chrome.ShowUnsupportedFormatDialog (
				parent,
				palette_formats.Formats,
				ex.FileName,
				Translations.GetString ("Unsupported palette format"),
				ex.Message);
		}
	}

	private void HandlerPintaCoreActionsEditSavePaletteActivated (object sender, EventArgs e)
	{
		var fcd = Gtk.FileChooserNative.New (
			Translations.GetString ("Save Palette File"),
			chrome.MainWindow,
			Gtk.FileChooserAction.Save,
			Translations.GetString ("Save"),
			Translations.GetString ("Cancel"));

		foreach (var format in palette_formats.Formats) {

			if (format.IsReadOnly ())
				continue;

			Gtk.FileFilter fileFilter = format.Filter;
			fcd.AddFilter (fileFilter);
		}

		if (last_palette_dir != null)
			fcd.SetCurrentFolder (last_palette_dir);

		fcd.OnResponse += (_, args) => {

			Gtk.ResponseType response = (Gtk.ResponseType) args.ResponseId;

			if (response != Gtk.ResponseType.Accept)
				return;

			Gio.File file = fcd.GetFile ()!;

			// Add in the extension if necessary, based on the current selected file filter.
			// Note: on macOS, fcd.Filter doesn't seem to properly update to the current filter.
			// However, on macOS the dialog always adds the extension automatically, so this issue doesn't matter.
			string basename = file.GetParent ()!.GetRelativePath (file)!;
			string extension = System.IO.Path.GetExtension (basename);
			if (string.IsNullOrEmpty (extension)) {
				var currentFormat = palette_formats.Formats.First (f => f.Filter == fcd.Filter);
				basename += "." + currentFormat.Extensions.First ();
				file = file.GetParent ()!.GetChild (basename);
			}

			PaletteDescriptor format = palette_formats.GetFormatByFilename (basename) ?? throw new FormatException ();
			palette.CurrentPalette.Save (file, format.Saver);
			last_palette_dir = file.GetParent ();
		};

		fcd.Show ();
	}

	private Gtk.FileFilter CreatePalettesFilter ()
	{
		Gtk.FileFilter palettesFilter = Gtk.FileFilter.New ();

		palettesFilter.Name = Translations.GetString ("Palette files");

		foreach (var format in palette_formats.Formats) {

			if (format.IsWriteOnly ())
				continue;

			foreach (var ext in format.Extensions)
				palettesFilter.AddPattern ($"*.{ext}");
		}

		return palettesFilter;
	}

	private static Gtk.FileFilter CreateCatchAllFilter ()
	{
		Gtk.FileFilter catchAllFilter = Gtk.FileFilter.New ();
		catchAllFilter.Name = Translations.GetString ("All files");
		catchAllFilter.AddPattern ("*");
		return catchAllFilter;
	}

	private void HandlerPintaCoreActionsEditResetPaletteActivated (object sender, EventArgs e)
	{
		palette.CurrentPalette.LoadDefault ();
	}

	void HandleInvertSelectionActivated (object sender, EventArgs e)
	{
		tools.Commit ();

		Document doc = workspace.ActiveDocument;

		// Clear the selection resize handles if necessary.
		doc.Layers.ToolLayer.Clear ();

		SelectionHistoryItem historyItem = new (
			workspace,
			Resources.Icons.EditSelectionInvert,
			Translations.GetString ("Invert Selection"));

		historyItem.TakeSnapshot ();

		// The inverted selection no longer matches the old handle rectangle, so drop it (the select tools hide their handles).
		doc.Selection.HandleBounds = RectangleD.Zero;
		doc.Selection.Invert (doc.ImageSize);

		doc.History.PushNewItem (historyItem);
		doc.Workspace.Invalidate ();
	}

	private void HandleCopySelectionActivated (object sender, EventArgs e)
	{
		tools.Commit ();

		Document doc = workspace.ActiveDocument;
		GdkExtensions.GetDefaultClipboard ().SetText (DocumentSelection.ToPolygonListJson (doc.Selection.SelectionPolygons));
	}

	private async void PasteSelection (CombineMode mode)
	{
		Document doc = workspace.ActiveDocument;

		string? text;
		try {
			text = await GdkExtensions.GetDefaultClipboard ().ReadTextAsync ();
		} catch (GLib.GException) {
			text = null; // No text on the clipboard (e.g. an image).
		}

		List<List<ClipperLib.IntPoint>>? polygons = DocumentSelection.ParsePolygonListJson (text);
		if (polygons is null || !workspace.HasOpenDocuments || workspace.ActiveDocument != doc)
			return;

		tools.Commit ();

		SelectionHistoryItem hist = new (
			workspace,
			Resources.StandardIcons.EditPaste,
			Translations.GetString ("Paste Selection"));
		hist.TakeSnapshot ();

		// A hidden selection covers the whole canvas; as in Paint.NET it counts as nothing selected here.
		doc.PreviousSelection = doc.Selection.Clone ();
		if (!doc.Selection.Visible)
			doc.PreviousSelection.SelectionPolygons = [];
		// The pasted shape has no handle rectangle, so the select tools hide their handles.
		// Cleared before combining, because the combine raises the selection-changed event the tools listen to.
		doc.PreviousSelection.HandleBounds = RectangleD.Zero;

		SelectionModeHandler.PerformSelectionMode (doc, mode, polygons);
		// Exclude/Intersect can leave nothing; as in Paint.NET that is "nothing selected", not an empty visible selection.
		if (doc.Selection.SelectionPolygons.Count == 0)
			doc.ResetSelectionPaths ();

		doc.History.PushNewItem (hist);
		doc.Workspace.Invalidate ();
	}

	private void WorkspaceActiveDocumentChanged (object? sender, EventArgs e)
	{
		if (active_document != null) {
			active_document.History.HistoryItemAdded -= OnDocumentHistoryChanged;
			active_document.History.ActionRedone -= OnDocumentHistoryChanged;
			active_document.History.ActionUndone -= OnDocumentHistoryChanged;
		}

		active_document = workspace.ActiveDocumentOrDefault;

		// Register event handlers for the new document to update the undo/redo state.
		if (active_document != null) {
			active_document.History.HistoryItemAdded += OnDocumentHistoryChanged;
			active_document.History.ActionRedone += OnDocumentHistoryChanged;
			active_document.History.ActionUndone += OnDocumentHistoryChanged;
		}

		// Force an update of the undo/redo state after switching documents.
		UpdateUndoRedoActions ();
	}

	private void OnDocumentHistoryChanged (object? sender, EventArgs e)
	{
		UpdateUndoRedoActions ();
	}

	private void UpdateUndoRedoActions ()
	{
		if (active_document != null) {
			Redo.Sensitive = active_document.Workspace.History.CanRedo;
			Undo.Sensitive = active_document.Workspace.History.CanUndo;
		} else {
			Redo.Sensitive = false;
			Undo.Sensitive = false;
		}

	}

	#endregion
}
