//
// LayerActions.cs
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

namespace Pinta.Core;

public sealed class LayerActions
{
	public Command AddNewLayer { get; }
	public Command DeleteLayer { get; }
	public Command DuplicateLayer { get; }
	public Command MergeLayerDown { get; }
	public Command ImportFromFile { get; }
	public Command FlipHorizontal { get; }
	public Command FlipVertical { get; }
	public Command RotateZoom { get; }
	public Command MoveLayerUp { get; }
	public Command MoveLayerDown { get; }
	public Command Properties { get; }
	public Command ToggleVisibility { get; }
	public Command Rotate180 { get; }
	public Command GoToTopLayer { get; }
	public Command GoToLayerAbove { get; }
	public Command GoToLayerBelow { get; }
	public Command GoToBottomLayer { get; }
	public Command MoveLayerToTop { get; }
	public Command MoveLayerToBottom { get; }

	private readonly ChromeManager chrome;
	private readonly ImageConverterManager image_formats;
	private readonly RecentFileManager recent_files;
	private readonly ToolManager tools;
	private readonly WorkspaceManager workspace;
	private readonly ImageActions image;
	public LayerActions (
		ChromeManager chrome,
		ImageConverterManager imageFormats,
		RecentFileManager recentFiles,
		ToolManager tools,
		WorkspaceManager workspace,
		ImageActions image)
	{
		AddNewLayer = new Command (
			"addnewlayer",
			Translations.GetString ("Add New Layer"),
			null,
			Resources.Icons.LayerNew,
			shortcuts: ["<Primary><Shift>N"]);

		DeleteLayer = new Command (
			"deletelayer",
			Translations.GetString ("Delete Layer"),
			null,
			Resources.Icons.LayerDelete,
			shortcuts: ["<Primary><Shift>Delete"]);

		DuplicateLayer = new Command (
			"duplicatelayer",
			Translations.GetString ("Duplicate Layer"),
			null,
			Resources.Icons.LayerDuplicate,
			shortcuts: ["<Primary><Shift>D"]);

		MergeLayerDown = new Command (
			"mergelayerdown",
			Translations.GetString ("Merge Layer Down"),
			null,
			Resources.Icons.LayerMergeDown,
			shortcuts: ["<Primary>M"]);

		ImportFromFile = new Command (
			"importfromfile",
			Translations.GetString ("Import From File..."),
			null,
			Resources.Icons.LayerImport);

		FlipHorizontal = new Command (
			"fliplayerhorizontal",
			Translations.GetString ("Flip Horizontal"),
			null,
			Resources.Icons.LayerFlipHorizontal);

		FlipVertical = new Command (
			"fliplayervertical",
			Translations.GetString ("Flip Vertical"),
			null,
			Resources.Icons.LayerFlipVertical);

		RotateZoom = new Command (
			"RotateZoom",
			Translations.GetString ("Rotate / Zoom..."),
			null,
			Resources.Icons.LayerRotateZoom,
			shortcuts: ["<Primary><Shift>Z"]);

		MoveLayerUp = new Command (
			"movelayerup",
			Translations.GetString ("Move Layer Up"),
			null,
			Resources.StandardIcons.LayerMoveUp);

		MoveLayerDown = new Command (
			"movelayerdown",
			Translations.GetString ("Move Layer Down"),
			null,
			Resources.StandardIcons.LayerMoveDown);

		Properties = new Command (
			"properties",
			Translations.GetString ("Layer Properties..."),
			null,
			Resources.Icons.LayerProperties,
			shortcuts: ["F4"]);

		ToggleVisibility = new Command (
			"togglelayervisibility",
			Translations.GetString ("Toggle Layer Visibility"),
			null,
			Resources.StandardIcons.ViewReveal,
			shortcuts: ["<Primary>comma"]);

		Rotate180 = new Command (
			"rotatelayer180",
			Translations.GetString ("Rotate 180°"),
			null,
			Resources.Icons.ImageRotate180);

		GoToTopLayer = new Command (
			"gototoplayer",
			Translations.GetString ("Go to Top Layer"),
			null,
			null,
			shortcuts: ["<Primary><Alt>Page_Up"]);

		GoToLayerAbove = new Command (
			"gotolayerabove",
			Translations.GetString ("Go to Layer Above"),
			null,
			null,
			shortcuts: ["<Alt>Page_Up"]);

		GoToLayerBelow = new Command (
			"gotolayerbelow",
			Translations.GetString ("Go to Layer Below"),
			null,
			null,
			shortcuts: ["<Alt>Page_Down"]);

		GoToBottomLayer = new Command (
			"gotobottomlayer",
			Translations.GetString ("Go to Bottom Layer"),
			null,
			null,
			shortcuts: ["<Primary><Alt>Page_Down"]);

		MoveLayerToTop = new Command (
			"movelayertotop",
			Translations.GetString ("Move Layer to Top"),
			null,
			Resources.StandardIcons.LayerMoveUp);

		MoveLayerToBottom = new Command (
			"movelayertobottom",
			Translations.GetString ("Move Layer to Bottom"),
			null,
			Resources.StandardIcons.LayerMoveDown);

		this.chrome = chrome;
		image_formats = imageFormats;
		recent_files = recentFiles;
		this.tools = tools;
		this.workspace = workspace;
		this.image = image;
	}

