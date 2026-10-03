using System;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Where the pointer is relative to a move tool's frame, which decides what a drag does.
/// </summary>
public enum TransformFrameZone
{
	/// <summary>Inside the frame, or well outside it: the drag moves.</summary>
	Move,
	/// <summary>A corridor just outside the frame: the drag rotates about the pivot.</summary>
	Rotate,
}

/// <summary>
/// Paint.NET's move-tool frame: a rectangle carried by a transform, with 8 control nubs on its
/// corners and edges and a rotation pivot. The frame rotates and scales along with the content.
/// </summary>
public sealed class TransformFrame
{
	public const int NUB_COUNT = 8;

	// Nub positions as fractions of the frame, clockwise from the top-left corner.
	private static readonly (double X, double Y)[] nub_factors = [
		(0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5),
	];

	/// <summary>The untransformed frame.</summary>
	public RectangleD Rect { get; private set; }

	/// <summary>Maps the untransformed frame into canvas space.</summary>
	public Matrix Matrix { get; } = CairoExtensions.CreateIdentityMatrix ();

	/// <summary>The rotation pivot, in the untransformed frame's space so it travels with the content.</summary>
	public PointD PivotLocal { get; set; }

	public void Reset (RectangleD rect)
	{
		Rect = rect;
		Matrix.InitIdentity ();
		PivotLocal = rect.GetCenter ();
	}

	public TransformFrame Clone ()
	{
		TransformFrame clone = new () { Rect = Rect, PivotLocal = PivotLocal };
		clone.Matrix.InitMatrix (Matrix);
		return clone;
	}

	public PointD Pivot => Matrix.TransformPoint (PivotLocal);

	public PointD GetNub (int nub) => Matrix.TransformPoint (GetNubLocal (nub));

	/// <summary>The four corners in canvas space, in order around the frame.</summary>
	public PointD[] GetCorners () => [GetNub (0), GetNub (2), GetNub (4), GetNub (6)];

	public PointD ToLocal (PointD canvasPoint)
	{
		Matrix inverse = Matrix.Clone ();
		inverse.Invert ();
		return inverse.TransformPoint (canvasPoint);
	}

	/// <summary>
	/// The frame's rotation in degrees, counter-clockwise positive as on screen, in (-180, 180].
	/// </summary>
	public double AngleDegrees {
		get {
			double dx = 1, dy = 0;
			Matrix.TransformDistance (ref dx, ref dy);
			return NormalizeDegrees (-Math.Atan2 (dy, dx) * 180 / Math.PI);
		}
	}

	/// <summary>
	/// Canvas-space transform that rotates about the pivot by the angle the pointer swept from
	/// <paramref name="start"/> to <paramref name="current"/>. With <paramref name="snap"/>, the frame's
	/// resulting angle lands on a multiple of 15 degrees.
	/// </summary>
	public Matrix ComputeRotation (PointD start, PointD current, bool snap)
	{
		PointD pivot = Pivot;
		double delta = Math.Atan2 (current.Y - pivot.Y, current.X - pivot.X) - Math.Atan2 (start.Y - pivot.Y, start.X - pivot.X);

		if (snap) {
			// The frame angle is counter-clockwise positive; a positive Cairo rotation is clockwise on screen.
			double step = Math.PI / 12;
			double current_angle = -AngleDegrees * Math.PI / 180;
			delta = Math.Round ((current_angle + delta) / step) * step - current_angle;
		}

		Matrix m = CairoExtensions.CreateIdentityMatrix ();
		m.Translate (pivot.X, pivot.Y);
		m.Rotate (delta);
		m.Translate (-pivot.X, -pivot.Y);
		return m;
	}

