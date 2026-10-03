using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Paint.NET's four-arrows-in-a-square move icon, drawn a fixed window distance below-right of a
/// canvas point (the end of a line, or a corner of a shape's box). Dragging it moves the shape.
/// </summary>
public sealed class MoveIconHandle : IToolHandle
{
	private const float SIZE = 15;
	private const double OFFSET = 14;

	private static readonly Gdk.RGBA fill_color = new () { Red = 1, Green = 1, Blue = 1, Alpha = 1 };
	private static readonly Gdk.RGBA stroke_color = new () { Red = 0.15f, Green = 0.15f, Blue = 0.15f, Alpha = 1 };

	private readonly IWorkspaceService workspace;

	public MoveIconHandle (IWorkspaceService workspace)
	{
		this.workspace = workspace;
	}

	public bool Active { get; set; }

	/// <summary>The canvas point the icon sits next to.</summary>
	public PointD CanvasAnchor { get; set; }

	public bool ContainsPoint (PointD windowPoint)
		=> WindowRect.Inflated (2, 2).ContainsPoint (windowPoint);

	public RectangleI InvalidateRect => WindowRect.Inflated (2, 2).ToInt ();

	private RectangleD WindowRect {
		get {
			PointD anchor = workspace.CanvasPointToView (CanvasAnchor);
			return new (anchor.X + OFFSET, anchor.Y + OFFSET, SIZE, SIZE);
		}
	}

	public void Draw (Gtk.Snapshot snapshot)
	{
		RectangleD r = WindowRect;
		float x = (float) r.X + 0.5f, y = (float) r.Y + 0.5f;
		float cx = x + SIZE / 2, cy = y + SIZE / 2;
		const float ARM = 5, HEAD = 2;

		Gsk.PathBuilder box = Gsk.PathBuilder.New ();
		box.MoveTo (x, y);
		box.LineTo (x + SIZE, y);
		box.LineTo (x + SIZE, y + SIZE);
		box.LineTo (x, y + SIZE);
		box.Close ();
		Gsk.Path boxPath = box.ToPath ();
		snapshot.AppendFill (boxPath, Gsk.FillRule.Winding, fill_color);

		Gsk.Stroke stroke = Gsk.Stroke.New (lineWidth: 1.0f);
		snapshot.AppendStroke (boxPath, stroke, stroke_color);

		// The four arrows: a cross with a small head at each end.
		Gsk.PathBuilder arrows = Gsk.PathBuilder.New ();
		arrows.MoveTo (cx - ARM, cy);
		arrows.LineTo (cx + ARM, cy);
		arrows.MoveTo (cx, cy - ARM);
		arrows.LineTo (cx, cy + ARM);
		arrows.MoveTo (cx - ARM + HEAD, cy - HEAD);
		arrows.LineTo (cx - ARM, cy);
		arrows.LineTo (cx - ARM + HEAD, cy + HEAD);
		arrows.MoveTo (cx + ARM - HEAD, cy - HEAD);
		arrows.LineTo (cx + ARM, cy);
		arrows.LineTo (cx + ARM - HEAD, cy + HEAD);
		arrows.MoveTo (cx - HEAD, cy - ARM + HEAD);
		arrows.LineTo (cx, cy - ARM);
		arrows.LineTo (cx + HEAD, cy - ARM + HEAD);
		arrows.MoveTo (cx - HEAD, cy + ARM - HEAD);
		arrows.LineTo (cx, cy + ARM);
		arrows.LineTo (cx + HEAD, cy + ARM - HEAD);
		snapshot.AppendStroke (arrows.ToPath (), stroke, stroke_color);
	}
}
