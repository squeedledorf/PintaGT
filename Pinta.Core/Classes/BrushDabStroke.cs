using System;
using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// The tip stamped by <see cref="BrushDabStroke"/>.
/// </summary>
public enum BrushTip
{
	Circle,
	Square,
}

/// <summary>
/// A Paint.NET style brush stroke: dabs of the brush tip are stamped along the pointer path,
/// one every <c>Spacing</c> × brush size, into a coverage mask that keeps the highest coverage
/// each pixel has seen. A stroke therefore never builds up over itself, however the dabs overlap.
/// With smoothing on, the path follows a centripetal Catmull-Rom spline through the input points.
/// </summary>
public sealed class BrushDabStroke
{
	/// <summary>Brush size (diameter) in pixels.</summary>
	public double Size { get; }
	/// <summary>0 (soft) to 1 (hard). Ignored without antialiasing.</summary>
	public double Hardness { get; }
	/// <summary>Distance between dabs as a fraction of the brush size.</summary>
	public double Spacing { get; }
	public bool Smoothing { get; }
	public bool Antialias { get; }
	public BrushTip Tip { get; }

	// Finer than half a pixel adds work without changing the result.
	private const double MIN_INTERVAL = 0.5;
	// Each spline segment is walked as straight pieces about this long.
	private const double SPLINE_STEP = 2;

	private readonly List<PointD> inputs = [];
	private PointD? last_input;
	private double carry; // distance travelled since the last dab
	private bool finished;

	public BrushDabStroke (double size, double hardness, double spacing, bool smoothing, bool antialias, BrushTip tip = BrushTip.Circle)
	{
		Size = Math.Max (size, 0.01);
		Hardness = Math.Clamp (hardness, 0, 1);
		Spacing = Math.Max (spacing, 0);
		Smoothing = smoothing;
		Antialias = antialias;
		Tip = tip;
	}

	private double Interval => Math.Max (Size * Spacing, MIN_INTERVAL);

	/// <summary>
	/// Adds a pointer position and returns the dab centres it produces. The first point
	/// always gives a dab, so a click without a drag paints. With smoothing, the path
	/// trails one input point behind until <see cref="Finish"/>.
	/// </summary>
	public List<PointD> AddPoint (PointD p)
	{
		List<PointD> dabs = [];
		if (finished || (last_input is PointD prev && prev == p))
			return dabs;

		if (last_input is null) {
			last_input = p;
			inputs.Add (p);
			inputs.Add (p); // The spline's first segment uses the start point as its own neighbour.
			dabs.Add (p);
			return dabs;
		}

		if (!Smoothing) {
			Walk (last_input.Value, p, dabs);
			last_input = p;
			return dabs;
		}

		last_input = p;
		inputs.Add (p);
		if (inputs.Count == 4) {
			WalkSpline (inputs[0], inputs[1], inputs[2], inputs[3], dabs);
			inputs.RemoveAt (0);
		}
		return dabs;
	}

	/// <summary>
	/// Ends the stroke, returning the dabs for the part of the path smoothing held back.
	/// </summary>
	public List<PointD> Finish ()
	{
		List<PointD> dabs = [];
		if (finished)
			return dabs;
		finished = true;

		if (Smoothing && inputs.Count == 3)
			WalkSpline (inputs[0], inputs[1], inputs[2], inputs[2], dabs);
		return dabs;
	}

	private void WalkSpline (PointD p0, PointD p1, PointD p2, PointD p3, List<PointD> dabs)
	{
		double chord = p1.Distance (p2);
		int steps = Math.Max (1, (int) Math.Ceiling (chord / SPLINE_STEP));
		PointD from = p1;
		for (int i = 1; i <= steps; i++) {
			PointD to = i == steps ? p2 : CatmullRom (p0, p1, p2, p3, (double) i / steps);
			Walk (from, to, dabs);
			from = to;
		}
	}

	private void Walk (PointD a, PointD b, List<PointD> dabs)
	{
		double length = a.Distance (b);
		if (length <= 0)
			return;

		double interval = Interval;
		double next = interval - carry;
		while (next <= length) {
			double f = next / length;
			dabs.Add (new PointD (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f));
			next += interval;
		}
		carry = length - (next - interval);
	}