	/// <summary>
	/// Canvas-space transform that scales the frame as <paramref name="nub"/> is dragged to
	/// <paramref name="canvasPoint"/>. The opposite nub stays put, or the centre with
	/// <paramref name="fromCenter"/>; <paramref name="keepAspect"/> keeps the width-to-height ratio.
	/// Dragging past the opposite nub flips the frame.
	/// </summary>
	public Matrix ComputeScale (int nub, PointD canvasPoint, bool keepAspect, bool fromCenter)
	{
		(double fx, double fy) = nub_factors[nub];
		RectangleD r = Rect;
		PointD center = r.GetCenter ();
		PointD q = ToLocal (canvasPoint);

		double ax = (fromCenter || fx == 0.5) ? center.X : r.X + (1 - fx) * r.Width;
		double ay = (fromCenter || fy == 0.5) ? center.Y : r.Y + (1 - fy) * r.Height;

		double sx = (fx == 0.5) ? 1 : Ratio (q.X - ax, r.X + fx * r.Width - ax);
		double sy = (fy == 0.5) ? 1 : Ratio (q.Y - ay, r.Y + fy * r.Height - ay);

		if (keepAspect) {
			if (fx == 0.5)
				sx = Math.Abs (sy);
			else if (fy == 0.5)
				sy = Math.Abs (sx);
			else {
				double s = Math.Max (Math.Abs (sx), Math.Abs (sy));
				sx = SignOf (sx) * s;
				sy = SignOf (sy) * s;
			}
		}

		// Never collapse below one pixel, which would lose the content.
		sx = AtLeastOnePixel (sx, r.Width);
		sy = AtLeastOnePixel (sy, r.Height);

		Matrix local = CairoExtensions.CreateIdentityMatrix ();
		local.Translate (ax, ay);
		local.Scale (sx, sy);
		local.Translate (-ax, -ay);

		Matrix inverse = Matrix.Clone ();
		inverse.Invert ();
		return Compose (Compose (inverse, local), Matrix);
	}

	/// <summary>
	/// Returns the transform that applies <paramref name="first"/> and then <paramref name="then"/>.
	/// </summary>
	public static Matrix Compose (Matrix first, Matrix then)
	{
		Matrix result = first.Clone ();
		result.Multiply (then);
		return result;
	}

	/// <summary>
	/// Classifies a point against the frame outline (a convex polygon, all in window space):
	/// inside or well outside moves, a corridor of <paramref name="corridor"/> just outside rotates.
	/// </summary>
	public static TransformFrameZone HitTest (ReadOnlySpan<PointD> outline, PointD p, double corridor)
	{
		if (IsInside (outline, p))
			return TransformFrameZone.Move;

		double distance = double.MaxValue;
		for (int i = 0; i < outline.Length; i++)
			distance = Math.Min (distance, DistanceToSegment (p, outline[i], outline[(i + 1) % outline.Length]));

		return distance <= corridor ? TransformFrameZone.Rotate : TransformFrameZone.Move;
	}

	public static double NormalizeDegrees (double degrees)
	{
		degrees %= 360;
		if (degrees <= -180)
			degrees += 360;
		else if (degrees > 180)
			degrees -= 360;
		return Math.Abs (degrees) < 1e-9 ? 0 : degrees;
	}

	private PointD GetNubLocal (int nub)
	{
		(double fx, double fy) = nub_factors[nub];
		return new (Rect.X + fx * Rect.Width, Rect.Y + fy * Rect.Height);
	}

	private static double Ratio (double num, double den)
		=> Math.Abs (den) < 1e-9 ? 1 : num / den;

	private static double SignOf (double v)
		=> v < 0 ? -1 : 1;

	private static double AtLeastOnePixel (double scale, double size)
		=> (size > 0 && Math.Abs (scale * size) < 1) ? SignOf (scale) / size : scale;

	private static bool IsInside (ReadOnlySpan<PointD> polygon, PointD p)
	{
		// Convex polygon: inside when p is on the same side of every edge.
		int sign = 0;
		for (int i = 0; i < polygon.Length; i++) {
			PointD a = polygon[i];
			PointD b = polygon[(i + 1) % polygon.Length];
			double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
			if (Math.Abs (cross) < 1e-9)
				continue;
			int s = Math.Sign (cross);
			if (sign == 0)
				sign = s;
			else if (s != sign)
				return false;
		}
		return true;
	}

	private static double DistanceToSegment (PointD p, PointD a, PointD b)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y;
		double len2 = dx * dx + dy * dy;
		double t = len2 == 0 ? 0 : Math.Clamp (((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
		double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
		return Math.Sqrt (ex * ex + ey * ey);
	}
}