	public void RegisterActions (Gtk.Application app, Gio.Menu menu)
	{
		// Paint.NET Layers menu order.
		Gio.Menu add_section = Gio.Menu.New ();
		add_section.AppendItem (AddNewLayer.CreateMenuItem ());
		add_section.AppendItem (DeleteLayer.CreateMenuItem ());
		add_section.AppendItem (DuplicateLayer.CreateMenuItem ());
		add_section.AppendItem (MergeLayerDown.CreateMenuItem ());
		add_section.AppendItem (ToggleVisibility.CreateMenuItem ());

		Gio.Menu import_section = Gio.Menu.New ();
		import_section.AppendItem (ImportFromFile.CreateMenuItem ());

		Gio.Menu transform_section = Gio.Menu.New ();
		transform_section.AppendItem (FlipHorizontal.CreateMenuItem ());
		transform_section.AppendItem (FlipVertical.CreateMenuItem ());
		transform_section.AppendItem (Rotate180.CreateMenuItem ());
		transform_section.AppendItem (RotateZoom.CreateMenuItem ());

		Gio.Menu go_to_section = Gio.Menu.New ();
		go_to_section.AppendItem (GoToTopLayer.CreateMenuItem ());
		go_to_section.AppendItem (GoToLayerAbove.CreateMenuItem ());
		go_to_section.AppendItem (GoToLayerBelow.CreateMenuItem ());
		go_to_section.AppendItem (GoToBottomLayer.CreateMenuItem ());

		Gio.Menu move_section = Gio.Menu.New ();
		move_section.AppendItem (MoveLayerToTop.CreateMenuItem ());
		move_section.AppendItem (MoveLayerUp.CreateMenuItem ());
		move_section.AppendItem (MoveLayerDown.CreateMenuItem ());
		move_section.AppendItem (MoveLayerToBottom.CreateMenuItem ());

		Gio.Menu properties_section = Gio.Menu.New ();
		properties_section.AppendItem (Properties.CreateMenuItem ());

		menu.AppendSection (null, add_section);
		menu.AppendSection (null, import_section);
		menu.AppendSection (null, transform_section);
		menu.AppendSection (null, go_to_section);
		menu.AppendSection (null, move_section);
		menu.AppendSection (null, properties_section);

		app.AddCommands ([
			AddNewLayer,
			DeleteLayer,
			DuplicateLayer,
			MergeLayerDown,
			ImportFromFile,

			FlipHorizontal,
			FlipVertical,
			RotateZoom,

			Properties,

			MoveLayerDown,
			MoveLayerUp,

			ToggleVisibility,
			Rotate180,
			GoToTopLayer,
			GoToLayerAbove,
			GoToLayerBelow,
			GoToBottomLayer,
			MoveLayerToTop,
			MoveLayerToBottom]);
	}

