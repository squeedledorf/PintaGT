using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Paint.NET's control nub for live fills and text: a small square marking the anchor point, with a box holding
/// a four-way arrow just below and to the right of it. Dragging the box moves the anchor.
/// </summary>
public sealed class MoveNubHandle : IToolHandle
{
	private static readonly Gdk.RGBA fill_color = new () { Red = 1, Green = 1, Blue = 1, Alpha = 0.9f };
	private static readonly Gdk.RGBA stroke_color = new () { Red = 0.15f, Green = 0.15f, Blue = 0.15f, Alpha = 1 };

	private const float MARK_SIZE = 4;
	private const float BOX_SIZE = 15;
	private const float BOX_OFFSET = 10;

	private readonly IWorkspaceService workspace;

	public MoveNubHandle (IWorkspaceService workspace)
	{
		this.workspace = workspace;
	}

	/// <summary>The anchor point, in canvas coordinates.</summary>
	public PointD CanvasPosition { get; set; }

	public bool Active { get; set; }

	public Gdk.Cursor Cursor { get; } = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Move);

	private RectangleD BoxRect {
		get {
			PointD p = workspace.CanvasPointToView (CanvasPosition);
			return new RectangleD (p.X + BOX_OFFSET, p.Y + BOX_OFFSET, BOX_SIZE, BOX_SIZE);
		}
	}

	public bool ContainsPoint (PointD windowPoint)
		=> Active && BoxRect.Inflated (3, 3).ContainsPoint (windowPoint);

	/// <summary>The window area to invalidate when the nub moves.</summary>
	public RectangleI InvalidateRect {
		get {
			PointD p = workspace.CanvasPointToView (CanvasPosition);
			return new RectangleD (p.X - MARK_SIZE, p.Y - MARK_SIZE, BOX_OFFSET + BOX_SIZE + MARK_SIZE + 2, BOX_OFFSET + BOX_SIZE + MARK_SIZE + 2).ToInt ();
		}
	}

	public void Draw (Gtk.Snapshot snapshot)
	{
		PointD p = workspace.CanvasPointToView (CanvasPosition);
		RectangleD box = BoxRect;
		float x = (float) p.X, y = (float) p.Y;
		float bx = (float) box.X + 0.5f, by = (float) box.Y + 0.5f;

		Gsk.PathBuilder outline = Gsk.PathBuilder.New ();
		outline.AddRect (Graphene.Rect.Alloc ().Init (x - MARK_SIZE / 2, y - MARK_SIZE / 2, MARK_SIZE, MARK_SIZE));
		outline.AddRect (Graphene.Rect.Alloc ().Init (bx, by, BOX_SIZE, BOX_SIZE));
		Gsk.Path outlinePath = outline.ToPath ();
		snapshot.AppendFill (outlinePath, Gsk.FillRule.Winding, fill_color);

		Gsk.Stroke stroke = Gsk.Stroke.New (lineWidth: 1.0f);
		snapshot.AppendStroke (outlinePath, stroke, stroke_color);

		// The four-way arrow: a cross with an arrow head at each end.
		float cx = bx + BOX_SIZE / 2, cy = by + BOX_SIZE / 2, r = BOX_SIZE / 2 - 2.5f, h = 2.5f;
		Gsk.PathBuilder arrows = Gsk.PathBuilder.New ();
		arrows.MoveTo (cx - r, cy);
		arrows.LineTo (cx + r, cy);
		arrows.MoveTo (cx, cy - r);
		arrows.LineTo (cx, cy + r);
		foreach ((float dx, float dy) in new (float, float)[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
			float tx = cx + dx * r, ty = cy + dy * r;
			arrows.MoveTo (tx - dx * h - dy * h, ty - dy * h - dx * h);
			arrows.LineTo (tx, ty);
			arrows.LineTo (tx - dx * h + dy * h, ty - dy * h + dx * h);
		}
		snapshot.AppendStroke (arrows.ToPath (), stroke, stroke_color);
	}
}
