using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pinta.Core;

namespace Pinta.Docking;

/// <summary>
/// The canvas area with Paint.NET's utility windows floating over it. Panels are dragged by their
/// title bar, snap to the area edges and to each other, stay anchored to their nearest edges when the
/// area resizes, and fade while the pointer is elsewhere. Clicks between panels reach the canvas.
/// </summary>
[GObject.Subclass<Gtk.Overlay>]
public sealed partial class PanelArea
{
	/// <summary>Setting: whether panels turn translucent while the pointer is not over them.</summary>
	public const string TRANSLUCENT_SETTING = "panels-translucent";

	private const int MIN_PANEL_SIZE = 80;

	private sealed class Entry (FloatingPanel panel, PanelAnchor defaultAnchor, Size defaultSize)
	{
		public FloatingPanel Panel { get; } = panel;
		public PanelAnchor DefaultAnchor { get; } = defaultAnchor;
		public Size DefaultSize { get; } = defaultSize;
		public PanelAnchor Anchor { get; set; } = defaultAnchor;
		public Size Size { get; set; } = defaultSize; // Empty for panels sized by their content.
		public bool Resizable { get; } = !defaultSize.IsEmpty;
	}

	private readonly List<Entry> entries = [];
	private ISettingsService settings = null!; // NRT - set by New()
	private Size outer = Size.Empty; // The whole overlay.
	private Size area = Size.Empty; // Where panels go: the overlay less the inset.
	private int inset_left;
	private int inset_top;
	private uint relayout_idle;
	private bool interacting;

	partial void Initialize ()
	{
		// Reports the area's size; it draws nothing and lets every click through.
		Gtk.DrawingArea sizer = Gtk.DrawingArea.New ();
		sizer.CanTarget = false;
		sizer.OnResize += (_, e) => {
			outer = new Size (e.Width, e.Height);
			UpdateArea ();
		};
		AddOverlay (sizer);
	}

	public static PanelArea New (Gtk.Widget canvas, ISettingsService settings)
	{
		PanelArea panelArea = NewWithProperties ([]);
		panelArea.settings = settings;
		panelArea.Child = canvas;
		panelArea.Hexpand = true;
		panelArea.Vexpand = true;
		return panelArea;
	}

	/// <summary>
	/// Add a panel at its saved place, or at <paramref name="defaultAnchor"/>.
	/// A non-empty <paramref name="defaultSize"/> makes the panel resizable from its grip.
	/// </summary>
	public void AddPanel (FloatingPanel panel, PanelAnchor defaultAnchor, Size defaultSize)
	{
		Entry entry = new (panel, defaultAnchor, defaultSize);
		LoadGeometry (entry);
		entries.Add (entry);

		AddOverlay (panel);
		Apply (entry);
		panel.OnShow += (_, _) => QueueRelayout (); // Clamp to an area that may have shrunk while it was hidden.

		Gtk.GestureDrag move = Gtk.GestureDrag.New ();
		move.OnDragBegin += (_, args) => BeginMove (entry, args.StartX, args.StartY);
		move.OnDragUpdate += (gesture, _) => UpdateMove (entry, gesture);
		move.OnDragEnd += (_, _) => EndInteraction (entry);
		panel.TitleBar.AddController (move);

		// Resizable panels resize from their left, right and bottom edges and from the grip.
		// The gesture runs before the panel's content and lets go of presses away from the edges.
		if (entry.Resizable) {
			Gtk.GestureDrag resize = Gtk.GestureDrag.New ();
			resize.SetPropagationPhase (Gtk.PropagationPhase.Capture);
			resize.OnDragBegin += (gesture, args) => {
				ResizeEdges edges = EdgesAt (entry, args.StartX, args.StartY);
				if (edges == ResizeEdges.None) {
					gesture.SetState (Gtk.EventSequenceState.Denied);
					return;
				}
				gesture.SetState (Gtk.EventSequenceState.Claimed);
				BeginResize (entry, edges, args.StartX, args.StartY);
			};
			resize.OnDragUpdate += (gesture, _) => UpdateResize (entry, gesture);
			resize.OnDragEnd += (_, _) => {
				if (resize_edges != ResizeEdges.None)
					EndInteraction (entry);
				resize_edges = ResizeEdges.None;
			};
			panel.AddController (resize);
		}

		// Paint.NET's translucent windows: faded unless the pointer is over the panel.
		Gtk.EventControllerMotion hover = Gtk.EventControllerMotion.New ();
		hover.OnEnter += (_, _) => panel.Faded = false;
		hover.OnLeave += (_, _) => {
			if (!interacting)
				panel.Faded = RestsFaded;
		};
		if (entry.Resizable)
			hover.OnMotion += (_, args) => {
				if (!interacting)
					panel.Cursor = ResizeCursor (EdgesAt (entry, args.X, args.Y));
			};
		panel.AddController (hover);
		panel.Faded = RestsFaded;
	}

