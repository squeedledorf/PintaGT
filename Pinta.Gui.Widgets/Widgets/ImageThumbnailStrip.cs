using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// Paint.NET's image list: a strip with a thumbnail for every open image.
/// Click to switch images, middle-click or the red X to close, right-click for the image menu,
/// drag to reorder. Unsaved images show their asterisk in the window title, as in Paint.NET.
/// </summary>
[GObject.Subclass<Gtk.Box>]
public sealed partial class ImageThumbnailStrip
{
	private const int PADDING = 5; // Between the highlight and the image.
	private const int CLOSE_RADIUS = 6;
	private const uint REFRESH_DELAY_MS = 300; // Thumbnails are re-rendered at most this often while editing.
	private const string DRAG_MARKER = "pinta-image-list";

	// Paint.NET's selection: a light-blue tile with a blue border.
	private static readonly Color selected_fill = new (0.8, 0.89, 0.97);
	private static readonly Color selected_border = new (0.0, 0.47, 0.84);
	private static readonly Color hover_fill = new (0.0, 0.47, 0.84, 0.1);
	private static readonly Color hover_border = new (0.0, 0.47, 0.84, 0.45);
	private static readonly Color shadow_color = new (0, 0, 0, 0.06);
	private static readonly Color close_color = new (0.79, 0.31, 0.31);
	private static readonly Pattern transparent_pattern = CairoExtensions.CreateTransparentBackgroundPattern (4);

	private readonly Dictionary<Document, Thumbnail> thumbnails = [];
	private Thumbnail? dragged;
	private int thumbnail_height = 40;

	private Gtk.ScrolledWindow scroller;
	private Gtk.Box items_box;
	private Gtk.Button scroll_left;
	private Gtk.Button scroll_right;
	private Gio.Menu list_menu;
	private Gio.Menu context_menu;
	private Gio.SimpleAction copy_path_action;
	private Gio.SimpleAction open_folder_action;
	private Gtk.PopoverMenu? context_popover;

	public static ImageThumbnailStrip New () => NewWithProperties ([]);

	[MemberNotNull (nameof (scroller), nameof (items_box), nameof (scroll_left), nameof (scroll_right))]
	[MemberNotNull (nameof (list_menu), nameof (context_menu), nameof (copy_path_action), nameof (open_folder_action))]
	partial void Initialize ()
	{
		Gtk.Box itemsBox = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		itemsBox.Valign = Gtk.Align.Center;

		Gtk.DropTarget dropTarget = Gtk.DropTarget.New (GObject.Type.String, Gdk.DragAction.Move);
		dropTarget.OnDrop += HandleDrop;
		itemsBox.AddController (dropTarget);

		Gtk.ScrolledWindow scrolledWindow = Gtk.ScrolledWindow.New ();
		scrolledWindow.SetPolicy (Gtk.PolicyType.External, Gtk.PolicyType.Never);
		scrolledWindow.PropagateNaturalWidth = true;
		scrolledWindow.PropagateNaturalHeight = true;
		scrolledWindow.SetChild (itemsBox);

		// The wheel scrolls the list sideways, as in Paint.NET.
		Gtk.EventControllerScroll scrollController = Gtk.EventControllerScroll.New (Gtk.EventControllerScrollFlags.BothAxes);
		scrollController.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		scrollController.OnScroll += (controller, args) => {
			double delta = args.Dx != 0 ? args.Dx : args.Dy;
			// A wheel reports notches; a touchpad already reports pixels.
			if (controller.GetUnit () == Gdk.ScrollUnit.Wheel)
				delta *= thumbnail_height + 2 * PADDING;
			ScrollBy (delta);
			return true;
		};
		scrolledWindow.AddController (scrollController);

		Gtk.Button scrollLeft = CreateArrowButton ("go-previous-symbolic", Translations.GetString ("Scroll Left"));
		scrollLeft.OnClicked += (_, _) => ScrollBy (-PageStep ());

		Gtk.Button scrollRight = CreateArrowButton ("go-next-symbolic", Translations.GetString ("Scroll Right"));
		scrollRight.OnClicked += (_, _) => ScrollBy (PageStep ());

		// The "all images" dropdown lists the open images by name.
		Gio.Menu listMenu = Gio.Menu.New ();
		Gtk.MenuButton listButton = Gtk.MenuButton.New ();
		listButton.IconName = "pinta-image-list-more";
		listButton.TooltipText = Translations.GetString ("All Images");
		listButton.Valign = Gtk.Align.Center;
		listButton.AddCssClass (AdwaitaStyles.Flat);
		listButton.AddCssClass ("pdn-flat-button");
		listButton.MenuModel = listMenu;

		// The image menu. The image is made active before the menu shows, so the
		// app-level Save / Save As / Close actions act on the right-clicked image.
		Gio.SimpleAction copyPathAction = Gio.SimpleAction.New ("copy-path", null);
		copyPathAction.OnActivate += (_, _) => CopyActivePath ();

		Gio.SimpleAction openFolderAction = Gio.SimpleAction.New ("open-folder", null);
		openFolderAction.OnActivate += (_, _) => OpenActiveContainingFolder ();

		Gio.SimpleActionGroup actions = Gio.SimpleActionGroup.New ();
		actions.AddAction (copyPathAction);
		actions.AddAction (openFolderAction);
		InsertActionGroup ("imagelist", actions);

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Horizontal);
		Spacing = 0;
		Append (scrollLeft);
		Append (scrolledWindow);
		Append (scrollRight);
		Append (listButton);

