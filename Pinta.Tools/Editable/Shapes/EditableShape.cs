using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// The geometry of the one shape a shape tool is editing before it is committed.
/// Its style (colours, brush, preset and so on) comes from the tool bar, so changing a setting restyles it.
/// </summary>
public abstract class EditableShape
{
	/// <summary>Drawn with the right mouse button, which swaps the primary and secondary colours.</summary>
	public bool UseSecondaryColor { get; init; }

	public abstract EditableShape Clone ();

	/// <summary>The control nubs, in canvas space.</summary>
	public abstract IReadOnlyList<PointD> Nubs { get; }

	/// <summary>The centre of rotation, in canvas space.</summary>
	public abstract PointD Pivot { get; }

	/// <summary>The outline used to decide whether a click is on the shape, in canvas space (a convex polygon).</summary>
	public abstract PointD[] Outline { get; }

	/// <summary>Applies a canvas-space transform (a move or a rotation) to the shape.</summary>
	public abstract void Transform (Matrix m);
}

/// <summary>
/// A preset shape in a box that can be moved, resized and rotated, but never skewed.
/// </summary>
public sealed class BoxShape : EditableShape
{
	public TransformFrame Frame { get; private init; } = new ();

	public override BoxShape Clone ()
		=> new () { Frame = Frame.Clone (), UseSecondaryColor = UseSecondaryColor };

	public override IReadOnlyList<PointD> Nubs
		=> Enumerable.Range (0, TransformFrame.NUB_COUNT).Select (Frame.GetNub).ToArray ();

	public override PointD Pivot => Frame.Pivot;

	public override PointD[] Outline => Frame.GetCorners ();

	public override void Transform (Matrix m)
		=> Frame.Matrix.InitMatrix (TransformFrame.Compose (Frame.Matrix, m));

	/// <summary>The box's size on the canvas, which the transform's scale makes differ from the frame's rectangle.</summary>
	public (double Width, double Height) Size {
		get {
			double xx = 1, xy = 0, yx = 0, yy = 1;
			Frame.Matrix.TransformDistance (ref xx, ref xy);
			Frame.Matrix.TransformDistance (ref yx, ref yy);
			return (Frame.Rect.Width * Math.Sqrt (xx * xx + xy * xy), Frame.Rect.Height * Math.Sqrt (yx * yx + yy * yy));
		}
	}

	/// <summary>The preset's contours, in canvas space.</summary>
	public IEnumerable<PointD[]> GetContours (ShapePreset preset, double cornerRadius)
	{
		(double w, double h) = Size;
		RectangleD r = Frame.Rect;
		double fx = w > 0 ? r.Width / w : 0;
		double fy = h > 0 ? r.Height / h : 0;

		foreach (PointD[] contour in ShapePresets.GetContours (preset, w, h, cornerRadius))
			yield return contour.Select (p => Frame.Matrix.TransformPoint (new PointD (r.X + p.X * fx, r.Y + p.Y * fy))).ToArray ();
	}
}

/// <summary>
/// A Line/Curve: four nubs (the ends and two in between) that shape it according to the curve type.
/// </summary>
public sealed class LineShape : EditableShape
{
	public const int NUB_COUNT = 4;

	public PointD[] Points { get; private init; } = new PointD[NUB_COUNT];

	public static LineShape FromEnds (PointD start, PointD end, bool useSecondaryColor)
	{
		LineShape shape = new () { UseSecondaryColor = useSecondaryColor };
		shape.SetEnds (start, end);
		return shape;
	}

	/// <summary>Makes the line straight from start to end, with the middle nubs at the thirds.</summary>
	public void SetEnds (PointD start, PointD end)
	{
		for (int i = 0; i < NUB_COUNT; i++) {
			double t = (double) i / (NUB_COUNT - 1);
			Points[i] = new (start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
		}
	}

	public override LineShape Clone ()
		=> new () { Points = (PointD[]) Points.Clone (), UseSecondaryColor = UseSecondaryColor };

	public override IReadOnlyList<PointD> Nubs => Points;

	/// <summary>The centre of the nubs' bounding box: Paint.NET rotates a line about its geometric centre.</summary>
	public override PointD Pivot {
		get {
			double minX = Points.Min (p => p.X), maxX = Points.Max (p => p.X);
			double minY = Points.Min (p => p.Y), maxY = Points.Max (p => p.Y);
			return new ((minX + maxX) / 2, (minY + maxY) / 2);
		}
	}

	public override PointD[] Outline {
		get {
			double minX = Points.Min (p => p.X), maxX = Points.Max (p => p.X);
			double minY = Points.Min (p => p.Y), maxY = Points.Max (p => p.Y);
			return [new (minX, minY), new (maxX, minY), new (maxX, maxY), new (minX, maxY)];
		}
	}

	public override void Transform (Matrix m)
	{
		for (int i = 0; i < Points.Length; i++)
			Points[i] = m.TransformPoint (Points[i]);
	}
}
