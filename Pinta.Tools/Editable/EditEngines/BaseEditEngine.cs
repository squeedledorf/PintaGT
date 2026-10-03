//
// BaseEditEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
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
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Paint.NET's editing model for the Shapes and Line/Curve tools: one shape at a time stays editable
/// until it is committed (Enter, Esc, a click outside it, a new shape or another tool), and becomes a
/// single history item named after the tool. The shape is drawn on a re-editable layer above the
/// current layer while it is edited, and its style follows the tool bar.
/// </summary>
public abstract class BaseEditEngine
{
	protected enum DragMode { None, Create, Move, Rotate, Nub, Pivot }

	// Width in window pixels of the corridor just outside a shape's box where a left drag rotates.
	protected const double ROTATE_CORRIDOR = 16;

	private static readonly (string Name, string Pattern)[] dash_styles = [
		(Translations.GetString ("Solid"), "-"),
		(Translations.GetString ("Dashes"), "--- "),
		(Translations.GetString ("Dotted"), "- "),
		(Translations.GetString ("Dash, Dot"), "--- - "),
		(Translations.GetString ("Dash, Dot, Dot"), "--- - - "),
	];

	protected readonly ShapeTool owner;
	protected readonly IWorkspaceService workspace;
	private readonly IPaletteService palette;
	private readonly IToolService tools;

	// The shape being edited, and where it lives.
	private EditableShape? shape;
	private ShapeHistoryItem? item;
	private Document? document;
	private UserLayer? layer;
	private ReEditableLayer? drawing_layer;
	private RectangleD? last_dirty;

	// The shape that the last tool commit (e.g. before saving) drew, so that saving can give it back for editing.
	private (ShapeHistoryItem Item, EditableShape Shape)? save_commit;

	private DragMode drag = DragMode.None;
	private int drag_nub;
	private PointD drag_origin;
	private EditableShape? drag_start;

	private readonly MoveHandle[] nub_handles;
	private readonly MoveHandle pivot_handle;
	private readonly MoveIconHandle move_handle;

	private static Gdk.Cursor? nub_cursor;
	private static Gdk.Cursor? move_cursor;

	protected BaseEditEngine (IServiceProvider services, ShapeTool owner, int nubCount)
	{
		this.owner = owner;
		workspace = services.GetService<IWorkspaceService> ();
		palette = services.GetService<IPaletteService> ();
		tools = services.GetService<IToolService> ();

		nub_handles = Enumerable.Range (0, nubCount).Select (_ => new MoveHandle (workspace)).ToArray ();
		pivot_handle = new MoveHandle (workspace) { Radius = 6, Crosshair = true };
		move_handle = new MoveIconHandle (workspace);
	}

	public IEnumerable<IToolHandle> Handles => [.. nub_handles, pivot_handle, move_handle];

	#region Subclass hooks

	/// <summary>A new shape, as the mouse goes down at <paramref name="start"/>.</summary>
	protected abstract EditableShape CreateShape (PointD start, bool useSecondaryColor);

	/// <summary>Reshapes a shape being drawn as the mouse is dragged.</summary>
	protected abstract void UpdateCreate (EditableShape shape, PointD origin, PointD current, bool shift, bool alt);

	/// <summary>Whether the shape is too small to keep after drawing it, e.g. a click without a drag.</summary>
	protected abstract bool IsDegenerate (EditableShape shape);

	/// <summary>Moves nub <paramref name="nub"/> of <paramref name="shape"/> (a copy of the shape as the drag started).</summary>
	protected abstract void DragNub (EditableShape shape, int nub, PointD current, bool shift, bool alt);

	/// <summary>The transform that rotates <paramref name="start"/> as the mouse goes from <paramref name="from"/> to <paramref name="to"/>.</summary>
	protected abstract Matrix ComputeRotation (EditableShape start, PointD from, PointD to, bool snap);

	/// <summary>Draws the shape and returns the area it covered.</summary>
	protected abstract RectangleD DrawShape (Context g, EditableShape shape, Color outline, Color fill);

	/// <summary>The canvas point that the move icon sits next to.</summary>
	protected abstract PointD MoveIconAnchor (EditableShape shape);