	public void RegisterHandlers ()
	{
		AddNewLayer.Activated += HandlePintaCoreActionsLayersAddNewLayerActivated;
		DeleteLayer.Activated += HandlePintaCoreActionsLayersDeleteLayerActivated;
		DuplicateLayer.Activated += HandlePintaCoreActionsLayersDuplicateLayerActivated;
		MergeLayerDown.Activated += HandlePintaCoreActionsLayersMergeLayerDownActivated;
		MoveLayerDown.Activated += HandlePintaCoreActionsLayersMoveLayerDownActivated;
		MoveLayerUp.Activated += HandlePintaCoreActionsLayersMoveLayerUpActivated;
		FlipHorizontal.Activated += HandlePintaCoreActionsLayersFlipHorizontalActivated;
		FlipVertical.Activated += HandlePintaCoreActionsLayersFlipVerticalActivated;
		ImportFromFile.Activated += HandlePintaCoreActionsLayersImportFromFileActivated;
		ToggleVisibility.Activated += HandleToggleVisibilityActivated;
		Rotate180.Activated += HandleRotate180Activated;
		GoToTopLayer.Activated += (_, _) => GoToLayer (doc => doc.Layers.UserLayers.Count - 1);
		GoToLayerAbove.Activated += (_, _) => GoToLayer (doc => doc.Layers.CurrentUserLayerIndex + 1);
		GoToLayerBelow.Activated += (_, _) => GoToLayer (doc => doc.Layers.CurrentUserLayerIndex - 1);
		GoToBottomLayer.Activated += (_, _) => GoToLayer (_ => 0);
		MoveLayerToTop.Activated += HandleMoveLayerToTopActivated;
		MoveLayerToBottom.Activated += HandleMoveLayerToBottomActivated;

		workspace.LayerAdded += EnableOrDisableLayerActions;
		workspace.LayerRemoved += EnableOrDisableLayerActions;
		workspace.SelectedLayerChanged += EnableOrDisableLayerActions;
		workspace.ActiveDocumentChanged += EnableOrDisableLayerActions;

		EnableOrDisableLayerActions (null, EventArgs.Empty);
	}

	private void EnableOrDisableLayerActions (object? sender, EventArgs e)
	{
		Document? activeDoc = workspace.ActiveDocumentOrDefault;

		bool hasMultipleLayers = activeDoc?.Layers.UserLayers.Count > 1;
		DeleteLayer.Sensitive = hasMultipleLayers;
		image.Flatten.Sensitive = hasMultipleLayers;

		bool canMergeDown = activeDoc?.Layers.CurrentUserLayerIndex > 0;
		MergeLayerDown.Sensitive = canMergeDown;
		MoveLayerDown.Sensitive = canMergeDown;
		MoveLayerToBottom.Sensitive = canMergeDown;
		GoToLayerBelow.Sensitive = canMergeDown;
		GoToBottomLayer.Sensitive = canMergeDown;

		bool canMoveUp = activeDoc != null
			&& activeDoc.Layers.CurrentUserLayerIndex < activeDoc.Layers.UserLayers.Count - 1;
		MoveLayerUp.Sensitive = canMoveUp;
		MoveLayerToTop.Sensitive = canMoveUp;
		GoToLayerAbove.Sensitive = canMoveUp;
		GoToTopLayer.Sensitive = canMoveUp;

		ToggleVisibility.Sensitive = activeDoc != null;
		Rotate180.Sensitive = activeDoc != null;
	}

	private void GoToLayer (Func<Document, int> getIndex)
	{
		Document? doc = workspace.ActiveDocumentOrDefault;
		if (doc is null)
			return;

		int index = getIndex (doc);
		if (index < 0 || index >= doc.Layers.UserLayers.Count || index == doc.Layers.CurrentUserLayerIndex)
			return;

		// Changing the selected layer is not recorded in the history.
		doc.Layers.SetCurrentUserLayer (index);
	}

	private void HandleToggleVisibilityActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		UserLayer layer = doc.Layers.CurrentUserLayer;
		bool hide = !layer.Hidden;