		// --- References to keep

		scroller = scrolledWindow;
		items_box = itemsBox;
		scroll_left = scrollLeft;
		scroll_right = scrollRight;
		list_menu = listMenu;
		context_menu = Gio.Menu.New ();
		copy_path_action = copyPathAction;
		open_folder_action = openFolderAction;

		// --- Further initialization

		Gtk.Adjustment hadjustment = scrolledWindow.GetHadjustment ()!;
		hadjustment.OnChanged += (_, _) => UpdateArrows ();
		hadjustment.OnValueChanged += (_, _) => UpdateArrows ();
		UpdateArrows ();

		WorkspaceManager workspace = PintaCore.Workspace;
		workspace.DocumentActivated += (_, e) => AddThumbnail (e.Document);
		workspace.DocumentClosed += (_, e) => RemoveThumbnail (e.Document);
		workspace.DocumentsReordered += (_, _) => SyncOrder ();
		workspace.ActiveDocumentChanged += (_, _) => OnActiveDocumentChanged ();

		foreach (Document document in workspace.OpenDocuments)
			AddThumbnail (document);

		OnActiveDocumentChanged ();
	}

	/// <summary>
	/// Size of the square box each image is fitted into, in pixels (the cell adds the padding).
	/// </summary>
	public int ThumbnailHeight {
		get => thumbnail_height;
		set {
			thumbnail_height = Math.Max (8, value);
			foreach (Thumbnail thumbnail in thumbnails.Values)
				thumbnail.Refresh ();
		}
	}

	/// <summary>
	/// Show the image menu for the active image, below its thumbnail (Paint.NET's Alt+minus).
	/// </summary>
	public void PopupMenuForActiveImage ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault is Document document && thumbnails.TryGetValue (document, out Thumbnail? thumbnail))
			PopupContextMenu (thumbnail);
	}

	private static Gtk.Button CreateArrowButton (string iconName, string tooltip)
	{
		Gtk.Button button = Gtk.Button.NewFromIconName (iconName);
		button.TooltipText = tooltip;
		button.Valign = Gtk.Align.Center;
		button.AddCssClass (AdwaitaStyles.Flat);
		return button;
	}

	private void AddThumbnail (Document document)
	{
		if (thumbnails.ContainsKey (document))
			return;

		Thumbnail thumbnail = new (this, document);
		thumbnails.Add (document, thumbnail);
		items_box.Append (thumbnail.Area);
		SyncOrder ();
	}

	private void RemoveThumbnail (Document document)
	{
		if (!thumbnails.Remove (document, out Thumbnail? thumbnail))
			return;

		if (dragged == thumbnail)
			dragged = null;

		thumbnail.Detach ();
		items_box.Remove (thumbnail.Area);
		RebuildListMenu ();
	}

	/// <summary>
	/// Put the thumbnails in the workspace's document order.
	/// </summary>
	private void SyncOrder ()
	{
		Gtk.Widget? previous = null;
		foreach (Document document in PintaCore.Workspace.OpenDocuments) {
			if (!thumbnails.TryGetValue (document, out Thumbnail? thumbnail))
				continue;

			items_box.ReorderChildAfter (thumbnail.Area, previous);
			previous = thumbnail.Area;
		}

		RebuildListMenu ();
	}

	private void OnActiveDocumentChanged ()
	{
		foreach (Thumbnail thumbnail in thumbnails.Values)
			thumbnail.Area.QueueDraw ();

		// Wait for the layout, so that a newly added thumbnail has a position.
		GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
			ScrollActiveIntoView ();
			return false;
		});
	}

	private void RebuildListMenu ()
	{
		list_menu.RemoveAll ();

		IReadOnlyList<Document> documents = PintaCore.Workspace.OpenDocuments;
		for (int i = 0; i < documents.Count; i++) {
			Document document = documents[i];
			string label = document.IsDirty ? $"{document.DisplayName}*" : document.DisplayName;
			list_menu.Append (label, $"app.active_document({i})");
		}
	}

	private void ActivateDocument (Document document)
	{
		int index = PintaCore.Workspace.OpenDocuments.IndexOf (document);
		if (index >= 0)
			PintaCore.Workspace.SetActiveDocument (index);
	}

	private void CloseDocument (Document document)
	{
		if (!PintaCore.Workspace.OpenDocuments.Contains (document))
			return;

		// Close acts on the active image (and asks to save unsaved changes).
		// Closing another image keeps the current one active, as in Paint.NET.
		Document? previous = PintaCore.Workspace.ActiveDocumentOrDefault;
		ActivateDocument (document);
		PintaCore.Actions.File.Close.Activate ();

		// Only once the close has finished: saving first (async) still needs this image active.
		bool closed = !PintaCore.Workspace.OpenDocuments.Contains (document);
		if (closed && previous is not null && previous != document)
			ActivateDocument (previous);
	}

	// --- Scrolling

	private double PageStep ()
		=> Math.Max (scroller.GetHadjustment ()!.PageSize * 0.75, thumbnail_height);

	private void ScrollBy (double delta)
	{
		Gtk.Adjustment adjustment = scroller.GetHadjustment ()!;
		adjustment.Value = Math.Clamp (adjustment.Value + delta, adjustment.Lower, Math.Max (adjustment.Lower, adjustment.Upper - adjustment.PageSize));
	}

	private void UpdateArrows ()
	{
		Gtk.Adjustment adjustment = scroller.GetHadjustment ()!;
		double maxValue = adjustment.Upper - adjustment.PageSize;

		// Showing the arrows narrows the list, which keeps it overflowing, so this does not oscillate.
		bool overflowing = maxValue > 0.5;
		scroll_left.Visible = overflowing;
		scroll_right.Visible = overflowing;
		scroll_left.Sensitive = adjustment.Value > adjustment.Lower + 0.5;
		scroll_right.Sensitive = adjustment.Value < maxValue - 0.5;
	}

	private void ScrollActiveIntoView ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault is not Document document || !thumbnails.TryGetValue (document, out Thumbnail? thumbnail))
			return;

		if (!thumbnail.Area.TranslateCoordinates (items_box, 0, 0, out double x, out double _))
			return;

		Gtk.Adjustment adjustment = scroller.GetHadjustment ()!;
		double width = thumbnail.Area.GetWidth ();

		if (x < adjustment.Value)
			adjustment.Value = x;
		else if (x + width > adjustment.Value + adjustment.PageSize)
			adjustment.Value = x + width - adjustment.PageSize;
	}

	// --- Drag & drop reordering

	private bool HandleDrop (Gtk.DropTarget _, Gtk.DropTarget.DropSignalArgs args)
	{
		// Only thumbnails dragged within this strip are accepted.
		if (dragged is null || args.Value.GetString () != DRAG_MARKER)
			return false;

		WorkspaceManager workspace = PintaCore.Workspace;
		List<double> centers = [];
		foreach (Document document in workspace.OpenDocuments) {
			Gtk.DrawingArea area = thumbnails[document].Area;
			area.TranslateCoordinates (items_box, area.GetWidth () / 2.0, 0, out double centerX, out double _);
			centers.Add (centerX);
		}

		int from = workspace.OpenDocuments.IndexOf (dragged.Document);
		if (from < 0)
			return false;

		workspace.MoveDocument (from, ImageListLayout.DropIndex (centers, from, args.X));
		return true;
	}

	// --- Image menu

	private void PopupContextMenu (Thumbnail thumbnail)
	{
		Document document = thumbnail.Document;
		bool hasFile = document.HasFile;
		copy_path_action.SetEnabled (hasFile);
		open_folder_action.SetEnabled (hasFile);

		Gio.Menu fileSection = Gio.Menu.New ();
		fileSection.Append (Translations.GetString ("Copy Path"), "imagelist.copy-path");
		fileSection.Append (Translations.GetString ("Open Containing Folder"), "imagelist.open-folder");

		FileActions file = PintaCore.Actions.File;
		Gio.Menu saveSection = Gio.Menu.New ();
		saveSection.AppendItem (file.Save.CreateMenuItem ());
		saveSection.AppendItem (file.SaveAs.CreateMenuItem ());

		Gio.Menu closeSection = Gio.Menu.New ();
		closeSection.AppendItem (file.Close.CreateMenuItem ());
		closeSection.AppendItem (PintaCore.Actions.Window.CloseAll.CreateMenuItem ());

		context_menu.RemoveAll ();
		context_menu.AppendSection (document.DisplayName, fileSection);
		context_menu.AppendSection (null, saveSection);
		context_menu.AppendSection (null, closeSection);

		if (context_popover is null) {
			context_popover = Gtk.PopoverMenu.NewFromModel (context_menu);
			context_popover.SetParent (this);
			context_popover.Position = Gtk.PositionType.Bottom;
		}

		thumbnail.Area.TranslateCoordinates (this, 0, 0, out double x, out double y);
		context_popover.SetPointingTo (new Gdk.Rectangle {
			X = (int) x,
			Y = (int) y,
			Width = thumbnail.Area.GetWidth (),
			Height = thumbnail.Area.GetHeight (),
		});
		context_popover.Popup ();
	}

	private static void CopyActivePath ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault?.File is not Gio.File file)
			return;

		GdkExtensions.GetDefaultClipboard ().SetText (file.GetPath () ?? file.GetUri ());
	}

	private static async void OpenActiveContainingFolder ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault?.File is not Gio.File file)
			return;

		try {
			await Gtk.FileLauncher.New (file).OpenContainingFolderAsync (PintaCore.Chrome.MainWindow);
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to open containing folder: {e.Message}");
		}
	}

	/// <summary>
	/// One thumbnail: a drawing area showing a downscaled render of the document, re-rendered
	/// (throttled) when its canvas is invalidated.
	/// </summary>
	private sealed class Thumbnail
	{
		private readonly ImageThumbnailStrip strip;
		private ImageSurface? surface;
		private bool stale = true;
		private bool hovered;
		private uint refresh_timer;
		private PointD drag_start;

		public Document Document { get; }
		public Gtk.DrawingArea Area { get; }

		public Thumbnail (ImageThumbnailStrip strip, Document document)
		{
			this.strip = strip;
			Document = document;

			Area = Gtk.DrawingArea.New ();
			Area.Valign = Gtk.Align.Center;
			Area.SetDrawFunc ((_, g, width, height) => Draw (g, width, height));

			Gtk.GestureClick click = Gtk.GestureClick.New ();
			click.SetButton (0); // All buttons.
			click.OnPressed += HandlePressed;
			click.OnReleased += HandleReleased;
			Area.AddController (click);

			Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
			motion.OnEnter += (_, _) => SetHovered (true);
			motion.OnLeave += (_, _) => SetHovered (false);
			Area.AddController (motion);

			Gtk.DragSource dragSource = Gtk.DragSource.New ();
			dragSource.SetActions (Gdk.DragAction.Move);
			dragSource.OnPrepare += (_, args) => {
				strip.dragged = this;
				drag_start = new PointD (args.X, args.Y);
				return Gdk.ContentProvider.NewForValue (new GObject.Value (DRAG_MARKER));
			};
			dragSource.OnDragBegin += (source, _) => source.SetIcon (Gtk.WidgetPaintable.New (Area), (int) drag_start.X, (int) drag_start.Y);
			dragSource.OnDragEnd += (_, _) => strip.dragged = null;
			Area.AddController (dragSource);

			document.Workspace.CanvasInvalidated += HandleCanvasInvalidated;
			document.IsDirtyChanged += HandleDirtyChanged;
			document.Renamed += HandleRenamed;

			UpdateTooltip ();
			UpdateSize ();
		}

		public void Detach ()
		{
			Document.Workspace.CanvasInvalidated -= HandleCanvasInvalidated;
			Document.IsDirtyChanged -= HandleDirtyChanged;
			Document.Renamed -= HandleRenamed;

			if (refresh_timer != 0) {
				GLib.Source.Remove (refresh_timer);
				refresh_timer = 0;
			}

			surface?.Dispose ();
			surface = null;
		}

		/// <summary>
		/// Re-render on the next draw, e.g. after the image changed size.
		/// </summary>
		public void Refresh ()
		{
			stale = true;
			UpdateSize ();
			Area.QueueDraw ();
		}

		private bool IsActive => PintaCore.Workspace.ActiveDocumentOrDefault == Document;

		private void UpdateSize ()
		{
			// Square cells, as in Paint.NET; the image keeps its aspect inside.
			int size = strip.thumbnail_height + 2 * PADDING;
			Area.SetContentWidth (size);
			Area.SetContentHeight (size);
		}

		private void UpdateTooltip ()
			=> Area.TooltipText = Document.File?.GetParseName () ?? Document.DisplayName;

		private void HandleCanvasInvalidated (object? sender, CanvasInvalidatedEventArgs e)
		{
			// Coalesce the many invalidations of a brush stroke into one render per delay.
			if (refresh_timer != 0)
				return;

			refresh_timer = GLib.Functions.TimeoutAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, REFRESH_DELAY_MS, () => {
				refresh_timer = 0;
				Refresh ();
				return false;
			});
		}

		private void HandleDirtyChanged (object? sender, EventArgs e)
			=> strip.RebuildListMenu ();

		private void HandleRenamed (object? sender, EventArgs e)
		{
			UpdateTooltip ();
			strip.RebuildListMenu ();
		}

		private void SetHovered (bool value)
		{
			hovered = value;
			Area.QueueDraw ();
		}

		private bool ShowsCloseButton => hovered;

		private bool IsOnCloseButton (double x, double y)
		{
			PointD center = CloseButtonCenter (Area.GetWidth ());
			return ShowsCloseButton && Math.Abs (x - center.X) <= CLOSE_RADIUS + 1 && Math.Abs (y - center.Y) <= CLOSE_RADIUS + 1;
		}

		private static PointD CloseButtonCenter (int width) => new (width - CLOSE_RADIUS - 1, CLOSE_RADIUS + 1);

		private void HandlePressed (Gtk.GestureClick gesture, Gtk.GestureClick.PressedSignalArgs args)
		{
			switch (gesture.GetCurrentButton ()) {
				case Gdk.Constants.BUTTON_PRIMARY:
					if (!IsOnCloseButton (args.X, args.Y))
						strip.ActivateDocument (Document);
					break;
				case Gdk.Constants.BUTTON_MIDDLE:
					strip.CloseDocument (Document);
					break;
				case Gdk.Constants.BUTTON_SECONDARY:
					strip.ActivateDocument (Document);
					strip.PopupContextMenu (this);
					break;
			}
		}

		private void HandleReleased (Gtk.GestureClick gesture, Gtk.GestureClick.ReleasedSignalArgs args)
		{
			if (gesture.GetCurrentButton () == Gdk.Constants.BUTTON_PRIMARY && IsOnCloseButton (args.X, args.Y))
				strip.CloseDocument (Document);
		}

		private void Draw (Context g, int width, int height)
		{
			// Highlight: blue for the active image, faint blue on hover.
			if (IsActive || hovered) {
				g.Rectangle (0.5, 0.5, width - 1, height - 1);
				g.SetSourceColor (IsActive ? selected_fill : hover_fill);
				g.FillPreserve ();
				g.LineWidth = 1;
				g.SetSourceColor (IsActive ? selected_border : hover_border);
				g.Stroke ();
			}

			// The image, letterboxed into the thumbnail area.
			Size image = Document.ImageSize;
			int boxWidth = width - 2 * PADDING;
			int boxHeight = height - 2 * PADDING;
			if (image.Width > 0 && image.Height > 0 && boxWidth > 0 && boxHeight > 0) {
				double fit = Math.Min (boxWidth / (double) image.Width, boxHeight / (double) image.Height);
				int drawWidth = Math.Max (1, (int) Math.Round (image.Width * fit));
				int drawHeight = Math.Max (1, (int) Math.Round (image.Height * fit));
				double x = PADDING + (boxWidth - drawWidth) / 2;
				double y = PADDING + (boxHeight - drawHeight) / 2;

				// A soft drop shadow on all sides (stronger below right) instead of a frame.
				for (int i = 3; i >= 1; i--) {
					g.Rectangle (x - i + 1, y - i + 1, drawWidth + 2 * i, drawHeight + 2 * i);
					g.SetSourceColor (shadow_color);
					g.Fill ();
				}

				int scaleFactor = Area.GetScaleFactor ();
				ImageSurface thumb = GetSurface (drawWidth * scaleFactor, drawHeight * scaleFactor);

				g.Save ();
				g.Translate (x, y);
				g.Scale (1.0 / scaleFactor, 1.0 / scaleFactor);
				g.SetSourceSurface (thumb, 0, 0);
				g.Paint ();
				g.Restore ();
			}

			if (ShowsCloseButton)
				DrawCloseButton (g, CloseButtonCenter (width));
		}

		private ImageSurface GetSurface (int width, int height)
		{
			if (surface is not null && !stale && surface.Width == width && surface.Height == height)
				return surface;

			surface?.Dispose ();
			surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);

			using Context g = new (surface);
			g.SetSource (transparent_pattern);
			g.Paint ();
			g.Scale (width / (double) Document.ImageSize.Width, height / (double) Document.ImageSize.Height);

			// ponytail: the Good filter reads every source pixel (box downscale, so thin strokes stay visible);
			// at the refresh delay that is fine for photo-sized images. Cache a mip level if huge images lag.
			foreach (Layer layer in Document.Layers.GetLayersToPaint ()) {
				g.Save ();
				g.Transform (layer.Transform);
				using SurfacePattern pattern = new (layer.Surface) { Filter = Filter.Good };
				g.SetSource (pattern);
				g.PaintWithBlendMode (layer.BlendMode, layer.Opacity);
				g.Restore ();
			}

			stale = false;
			return surface;
		}

		private static void DrawCloseButton (Context g, PointD center)
		{
			g.Arc (center.X, center.Y, CLOSE_RADIUS, 0, 2 * Math.PI);
			g.SetSourceColor (close_color);
			g.Fill ();

			const double ARM = 2.5;
			g.MoveTo (center.X - ARM, center.Y - ARM);
			g.LineTo (center.X + ARM, center.Y + ARM);
			g.MoveTo (center.X + ARM, center.Y - ARM);
			g.LineTo (center.X - ARM, center.Y + ARM);
			g.SetSourceColor (new Color (1, 1, 1));
			g.LineWidth = 1.5;
			g.LineCap = LineCap.Round;
			g.Stroke ();
		}
	}
}