	/// <summary>Whether a left drag just outside the box rotates (shapes) rather than committing (lines).</summary>
	protected virtual bool HasRotateCorridor => false;

	/// <summary>Whether the shape has a draggable centre of rotation.</summary>
	protected virtual bool HasPivotHandle => false;

	/// <summary>Window pixels around the outline that still count as on the shape.</summary>
	protected virtual double OutlineMargin => 0;

	protected virtual bool HandleToolKey (ToolKeyEventArgs e) => false;

	public abstract void BuildToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix);

	public virtual void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		if (outline_width is not null)
			settings.PutSetting (SettingNames.BrushWidth (toolPrefix), outline_width.Value);

		if (fill_button is not null)
			settings.PutSetting (SettingNames.FillStyle (toolPrefix), fill_button.SelectedIndex);

		if (dash_picker is not null)
			settings.PutSetting (SettingNames.DashPattern (toolPrefix), DashPattern);
	}

	#endregion

	#region Tool bar

	private ToolBarDropDownButton? fill_button;
	private Gtk.Separator? fill_sep;
	private Gtk.SpinButton? outline_width;
	private Gtk.Label? outline_width_label;
	private Gtk.Separator? outline_width_sep;
	private Gtk.Label? dash_label;
	private GlyphPicker? dash_picker;

	protected double BrushWidth {
		get => outline_width?.Value ?? BaseTool.DEFAULT_BRUSH_WIDTH;
		set {
			if (outline_width is not null)
				outline_width.Value = value;
		}
	}

	protected string DashPattern => dash_styles[dash_picker?.SelectedIndex ?? 0].Pattern;

	protected GlyphPicker? DashPicker => dash_picker;

	protected bool StrokeShape => fill_button?.SelectedItem.Tag is not int mode || mode != 1;

	protected bool FillShape => fill_button?.SelectedItem.Tag is int mode && mode >= 1;

	/// <summary>Paint.NET's draw mode dropdown: outline, fill, or fill with outline.</summary>
	protected void AppendFillMode (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (fill_button is null) {
			fill_button = ToolBarDropDownButton.New ();
			fill_button.AddItem (Translations.GetString ("Draw Shape Outline"), Resources.Icons.FillStyleOutline, 0);
			fill_button.AddItem (Translations.GetString ("Draw Filled Shape"), Resources.Icons.FillStyleFill, 1);
			fill_button.AddItem (Translations.GetString ("Draw Filled Shape With Outline"), Resources.Icons.FillStyleOutlineFill, 2);
			fill_button.SelectedIndex = settings.GetSetting (SettingNames.FillStyle (toolPrefix), 0);
			fill_button.SelectedItemChanged += (_, _) => {
				UpdateStrokeWidgetsVisibility ();
				Redraw ();
			};
		}

		fill_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (fill_button);
		tb.Append (fill_sep);
	}

	protected void AppendBrushWidth (Gtk.Box tb, ISettingsService settings, string toolPrefix, string label)
	{
		outline_width_label ??= Gtk.Label.New ($" {label}: ");

		if (outline_width is null) {
			outline_width = GtkExtensions.CreateToolBarSpinButton (1, 1e5, 1, SettingNames.GetBrushWidth (settings, SettingNames.BrushWidth (toolPrefix)));
			outline_width.Digits = 2;
			outline_width.TooltipText = Translations.GetString ("Change brush size.") + "\n"
				+ "\n" + Translations.GetString ("Shortcut keys:")
				+ "\n" + Translations.GetString ("Press {0} to decrease brush size", "\"[\"")
				+ "\n" + Translations.GetString ("Press {0} to increase brush size", "\"]\"")
				// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS. {1} is a number.
				+ "\n" + Translations.GetString ("Hold {0} to change it by {1}", PintaCore.System.CtrlLabel (), BaseBrushTool.BrushWidthLargeStep);
			outline_width.OnValueChanged += (_, _) => Redraw ();
		}

		outline_width_sep ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (outline_width_label);
		tb.Append (outline_width);
		tb.Append (outline_width_sep);
	}

	/// <summary>The "Style:" label. The dash picker itself is appended with <see cref="AppendDashPicker"/>, so caps can go around it.</summary>
	protected void AppendStyleLabel (Gtk.Box tb)
	{
		dash_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Style")));
		tb.Append (dash_label);
	}

	protected void AppendDashPicker (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (dash_picker is null) {
			dash_picker = new GlyphPicker (
				dash_styles.Select (s => new GlyphPicker.Item (s.Name, CreateDashGlyph (s.Pattern))).ToArray (),
				columns: 1, showNameOnButton: false, showNamesInList: true);

			string saved = settings.GetSetting (SettingNames.DashPattern (toolPrefix), "-");
			dash_picker.SelectedIndex = Math.Max (0, Array.FindIndex (dash_styles, s => s.Pattern == saved));
			dash_picker.Changed += (_, _) => Redraw ();
		}

		tb.Append (dash_picker.Button);
	}

	protected void AppendSeparator (Gtk.Box tb, ref Gtk.Separator? separator)
	{
		separator ??= GtkExtensions.CreateToolBarSeparator ();
		tb.Append (separator);
	}

	protected void UpdateStrokeWidgetsVisibility ()
	{
		bool stroke = StrokeShape;
		foreach (Gtk.Widget? w in new Gtk.Widget?[] { outline_width, outline_width_label, outline_width_sep, dash_label, dash_picker?.Button })
			if (w is not null)
				w.Visible = stroke;
	}

	private static Gdk.Texture CreateDashGlyph (string pattern)
		=> GlyphPicker.CreateGlyph (40, 12, g => {
			g.SetSourceColor (GlyphPicker.GlyphStroke);
			g.LineWidth = 3;
			g.SetDashFromString (pattern, 3, LineCap.Butt);
			g.MoveTo (2, 6);
			g.LineTo (38, 6);
			g.Stroke ();
		});

	#endregion

	#region Tool events

	public virtual void HandleActivated ()
	{
		palette.PrimaryColorChanged += OnPaletteChanged;
		palette.SecondaryColorChanged += OnPaletteChanged;
		UpdateStrokeWidgetsVisibility ();
	}

	public virtual void HandleDeactivated ()
	{
		Commit ();
		palette.PrimaryColorChanged -= OnPaletteChanged;
		palette.SecondaryColorChanged -= OnPaletteChanged;
	}

	private void OnPaletteChanged (object? sender, EventArgs e) => Redraw ();

	/// <summary>Commits the shape because something else needs the layer (another action, a save, ...).</summary>
	public void HandleCommit ()
	{
		save_commit = null;

		if (shape is null)
			return;

		EditableShape committed = shape;
		ShapeHistoryItem? committedItem = Commit ();
		if (committedItem is not null)
			save_commit = (committedItem, committed);
	}

	/// <summary>
	/// Saving commits the shape so that it is in the file. Afterwards the shape goes back to being
	/// edited, with the same single history item, as if nothing had happened.
	/// </summary>
	public void HandleAfterSave ()
	{
		if (save_commit is not { } saved)
			return;

		save_commit = null;
		(ShapeHistoryItem committedItem, EditableShape committedShape) = saved;

		if (!workspace.HasOpenDocuments || workspace.ActiveDocument.History.Current != committedItem || !committedItem.IsCommitted)
			return;

		committedItem.Uncommit ();
		RestorePendingShape (committedItem, committedShape);
	}

	public bool HandleBeforeUndo () => drag != DragMode.None;

	public bool HandleBeforeRedo () => drag != DragMode.None;

	public bool HandleKeyDown (Document document, ToolKeyEventArgs e)
	{
		switch (e.Key.Value) {
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
			case Gdk.Constants.KEY_Escape:
				// Without a shape, let Enter fall through to Deselect.
				if (shape is null || drag != DragMode.None)
					return false;
				Commit ();
				return true;
			case Gdk.Constants.KEY_Up:
				return Nudge (0, -1, e);
			case Gdk.Constants.KEY_Down:
				return Nudge (0, 1, e);
			case Gdk.Constants.KEY_Left:
				return Nudge (-1, 0, e);
			case Gdk.Constants.KEY_Right:
				return Nudge (1, 0, e);
			case Gdk.Constants.KEY_bracketleft:
				BrushWidth -= e.IsControlPressed ? BaseBrushTool.BrushWidthLargeStep : 1;
				return true;
			case Gdk.Constants.KEY_bracketright:
				BrushWidth += e.IsControlPressed ? BaseBrushTool.BrushWidthLargeStep : 1;
				return true;
			default:
				return HandleToolKey (e);
		}
	}

	/// <summary>The arrow keys move the shape by a pixel, or by ten with Ctrl.</summary>
	private bool Nudge (double dx, double dy, ToolKeyEventArgs e)
	{
		if (shape is null || drag != DragMode.None)
			return false;

		double step = e.IsControlPressed ? 10 : 1;
		Matrix m = CairoExtensions.CreateIdentityMatrix ();
		m.Translate (dx * step, dy * step);
		shape.Transform (m);
		Redraw ();
		return true;
	}

	public void HandleMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (drag != DragMode.None || e.MouseButton is not (MouseButton.Left or MouseButton.Right))
			return;

		save_commit = null;

		bool right = e.MouseButton == MouseButton.Right;
		(DragMode mode, int nub) = HitTest (e.WindowPoint, right);

		if (mode == DragMode.None) {
			// A click outside the shape commits it and starts a new one.
			Commit ();
			StartShape (document, e.PointDouble, right);
			return;
		}

		drag = mode;
		drag_nub = nub;
		drag_origin = e.PointDouble;
		drag_start = shape!.Clone ();
		UpdateHandles ();
	}

	public void HandleMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (drag == DragMode.None || shape is null || drag_start is null) {
			UpdateCursor (e.WindowPoint);
			return;
		}

		PointD current = e.PointDouble;

		switch (drag) {
			case DragMode.Create:
				UpdateCreate (shape, drag_origin, current, e.IsShiftPressed, e.IsAltPressed);
				break;
			case DragMode.Move: {
					// Whole pixels, so that moving doesn't blur the shape.
					Matrix m = CairoExtensions.CreateIdentityMatrix ();
					m.Translate (Math.Round (current.X - drag_origin.X), Math.Round (current.Y - drag_origin.Y));
					shape = drag_start.Clone ();
					shape.Transform (m);
					break;
				}
			case DragMode.Rotate:
				shape = drag_start.Clone ();
				shape.Transform (ComputeRotation (drag_start, drag_origin, current, e.IsShiftPressed));
				PintaCore.Chrome.SetStatusBarText (Translations.GetString ("Angle: {0}°", RotationDegrees (drag_start, shape).ToString ("F2")));
				break;
			case DragMode.Nub:
				shape = drag_start.Clone ();
				DragNub (shape, drag_nub, current, e.IsShiftPressed, e.IsAltPressed);
				break;
			case DragMode.Pivot:
				if (shape is BoxShape box)
					box.Frame.PivotLocal = box.Frame.ToLocal (current);
				break;
		}

		Redraw ();
	}

	public void HandleMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (drag == DragMode.None)
			return;

		DragMode finished = drag;
		drag = DragMode.None;
		drag_start = null;

		if (finished == DragMode.Rotate) // Put the tool's hint back in place of the angle readout.
			PintaCore.Chrome.SetStatusBarText ($" {owner.Name}: {owner.StatusBarText}");

		if (finished == DragMode.Create && shape is not null && item is null) {
			if (IsDegenerate (shape)) {
				ClearPending ();
				return;
			}

			item = new ShapeHistoryItem (this, owner.Icon, owner.Name, layer!);
			this.document!.History.PushNewItem (item);
		}

		Redraw ();
		UpdateCursor (e.WindowPoint);
	}

	#endregion

	#region Pending shape

	private void StartShape (Document doc, PointD start, bool useSecondaryColor)
	{
		document = doc;
		layer = doc.Layers.CurrentUserLayer;
		drawing_layer = new ReEditableLayer (layer);
		item = null;
		shape = CreateShape (start, useSecondaryColor);
		drag = DragMode.Create;
		drag_origin = start;
		drag_start = shape.Clone ();
		last_dirty = null;
		UpdateHandles ();
	}

	/// <summary>
	/// Draws the shape onto its layer and ends editing. Returns the history item that holds it.
	/// </summary>
	protected ShapeHistoryItem? Commit ()
	{
		if (shape is null || document is null || layer is null)
			return null;

		if (drag == DragMode.Create && item is null && IsDegenerate (shape)) {
			drag = DragMode.None;
			ClearPending ();
			return null;
		}

		drag = DragMode.None;
		drag_start = null;

		ImageSurface before = layer.Surface.Clone ();
		RectangleD dirty;
		using (Context g = CreateClippedContext (document, layer.Surface))
			dirty = Draw (g, shape);

		ShapeHistoryItem committed;
		if (item is not null && !item.IsCommitted && document.History.Current == item) {
			committed = item;
			committed.Commit (before);
		} else {
			// Something else went onto the history while the shape was edited, so it gets an item of its own.
			committed = new ShapeHistoryItem (this, owner.Icon, owner.Name, layer);
			committed.Commit (before);
			document.History.PushNewItem (committed);
		}

		Document doc = document;
		ClearPending ();
		doc.Workspace.Invalidate (dirty.Inflated (2, 2).ToInt ());
		return committed;
	}

	/// <summary>Removes the shape being edited (Undo of its history item). Returns it for Redo.</summary>
	internal EditableShape? TakePendingShape (ShapeHistoryItem forItem)
	{
		if (item != forItem || shape is null)
			return null;

		EditableShape taken = shape;
		ClearPending ();
		return taken;
	}

	/// <summary>Brings a shape back for editing (Redo of its history item, or after saving).</summary>
	internal void RestorePendingShape (ShapeHistoryItem forItem, EditableShape restored)
	{
		if (tools.CurrentTool != owner)
			tools.SetCurrentTool (owner);

		if (shape is not null)
			ClearPending ();

		document = workspace.ActiveDocument;
		layer = forItem.Layer;
		drawing_layer = new ReEditableLayer (layer);
		item = forItem;
		shape = restored;
		last_dirty = null;
		Redraw ();
	}

	private void ClearPending ()
	{
		drawing_layer?.TryRemoveLayer ();

		if (document is not null && last_dirty is RectangleD dirty)
			document.Workspace.Invalidate (dirty.ToInt ());

		shape = null;
		item = null;
		drawing_layer = null;
		layer = null;
		document = null;
		last_dirty = null;
		drag = DragMode.None;
		drag_start = null;
		UpdateHandles ();
	}

	/// <summary>Redraws the shape being edited, e.g. after a tool bar setting changed.</summary>
	public void Redraw ()
	{
		if (shape is null || document is null || drawing_layer is null) {
			UpdateHandles ();
			return;
		}

		Layer target = drawing_layer.Layer;
		target.Clear ();

		RectangleD dirty;
		using (Context g = CreateClippedContext (document, target.Surface))
			dirty = Draw (g, shape).Inflated (2, 2);

		RectangleD invalidate = last_dirty is RectangleD last ? dirty.Union (last) : dirty;
		last_dirty = dirty;
		document.Workspace.Invalidate (invalidate.ToInt ());

		UpdateHandles ();
	}

	private static Context CreateClippedContext (Document doc, ImageSurface surface)
	{
		Context g = new (surface);
		g.AppendPath (doc.Selection.SelectionPath);
		g.FillRule = FillRule.EvenOdd;
		g.Clip ();
		return g;
	}

	private RectangleD Draw (Context g, EditableShape s)
	{
		g.Antialias = owner.UseAntialiasing ? Antialias.Subpixel : Antialias.None;

		Color primary = palette.PrimaryColor;
		Color secondary = palette.SecondaryColor;
		if (s.UseSecondaryColor)
			(primary, secondary) = (secondary, primary);

		// Paint.NET: outline and fill-only use the primary colour; fill with outline fills with the secondary.
		Color outline = primary;
		Color fill = StrokeShape ? secondary : primary;

		return DrawShape (g, s, outline, fill);
	}

	#endregion

	#region Hit testing and handles

	private (DragMode, int) HitTest (PointD windowPoint, bool right)
	{
		if (shape is null)
			return (DragMode.None, 0);

		if (!right) {
			for (int i = 0; i < nub_handles.Length; i++) {
				if (nub_handles[i].Active && nub_handles[i].ContainsPoint (windowPoint))
					return (DragMode.Nub, i);
			}

			if (pivot_handle.Active && pivot_handle.ContainsPoint (windowPoint))
				return (DragMode.Pivot, 0);
		}

		if (move_handle.Active && move_handle.ContainsPoint (windowPoint))
			return (DragMode.Move, 0);

		PointD[] outline = shape.Outline.Select (workspace.CanvasPointToView).ToArray ();
		bool inside = TransformFrame.IsInside (outline, windowPoint) || DistanceToOutline (outline, windowPoint) <= OutlineMargin;

		if (inside)
			return (right ? DragMode.Rotate : DragMode.Move, 0);

		if (HasRotateCorridor && DistanceToOutline (outline, windowPoint) <= ROTATE_CORRIDOR)
			return (DragMode.Rotate, 0);

		return (DragMode.None, 0);
	}

	private static double DistanceToOutline (PointD[] outline, PointD p)
	{
		double distance = double.MaxValue;
		for (int i = 0; i < outline.Length; i++)
			distance = Math.Min (distance, TransformFrame.DistanceToSegment (p, outline[i], outline[(i + 1) % outline.Length]));
		return distance;
	}

	private void UpdateHandles ()
	{
		RectangleI dirty = HandlesRect ();

		bool visible = shape is not null && drag != DragMode.Create;

		if (visible) {
			IReadOnlyList<PointD> nubs = shape!.Nubs;
			for (int i = 0; i < nub_handles.Length; i++)
				nub_handles[i].CanvasPosition = nubs[i];
			pivot_handle.CanvasPosition = shape.Pivot;
			move_handle.CanvasAnchor = MoveIconAnchor (shape);
		}

		foreach (MoveHandle h in nub_handles)
			h.Active = visible;
		pivot_handle.Active = visible && HasPivotHandle;
		move_handle.Active = visible;

		if (workspace.HasOpenDocuments)
			workspace.InvalidateWindowRect (dirty.Union (HandlesRect ()));
	}

	private RectangleI HandlesRect ()
	{
		if (!workspace.HasOpenDocuments)
			return RectangleI.Zero;

		RectangleI rect = MoveHandle.UnionInvalidateRects (nub_handles.Append (pivot_handle).Where (h => h.Active));
		return move_handle.Active ? rect.Union (move_handle.InvalidateRect) : rect;
	}

	private void UpdateCursor (PointD windowPoint)
	{
		Gdk.Cursor? cursor = HitTest (windowPoint, right: false).Item1 switch {
			DragMode.Nub or DragMode.Pivot => nub_cursor ??= GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Grab),
			DragMode.Move => move_cursor ??= GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Move),
			DragMode.Rotate => BaseTransformTool.RotateCursor,
			_ => owner.DefaultCursor,
		};

		if (cursor != owner.CurrentCursor)
			owner.SetCursor (cursor);
	}

	private static double RotationDegrees (EditableShape from, EditableShape to)
	{
		// Compare the direction of the first two nubs before and after.
		IReadOnlyList<PointD> a = from.Nubs, b = to.Nubs;
		int last = a.Count - 1;
		double before = Math.Atan2 (a[last].Y - a[0].Y, a[last].X - a[0].X);
		double after = Math.Atan2 (b[last].Y - b[0].Y, b[last].X - b[0].X);
		return TransformFrame.NormalizeDegrees ((after - before) * 180 / Math.PI);
	}

	/// <summary>A rotation about <paramref name="pivot"/> by the angle the mouse swept, snapped to 15 degree steps with Shift.</summary>
	protected static Matrix RotationAbout (PointD pivot, PointD from, PointD to, bool snap)
	{
		double delta = Math.Atan2 (to.Y - pivot.Y, to.X - pivot.X) - Math.Atan2 (from.Y - pivot.Y, from.X - pivot.X);
		if (snap)
			delta = Math.Round (delta / (Math.PI / 12)) * (Math.PI / 12);

		Matrix m = CairoExtensions.CreateIdentityMatrix ();
		m.Translate (pivot.X, pivot.Y);
		m.Rotate (delta);
		m.Translate (-pivot.X, -pivot.Y);
		return m;
	}

	#endregion
}