		LayerProperties initial = new (layer.Name, layer.Hidden, layer.Opacity, layer.BlendMode);
		LayerProperties updated = new (layer.Name, hide, layer.Opacity, layer.BlendMode);

		UpdateLayerPropertiesHistoryItem historyItem = new (
			hide ? Resources.StandardIcons.ViewConceal : Resources.StandardIcons.ViewReveal,
			hide ? Translations.GetString ("Hide Layer") : Translations.GetString ("Show Layer"),
			doc.Layers.CurrentUserLayerIndex,
			initial,
			updated);

		historyItem.Redo ();

		doc.History.PushNewItem (historyItem);
		doc.Workspace.Invalidate ();
	}

	private void HandleRotate180Activated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		int index = doc.Layers.CurrentUserLayerIndex;
		UserLayer layer = doc.Layers.CurrentUserLayer;
		layer.FlipHorizontal ();
		layer.FlipVertical ();
		doc.Workspace.Invalidate ();

		CompoundHistoryItem hist = new (
			Resources.Icons.ImageRotate180,
			Translations.GetString ("Rotate Layer 180°"));
		hist.Push (new InvertHistoryItem (InvertType.FlipLayerHorizontal, index));
		hist.Push (new InvertHistoryItem (InvertType.FlipLayerVertical, index));
		doc.History.PushNewItem (hist);
	}

	private void HandleMoveLayerToTopActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		CompoundHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveUp,
			Translations.GetString ("Move Layer to Top"));

		while (doc.Layers.CurrentUserLayerIndex < doc.Layers.UserLayers.Count - 1) {
			int index = doc.Layers.CurrentUserLayerIndex;
			hist.Push (new SwapLayersHistoryItem (string.Empty, string.Empty, index, index + 1));
			doc.Layers.MoveCurrentLayerUp ();
		}

		doc.History.PushNewItem (hist);
	}

	private void HandleMoveLayerToBottomActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		CompoundHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveDown,
			Translations.GetString ("Move Layer to Bottom"));

		while (doc.Layers.CurrentUserLayerIndex > 0) {
			int index = doc.Layers.CurrentUserLayerIndex;
			hist.Push (new SwapLayersHistoryItem (string.Empty, string.Empty, index, index - 1));
			doc.Layers.MoveCurrentLayerDown ();
		}

		doc.History.PushNewItem (hist);
	}

	private Gtk.FileFilter CreateImagesFileFilter ()
	{
		Gtk.FileFilter imagesFilter = Gtk.FileFilter.New ();
		foreach (var format in image_formats.Formats) {
			if (!format.IsImportAvailable ()) continue;
			foreach (string ext in format.Extensions)
				imagesFilter.AddPattern ($"*.{ext}");
		}

		// On Unix-like systems, file extensions are often considered optional.
		// Files can often also be identified by their MIME types.
		// Windows does not understand MIME types natively.
		// Adding a MIME filter on Windows would break the native file picker and force a GTK file picker instead.
		if (SystemManager.GetOperatingSystem () != OS.Windows)
			foreach (var format in image_formats.Formats)
				foreach (var mime in format.Mimes)
					imagesFilter.AddMimeType (mime);

		imagesFilter.Name = Translations.GetString ("Image files");

		return imagesFilter;
	}

	private async void HandlePintaCoreActionsLayersImportFromFileActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		// Add image files filter
		using Gtk.FileFilter imagesFilter = CreateImagesFileFilter ();

		using Gio.ListStore fileFilters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		fileFilters.Append (imagesFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Open Image File"));
		fileDialog.SetFilters (fileFilters);
		if (recent_files.GetDialogDirectory () is Gio.File dir && dir.QueryExists (null))
			fileDialog.SetInitialFolder (dir);

		Gio.File? choice = await fileDialog.OpenFileAsync (chrome.MainWindow);

		if (choice is null) return;

		Gio.File? directory = choice.GetParent ();

		if (directory is not null)
			recent_files.LastDialogDirectory = directory;

		// Open the image and add it to the layers
		UserLayer layer = doc.Layers.AddNewLayer (choice.GetDisplayName ());

		using (Gio.FileInputStream fs = choice.Read (null)) {
			try {
				using GdkPixbuf.Pixbuf bg = GdkPixbuf.Pixbuf.NewFromStream (fs, cancellable: null)!; // NRT: only nullable when an error is thrown
				using Cairo.Context context = new (layer.Surface);
				context.DrawPixbuf (bg, PointD.Zero);
			} finally {
				fs.Close (null);
			}
		}

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerImport,
			Translations.GetString ("Import From File"),
			doc.Layers.IndexOf (layer));

		// --- Changes to document go after everything else is completed successfully

		doc.Layers.SetCurrentUserLayer (layer);
		doc.History.PushNewItem (hist);
		doc.Workspace.Invalidate ();
	}

	private void HandlePintaCoreActionsLayersFlipVerticalActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		doc.Layers.CurrentUserLayer.FlipVertical ();
		doc.Workspace.Invalidate ();
		doc.History.PushNewItem (new InvertHistoryItem (InvertType.FlipLayerVertical, doc.Layers.CurrentUserLayerIndex));
	}

	private void HandlePintaCoreActionsLayersFlipHorizontalActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		doc.Layers.CurrentUserLayer.FlipHorizontal ();
		doc.Workspace.Invalidate ();
		doc.History.PushNewItem (new InvertHistoryItem (InvertType.FlipLayerHorizontal, doc.Layers.CurrentUserLayerIndex));
	}

	private void HandlePintaCoreActionsLayersMoveLayerUpActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		SwapLayersHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveUp,
			Translations.GetString ("Move Layer Up"),
			doc.Layers.CurrentUserLayerIndex,
			doc.Layers.CurrentUserLayerIndex + 1);

		doc.Layers.MoveCurrentLayerUp ();
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersMoveLayerDownActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		SwapLayersHistoryItem hist = new (
			Resources.StandardIcons.LayerMoveDown,
			Translations.GetString ("Move Layer Down"),
			doc.Layers.CurrentUserLayerIndex,
			doc.Layers.CurrentUserLayerIndex - 1);

		doc.Layers.MoveCurrentLayerDown ();
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersMergeLayerDownActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		int bottomLayerIndex = doc.Layers.CurrentUserLayerIndex - 1;
		Cairo.ImageSurface oldBottomSurface = doc.Layers.UserLayers[bottomLayerIndex].Surface.Clone ();

		CompoundHistoryItem hist = new (
			Resources.Icons.LayerMergeDown,
			Translations.GetString ("Merge Layer Down"));

		DeleteLayerHistoryItem h1 = new (
			string.Empty,
			string.Empty,
			doc.Layers.CurrentUserLayer,
			doc.Layers.CurrentUserLayerIndex);

		doc.Layers.MergeCurrentLayerDown ();

		SimpleHistoryItem h2 = new (
			string.Empty,
			string.Empty,
			oldBottomSurface,
			bottomLayerIndex);
		hist.Push (h1);
		hist.Push (h2);

		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersDuplicateLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		UserLayer l = doc.Layers.DuplicateCurrentLayer ();

		// Make new layer the current layer
		doc.Layers.SetCurrentUserLayer (l);

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerDuplicate,
			Translations.GetString ("Duplicate Layer"),
			doc.Layers.IndexOf (l));
		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersDeleteLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		tools.Commit ();

		DeleteLayerHistoryItem hist = new (
			Resources.Icons.LayerDelete,
			Translations.GetString ("Delete Layer"),
			doc.Layers.CurrentUserLayer,
			doc.Layers.CurrentUserLayerIndex);

		doc.Layers.DeleteLayer (doc.Layers.CurrentUserLayerIndex);

		doc.History.PushNewItem (hist);
	}

	private void HandlePintaCoreActionsLayersAddNewLayerActivated (object sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;
		tools.Commit ();

		UserLayer l = doc.Layers.AddNewLayer (string.Empty);

		AddLayerHistoryItem hist = new (
			Resources.Icons.LayerNew,
			Translations.GetString ("Add New Layer"),
			doc.Layers.IndexOf (l));
		doc.History.PushNewItem (hist);
	}
}