	/// <summary>
	/// Put a panel back at its default place and size (Paint.NET's Ctrl+Shift+F5–F8).
	/// </summary>
	public void ResetPanel (string id)
	{
		foreach (Entry entry in entries.Where (e => e.Panel.Id == id)) {
			entry.Anchor = entry.DefaultAnchor;
			entry.Size = entry.DefaultSize;
			Apply (entry);
		}
	}

	/// <summary>
	/// Keep the panels clear of a strip along the left and top edges (the rulers, as in Paint.NET).
	/// Panels keep their place relative to what is left, so they move along when the strip changes.
	/// </summary>
	public void SetInset (int left, int top)
	{
		if (inset_left == left && inset_top == top)
			return;
		inset_left = left;
		inset_top = top;
		UpdateArea ();
	}

	private void UpdateArea ()
	{
		area = new Size (Math.Max (0, outer.Width - inset_left), Math.Max (0, outer.Height - inset_top));
		QueueRelayout ();
	}

	private bool RestsFaded => settings.GetSetting (TRANSLUCENT_SETTING, true);

	// --- Placement

	private Size PanelSize (Entry entry)
	{
		// Measure rather than read the allocation: right after a resize the allocation is a frame behind
		// the new size request, and re-anchoring with the stale width shifted the panel sideways.
		// A hidden panel measures as 0 x 0; it is placed again when shown.
		FloatingPanel panel = entry.Panel;
		if (!panel.Visible)
			return entry.Resizable ? entry.Size : Size.Empty;

		panel.Measure (Gtk.Orientation.Horizontal, -1, out _, out int width, out _, out _);
		panel.Measure (Gtk.Orientation.Vertical, width, out _, out int height, out _, out _);
		// Measure includes the margins, which here are the panel's offsets in the area.
		return new Size (width - panel.MarginStart - panel.MarginEnd, height - panel.MarginTop - panel.MarginBottom);
	}

	private bool HasArea => area.Width > 0 && area.Height > 0;

	private RectangleD Bounds (Entry entry)
	{
		Size size = PanelSize (entry);
		PanelAnchor anchor = HasArea ? FloatingPanelLayout.Clamp (entry.Anchor, size, area) : entry.Anchor;
		return new RectangleD (FloatingPanelLayout.ToPosition (anchor, size, area), size.Width, size.Height);
	}

	/// <summary>
	/// Place the panel by its anchor, kept inside the area. The stored anchor is not changed,
	/// so a panel pushed in by a smaller window goes back when the window grows again.
	/// </summary>
	private void Apply (Entry entry)
	{
		FloatingPanel panel = entry.Panel;

		if (entry.Resizable) {
			Size size = entry.Size;
			if (HasArea)
				size = new Size (Math.Min (size.Width, area.Width), Math.Min (size.Height, area.Height));
			panel.SetSizeRequest (Math.Max (MIN_PANEL_SIZE, size.Width), Math.Max (MIN_PANEL_SIZE, size.Height));
		}

		PanelAnchor anchor = HasArea ? FloatingPanelLayout.Clamp (entry.Anchor, PanelSize (entry), area) : entry.Anchor;

		panel.Halign = anchor.Right ? Gtk.Align.End : Gtk.Align.Start;
		panel.Valign = anchor.Bottom ? Gtk.Align.End : Gtk.Align.Start;
		panel.MarginStart = anchor.Right ? 0 : anchor.OffsetX + inset_left;
		panel.MarginEnd = anchor.Right ? anchor.OffsetX : 0;
		panel.MarginTop = anchor.Bottom ? 0 : anchor.OffsetY + inset_top;
		panel.MarginBottom = anchor.Bottom ? anchor.OffsetY : 0;
	}

