//
// BaseTransformTool.cs
//
// Author:
//       Volodymyr <${AuthorEmail}>
//
// Copyright (c) 2012 Volodymyr
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
/// Shared behaviour of Paint.NET's two move tools: 8 control nubs on a frame that turns with the
/// content, a movable centre of rotation, a rotate corridor just outside the frame, and Ctrl to
/// work on a copy.
/// </summary>
public abstract class BaseTransformTool : BaseTool
{
	private enum DragMode { None, Move, Rotate, Scale, Pivot }

	// Width in window pixels of the corridor just outside the frame where a drag rotates.
	private const double ROTATE_CORRIDOR = 16;

	// The centre of rotation sits where a reflexive move drag starts, so a press only grabs it once
	// the pointer has rested on it this long; a press that comes sooner moves the pixels.
	private const int PIVOT_DWELL_MS = 400;

	private static Gdk.Cursor? rotate_cursor;
	private static Gdk.Cursor? nub_cursor;

	/// <summary>A double-headed curved arrow, shared with the shape tools.</summary>
	internal static Gdk.Cursor RotateCursor => rotate_cursor ??= CreateRotateCursor ();

	private readonly IWorkspaceService workspace;
	private readonly Matrix transform = CairoExtensions.CreateIdentityMatrix ();
	private readonly TransformFrame frame = new ();
	private TransformFrame drag_frame = new ();
	private bool frame_valid = false;
	private readonly MoveHandle[] nubs;
	private readonly MoveHandle pivot_handle;
	private PointD original_point;
	private DragMode mode = DragMode.None;
	private int active_nub;
	private bool using_mouse = false;
	private long pivot_hover_since = -1; // Environment.TickCount64 when the pointer reached the pivot, or -1.
	private uint pivot_dwell_timer = 0;
	private PointD last_hover_point;

	/// <summary>
	/// Initializes a new instance of the <see cref="BaseTransformTool"/> class.
	/// </summary>
	public BaseTransformTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();

		nubs = Enumerable.Range (0, TransformFrame.NUB_COUNT).Select (_ => new MoveHandle (workspace)).ToArray ();
		pivot_handle = new MoveHandle (workspace) { Radius = 6, Crosshair = true };