	/// <summary>
	/// A point on the centripetal Catmull-Rom segment from p1 to p2, with u from 0 to 1.
	/// </summary>
	internal static PointD CatmullRom (PointD p0, PointD p1, PointD p2, PointD p3, double u)
	{
		// Knot intervals are sqrt(distance); a floor keeps repeated points from dividing by zero.
		static double Knot (PointD a, PointD b) => Math.Max (Math.Sqrt (a.Distance (b)), 1e-4);

		double t0 = 0;
		double t1 = t0 + Knot (p0, p1);
		double t2 = t1 + Knot (p1, p2);
		double t3 = t2 + Knot (p2, p3);
		double t = t1 + (t2 - t1) * u;

		static PointD Mix (PointD a, PointD b, double ta, double tb, double t)
		{
			double wa = (tb - t) / (tb - ta);
			double wb = (t - ta) / (tb - ta);
			return new PointD (a.X * wa + b.X * wb, a.Y * wa + b.Y * wb);
		}

		PointD a1 = Mix (p0, p1, t0, t1, t);
		PointD a2 = Mix (p1, p2, t1, t2, t);
		PointD a3 = Mix (p2, p3, t2, t3, t);
		PointD b1 = Mix (a1, a2, t0, t2, t);
		PointD b2 = Mix (a2, a3, t1, t3, t);
		return Mix (b1, b2, t1, t2, t);
	}

	/// <summary>
	/// The coverage (0 to 1) of a pixel whose centre is <paramref name="distance"/> from the dab centre.
	/// </summary>
	public double Coverage (double distance)
	{
		double r = Size / 2;
		if (!Antialias)
			return distance < r ? 1 : 0;

		// A one pixel ramp at the rim antialiases a hard tip...
		double edge = Math.Clamp (r - distance + 0.5, 0, 1);
		// ...and below full hardness the tip also fades out from Hardness × radius to the rim's outer
		// edge, so a soft small brush still reaches its full size (a 2px dab is not a single pixel).
		double inner = r * Hardness;
		if (distance <= inner || inner >= r)
			return edge;
		double x = Math.Clamp ((distance - inner) / (r + 0.5 - inner), 0, 1);
		double fade = 1 - x * x * (3 - 2 * x); // smoothstep
		return Math.Min (edge, fade);
	}

	/// <summary>
	/// Where the dab for pointer position <paramref name="p"/> is centred. Without antialiasing
	/// the centre snaps to the pixel grid, so an odd size centres on a pixel and an even size
	/// on a pixel corner, and every dab of a stroke has the same shape.
	/// </summary>
	public PointD DabCentre (PointD p)
	{
		if (Antialias)
			return p;
		if (((int) Math.Round (Size)) % 2 == 1)
			return new PointD (Math.Floor (p.X) + 0.5, Math.Floor (p.Y) + 0.5);
		// Round halves up: the tools pass screen pixel centres, which sit on .5 at 100% zoom.
		return new PointD (Math.Floor (p.X + 0.5), Math.Floor (p.Y + 0.5));
	}

	/// <summary>
	/// Stamps a dab into a coverage mask of black pixels whose alpha is the coverage,
	/// keeping the higher coverage where pixels are already covered.
	/// Returns the pixels the dab can touch, clipped to the mask (empty if none).
	/// </summary>
	public RectangleI Stamp (Span<ColorBgra> mask, int width, int height, PointD position)
	{
		PointD c = DabCentre (position);
		double reach = Size / 2 + 1;
		RectangleI bounds = RectangleI.FromLTRB (
			(int) Math.Floor (c.X - reach), (int) Math.Floor (c.Y - reach),
			(int) Math.Ceiling (c.X + reach), (int) Math.Ceiling (c.Y + reach));
		bounds = bounds.Intersect (new RectangleI (0, 0, width, height));
		if (bounds.IsEmpty)
			return RectangleI.Zero;

		for (int y = bounds.Top; y <= bounds.Bottom; y++) {
			Span<ColorBgra> row = mask.Slice (y * width, width);
			double dy = y + 0.5 - c.Y;
			for (int x = bounds.Left; x <= bounds.Right; x++) {
				double dx = x + 0.5 - c.X;
				double d = Tip == BrushTip.Square
					? Math.Max (Math.Abs (dx), Math.Abs (dy))
					: Math.Sqrt (dx * dx + dy * dy);
				byte a = (byte) Math.Round (Coverage (d) * 255);
				if (a > row[x].A)
					row[x] = ColorBgra.FromBgra (0, 0, 0, a);
			}
		}
		return bounds;
	}
}