	private void QueueRelayout ()
	{
		// Changing margins while GTK is allocating would trigger another layout pass from inside this one.
		if (relayout_idle != 0)
			return;

		relayout_idle = GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
			relayout_idle = 0;
			foreach (Entry entry in entries)
				Apply (entry);
			return false;
		});
	}

	/// <summary>Where the pointer of an ongoing drag is, relative to the panels' area.</summary>
	private bool PointerInArea (Gtk.GestureDrag gesture, out PointD point)
	{
		point = PointD.Zero;
		if (gesture.Widget is not Gtk.Widget widget || !gesture.GetPoint (null, out double x, out double y))
			return false;

		if (!widget.TranslateCoordinates (this, x, y, out double areaX, out double areaY))
			return false;

		point = new PointD (areaX - inset_left, areaY - inset_top);
		return true;
	}

	// --- Moving

	private PointD grab_offset; // Pointer position inside the panel when a move began.
	private RectangleD resize_start_bounds;
	private PointD resize_start_pointer;

	private void BeginMove (Entry entry, double startX, double startY)
	{
		interacting = true;
		entry.Panel.Faded = false;
		entry.Panel.TitleBar.TranslateCoordinates (entry.Panel, startX, startY, out double x, out double y);
		grab_offset = new PointD (x, y);
	}

	private void UpdateMove (Entry entry, Gtk.GestureDrag gesture)
	{
		if (!HasArea || !PointerInArea (gesture, out PointD pointer))
			return;

		Size size = PanelSize (entry);
		RectangleD wanted = new (pointer.X - grab_offset.X, pointer.Y - grab_offset.Y, size.Width, size.Height);
		IEnumerable<RectangleD> others = entries
			.Where (e => e != entry && e.Panel.Visible)
			.Select (Bounds);

		PointD position = FloatingPanelLayout.Snap (wanted, area, others);
		entry.Anchor = new PanelAnchor (false, false, (int) position.X, (int) position.Y);
		Apply (entry);
	}

	private void EndInteraction (Entry entry)
	{
		interacting = false;

		// Re-anchor to the nearest edges so the panel keeps its distance to them when the window resizes.
		if (HasArea)
			entry.Anchor = FloatingPanelLayout.ToAnchor (Bounds (entry), area);
		Apply (entry);
	}

	// --- Resizing

	[Flags]
	private enum ResizeEdges { None = 0, Left = 1, Right = 2, Bottom = 4 }

	private const int EDGE_WIDTH = 5;
	private ResizeEdges resize_edges;

	/// <summary>Which edges a press at (x, y) in the panel's coordinates would resize.</summary>
	private static ResizeEdges EdgesAt (Entry entry, double x, double y)
	{
		FloatingPanel panel = entry.Panel;

		// The grip is the bottom-right corner.
		if (panel.Grip is Gtk.Widget grip && panel.TranslateCoordinates (grip, x, y, out double gx, out double gy) && grip.Contains (gx, gy))
			return ResizeEdges.Right | ResizeEdges.Bottom;

		ResizeEdges edges = ResizeEdges.None;
		if (x < EDGE_WIDTH)
			edges |= ResizeEdges.Left;
		else if (x >= panel.GetWidth () - EDGE_WIDTH)
			edges |= ResizeEdges.Right;
		if (y >= panel.GetHeight () - EDGE_WIDTH)
			edges |= ResizeEdges.Bottom;
		return edges;
	}

	private static Gdk.Cursor? ResizeCursor (ResizeEdges edges) => edges switch {
		ResizeEdges.Left => Gdk.Cursor.NewFromName (Resources.StandardCursors.ResizeW, null),
		ResizeEdges.Right => Gdk.Cursor.NewFromName (Resources.StandardCursors.ResizeE, null),
		ResizeEdges.Bottom => Gdk.Cursor.NewFromName (Resources.StandardCursors.ResizeS, null),
		ResizeEdges.Left | ResizeEdges.Bottom => Gdk.Cursor.NewFromName (Resources.StandardCursors.ResizeSW, null),
		ResizeEdges.Right | ResizeEdges.Bottom => Gdk.Cursor.NewFromName (Resources.StandardCursors.ResizeSE, null),
		_ => null,
	};

	private void BeginResize (Entry entry, ResizeEdges edges, double startX, double startY)
	{
		interacting = true;
		resize_edges = edges;
		entry.Panel.Faded = false;
		resize_start_bounds = Bounds (entry);
		entry.Panel.TranslateCoordinates (this, startX, startY, out double x, out double y);
		resize_start_pointer = new PointD (x - inset_left, y - inset_top);

		// Resize from a fixed top-left corner (moved along when dragging the left edge).
		entry.Anchor = new PanelAnchor (false, false, (int) resize_start_bounds.X, (int) resize_start_bounds.Y);
		entry.Size = new Size ((int) resize_start_bounds.Width, (int) resize_start_bounds.Height);
		Apply (entry);
	}

	private void UpdateResize (Entry entry, Gtk.GestureDrag gesture)
	{
		if (resize_edges == ResizeEdges.None || !HasArea || !PointerInArea (gesture, out PointD pointer))
			return;

		RectangleD start = resize_start_bounds;
		double dx = pointer.X - resize_start_pointer.X;
		double dy = pointer.Y - resize_start_pointer.Y;
		double left = start.X;
		double right = start.X + start.Width;
		double bottom = start.Y + start.Height;

		if (resize_edges.HasFlag (ResizeEdges.Left))
			left = Math.Clamp (start.X + dx, 0, right - MIN_PANEL_SIZE);
		if (resize_edges.HasFlag (ResizeEdges.Right))
			right = Math.Clamp (right + dx, left + MIN_PANEL_SIZE, Math.Max (left + MIN_PANEL_SIZE, area.Width));
		if (resize_edges.HasFlag (ResizeEdges.Bottom))
			bottom = Math.Clamp (bottom + dy, start.Y + MIN_PANEL_SIZE, Math.Max (start.Y + MIN_PANEL_SIZE, area.Height));

		entry.Anchor = entry.Anchor with { OffsetX = (int) left };
		entry.Size = new Size ((int) (right - left), (int) (bottom - start.Y));
		Apply (entry);
	}

	// --- Settings

	private static string GeometryKey (Entry entry) => $"panel-{entry.Panel.Id}-geometry";

	private void LoadGeometry (Entry entry)
	{
		// "right,bottom,offsetX,offsetY,width,height"; anything unreadable keeps the defaults.
		string[] parts = settings.GetSetting (GeometryKey (entry), string.Empty).Split (',');
		if (parts.Length != 6)
			return;

		int[] values = new int[6];
		for (int i = 0; i < 6; i++)
			if (!int.TryParse (parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
				return;

		entry.Anchor = new PanelAnchor (values[0] != 0, values[1] != 0, values[2], values[3]);
		if (entry.Resizable)
			entry.Size = new Size (Math.Max (MIN_PANEL_SIZE, values[4]), Math.Max (MIN_PANEL_SIZE, values[5]));
	}

	public void SaveSettings ()
	{
		foreach (Entry entry in entries) {
			PanelAnchor a = entry.Anchor;
			string value = string.Join (',', new[] { a.Right ? 1 : 0, a.Bottom ? 1 : 0, a.OffsetX, a.OffsetY, entry.Size.Width, entry.Size.Height }
				.Select (v => v.ToString (CultureInfo.InvariantCulture)));
			settings.PutSetting (GeometryKey (entry), value);
		}
	}
}