		// Any selection change made elsewhere (other tools, undo, Deselect) starts a fresh frame.
		workspace.SelectionChanged += (_, _) => {
			if (!IsActive)
				frame_valid = false;
		};
		workspace.ActiveDocumentChanged += (_, _) => frame_valid = false;
	}

	public override IEnumerable<IToolHandle> Handles {
		get {
			UpdateHandles ();
			return [.. nubs, pivot_handle];
		}
	}

	/// <summary>
	/// Whether the current drag was started with Ctrl, which works on a copy and leaves the original in place.
	/// </summary>
	protected bool IsCopying { get; private set; }

	/// <summary>
	/// Whether the nubs are shown. By default only while there is a visible selection.
	/// </summary>
	protected virtual bool ShowFrame (Document document)
		=> document.Selection.Visible;

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);
		frame_valid = false;
	}

	protected override void OnMouseDown (
		Document document,
		ToolMouseEventArgs e)
	{
		if (mode != DragMode.None)
			return;

		// As in Paint.NET, the drag may start anywhere, even outside the canvas.
		original_point = e.PointDouble;

		if (e.MouseButton == MouseButton.Right) {
			mode = DragMode.Rotate; // The right button always rotates.
		} else {
			(mode, active_nub) = HitTest (document, e.WindowPoint);

			if (mode == DragMode.Pivot)
				return;
		}

		IsCopying = e.IsControlPressed;
		using_mouse = true;

		OnStartTransform (document);
	}

	protected override void OnMouseMove (
		Document document,
		ToolMouseEventArgs e)
	{
		if (mode == DragMode.Pivot) {
			// The centre of rotation can go anywhere, even off-canvas.
			frame.PivotLocal = frame.ToLocal (e.PointDouble);
			document.Workspace.Invalidate ();
			return;
		}

		if (!IsActive || !using_mouse) {
			UpdateCursor (document, e.WindowPoint);
			return;
		}

		switch (mode) {
			case DragMode.Rotate:
				transform.InitMatrix (drag_frame.ComputeRotation (original_point, e.PointDouble, e.IsShiftPressed));
				break;
			case DragMode.Scale:
				transform.InitMatrix (drag_frame.ComputeScale (active_nub, e.PointDouble, keepAspect: e.IsShiftPressed, fromCenter: e.IsAltPressed));
				break;
			default:
				// The cursor position can be a subpixel value. Round to an integer
				// so that we only translate by entire pixels.
				// (Otherwise, blurring / anti-aliasing may be introduced)
				transform.InitIdentity ();
				transform.Translate (
					Math.Floor (e.PointDouble.X - original_point.X),
					Math.Floor (e.PointDouble.Y - original_point.Y));
				break;
		}

		UpdateFrame ();

		if (mode == DragMode.Rotate)
			PintaCore.Chrome.SetStatusBarText (Translations.GetString ("Angle: {0}°", frame.AngleDegrees.ToString ("F2")));

		OnUpdateTransform (document, transform);
	}

	protected override void OnMouseUp (
		Document document,
		ToolMouseEventArgs e)
	{
		if (mode == DragMode.Pivot) {
			mode = DragMode.None;
			return;
		}

		if (!IsActive || !using_mouse)
			return;

		OnFinishTransform (document, transform);
		UpdateCursor (document, e.WindowPoint);
	}

	protected override bool OnKeyDown (
		Document document,
		ToolKeyEventArgs e)
	{
		if (using_mouse || mode == DragMode.Pivot) // Don't handle the arrow keys while already interacting via the mouse.
			return base.OnKeyDown (document, e);

		double dx = 0.0;
		double dy = 0.0;
		double coeff = e.IsControlPressed ? 10.0 : 1.0;

		switch (e.Key.Value) {
			case Gdk.Constants.KEY_Left:
				dx = -coeff;
				break;
			case Gdk.Constants.KEY_Right:
				dx = coeff;
				break;
			case Gdk.Constants.KEY_Up:
				dy = -coeff;
				break;
			case Gdk.Constants.KEY_Down:
				dy = coeff;
				break;
			default:
				// Otherwise, let the key be handled elsewhere.
				return base.OnKeyDown (document, e);
		}

		if (!IsActive) {
			mode = DragMode.Move;
			IsCopying = false;
			OnStartTransform (document);
		}

		transform.Translate (dx, dy);
		UpdateFrame ();
		OnUpdateTransform (document, transform);

		return true;
	}

	protected override bool OnKeyUp (
		Document document,
		ToolKeyEventArgs e)
	{
		if (IsActive && !using_mouse)
			OnFinishTransform (document, transform);

		return base.OnKeyUp (document, e);
	}

	protected abstract RectangleD GetSourceRectangle (Document document);

	protected virtual void OnStartTransform (Document document)
	{
		EnsureFrame (document);
		drag_frame = frame.Clone ();
		transform.InitIdentity ();
	}

	protected virtual void OnUpdateTransform (
		Document document,
		Matrix transform)
	{ }

	protected virtual void OnFinishTransform (
		Document document,
		Matrix transform)
	{
		if (mode == DragMode.Rotate) // Put the tool's hint back in place of the angle readout.
			PintaCore.Chrome.SetStatusBarText ($" {Name}: {StatusBarText}");

		mode = DragMode.None;
		using_mouse = false;
		IsCopying = false;
	}

	private bool IsActive
		=> mode is DragMode.Move or DragMode.Rotate or DragMode.Scale;

	private void EnsureFrame (Document document)
	{
		if (frame_valid)
			return;

		frame.Reset (GetSourceRectangle (document));
		frame_valid = true;
	}

	private void UpdateFrame ()
		=> frame.Matrix.InitMatrix (TransformFrame.Compose (drag_frame.Matrix, transform));

	private void UpdateHandles ()
	{
		bool visible = workspace.HasOpenDocuments && ShowFrame (workspace.ActiveDocument);

		if (visible) {
			EnsureFrame (workspace.ActiveDocument);

			for (int i = 0; i < nubs.Length; i++)
				nubs[i].CanvasPosition = frame.GetNub (i);

			pivot_handle.CanvasPosition = frame.Pivot;
		}

		foreach (MoveHandle handle in nubs)
			handle.Active = visible;

		pivot_handle.Active = visible;
	}

	private (DragMode, int) HitTest (Document document, PointD windowPoint)
	{
		UpdateHandles ();

		if (!pivot_handle.Active)
			return (DragMode.Move, 0);

		for (int i = 0; i < nubs.Length; i++) {
			if (nubs[i].ContainsPoint (windowPoint))
				return (DragMode.Scale, i);
		}

		if (pivot_handle.ContainsPoint (windowPoint) && pivot_hover_since >= 0
			&& Environment.TickCount64 - pivot_hover_since >= PIVOT_DWELL_MS)
			return (DragMode.Pivot, 0);

		PointD[] outline = frame.GetCorners ().Select (document.Workspace.CanvasPointToView).ToArray ();

		return TransformFrame.HitTest (outline, windowPoint, ROTATE_CORRIDOR) == TransformFrameZone.Rotate
			? (DragMode.Rotate, 0)
			: (DragMode.Move, 0);
	}

	private void UpdateCursor (Document document, PointD windowPoint)
	{
		TrackPivotHover (document, windowPoint);

		Gdk.Cursor? cursor = HitTest (document, windowPoint).Item1 switch {
			DragMode.Rotate => RotateCursor,
			DragMode.Scale or DragMode.Pivot => nub_cursor ??= GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Grab),
			_ => DefaultCursor,
		};

		if (cursor != CurrentCursor)
			SetCursor (cursor);
	}

	/// <summary>
	/// Starts the dwell clock when the pointer reaches the pivot, and shows the grab cursor once it runs out.
	/// </summary>
	private void TrackPivotHover (Document document, PointD windowPoint)
	{
		last_hover_point = windowPoint;

		if (!pivot_handle.Active || !pivot_handle.ContainsPoint (windowPoint)) {
			pivot_hover_since = -1;
			return;
		}

		if (pivot_hover_since >= 0)
			return;

		pivot_hover_since = Environment.TickCount64;

		if (pivot_dwell_timer != 0)
			GLib.Source.Remove (pivot_dwell_timer);

		pivot_dwell_timer = GLib.Functions.TimeoutAdd (GLib.Constants.PRIORITY_DEFAULT, PIVOT_DWELL_MS, () => {
			pivot_dwell_timer = 0;
			if (IsActiveTool () && mode == DragMode.None && workspace.HasOpenDocuments && workspace.ActiveDocument == document)
				UpdateCursor (document, last_hover_point);
			return false;
		});
	}

	/// <summary>
	/// A double-headed curved arrow, Paint.NET's sign that a drag rotates.
	/// </summary>
	private static Gdk.Cursor CreateRotateCursor ()
	{
		const int SIZE = 24;
		PointD a = new (7, 3), c1 = new (15, 7), c2 = new (17, 14), b = new (14, 21);

		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, SIZE, SIZE);
		using Context g = new (surface);
		g.LineCap = LineCap.Round;
		g.LineJoin = LineJoin.Round;

		void ArrowHead (PointD tip, PointD from)
		{
			double dx = tip.X - from.X, dy = tip.Y - from.Y;
			double len = Math.Sqrt (dx * dx + dy * dy);
			dx /= len;
			dy /= len;
			g.MoveTo (tip.X + dx, tip.Y + dy);
			g.LineTo (tip.X - 5 * dx - 3.5 * dy, tip.Y - 5 * dy + 3.5 * dx);
			g.LineTo (tip.X - 5 * dx + 3.5 * dy, tip.Y - 5 * dy - 3.5 * dx);
			g.ClosePath ();
			g.FillPreserve ();
			g.Stroke ();
		}

		void Arrow (double width, Color color)
		{
			g.SetSourceColor (color);
			g.LineWidth = width;
			g.MoveTo (a.X, a.Y);
			g.CurveTo (c1.X, c1.Y, c2.X, c2.Y, b.X, b.Y);
			g.Stroke ();
			ArrowHead (a, c1);
			ArrowHead (b, c2);
		}

		// A white halo first so the arrow shows on dark images.
		Arrow (3.5, new Color (1, 1, 1));
		Arrow (1.5, new Color (0, 0, 0));

		return Gdk.Cursor.NewFromTexture (surface.ToTexture (), SIZE / 2, SIZE / 2, null);
	}
}
