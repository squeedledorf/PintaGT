using System;
using System.Collections.Generic;
using System.Linq;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// The Shapes tool's 29 preset shapes, in Paint.NET's dropdown order.
/// </summary>
public enum ShapePreset
{
	Rectangle,
	RoundedRectangle,
	Ellipse,
	Diamond,
	Trapezoid,
	Parallelogram,
	Triangle,
	RightTriangle,

	Pentagon,
	Hexagon,
	Heptagon,
	Octagon,
	ThreePointStar,
	FourPointStar,
	FivePointStar,
	SixPointStar,

	Arrow,
	NotchedArrow,
	PentagonArrow,
	ChevronArrow,

	RectangularCallout,
	RoundedRectangleCallout,
	EllipseCallout,
	CloudCallout,

	LightningBolt,
	CheckMark,
	Multiply,
	Gear,
	Heart,
}

/// <summary>
/// Outlines of the preset shapes. The geometry is drawn from scratch for Pinta.
/// </summary>
public static class ShapePresets
{
	public const double DefaultCornerRadius = 20;

	public static readonly IReadOnlyList<ShapePreset> All = Enum.GetValues<ShapePreset> ();

	public static string GetName (ShapePreset preset) => preset switch {
		ShapePreset.Rectangle => Translations.GetString ("Rectangle"),
		ShapePreset.RoundedRectangle => Translations.GetString ("Rounded Rectangle"),
		ShapePreset.Ellipse => Translations.GetString ("Ellipse"),
		ShapePreset.Diamond => Translations.GetString ("Diamond"),
		ShapePreset.Trapezoid => Translations.GetString ("Trapezoid"),
		ShapePreset.Parallelogram => Translations.GetString ("Parallelogram"),
		ShapePreset.Triangle => Translations.GetString ("Triangle"),
		ShapePreset.RightTriangle => Translations.GetString ("Right Triangle"),
		ShapePreset.Pentagon => Translations.GetString ("Pentagon"),
		ShapePreset.Hexagon => Translations.GetString ("Hexagon"),
		ShapePreset.Heptagon => Translations.GetString ("Heptagon"),
		ShapePreset.Octagon => Translations.GetString ("Octagon"),
		ShapePreset.ThreePointStar => Translations.GetString ("Three-Point Star"),
		ShapePreset.FourPointStar => Translations.GetString ("Four-Point Star"),
		ShapePreset.FivePointStar => Translations.GetString ("Five-Point Star"),
		ShapePreset.SixPointStar => Translations.GetString ("Six-Point Star"),
		ShapePreset.Arrow => Translations.GetString ("Arrow"),
		ShapePreset.NotchedArrow => Translations.GetString ("Notched Arrow"),
		ShapePreset.PentagonArrow => Translations.GetString ("Pentagon Arrow"),
		ShapePreset.ChevronArrow => Translations.GetString ("Chevron Arrow"),
		ShapePreset.RectangularCallout => Translations.GetString ("Rectangular Callout"),
		ShapePreset.RoundedRectangleCallout => Translations.GetString ("Rounded Rectangle Callout"),
		ShapePreset.EllipseCallout => Translations.GetString ("Ellipse Callout"),
		ShapePreset.CloudCallout => Translations.GetString ("Cloud Callout"),
		ShapePreset.LightningBolt => Translations.GetString ("Lightning Bolt"),
		ShapePreset.CheckMark => Translations.GetString ("Check Mark"),
		ShapePreset.Multiply => Translations.GetString ("Multiply"),
		ShapePreset.Gear => Translations.GetString ("Gear"),
		ShapePreset.Heart => Translations.GetString ("Heart"),
		_ => throw new ArgumentOutOfRangeException (nameof (preset)),
	};

	/// <summary>The dropdown's group headings, with the first preset of each group.</summary>
	public static IReadOnlyList<(ShapePreset First, string Name)> Groups => [
		(ShapePreset.Rectangle, Translations.GetString ("Basic")),
		(ShapePreset.Pentagon, Translations.GetString ("Polygons and Stars")),
		(ShapePreset.Arrow, Translations.GetString ("Arrows")),
		(ShapePreset.RectangularCallout, Translations.GetString ("Callouts")),
		(ShapePreset.LightningBolt, Translations.GetString ("Symbols")),
	];

	/// <summary>Whether the toolbar offers a corner size for the preset.</summary>
	public static bool HasCornerRadius (ShapePreset preset)
		=> preset is ShapePreset.RoundedRectangle or ShapePreset.RoundedRectangleCallout;

	/// <summary>
	/// The closed contours of a preset fitted to a <paramref name="width"/> by <paramref name="height"/>
	/// box whose top-left corner is the origin. Fill them with the even-odd rule, so the gear gets its hole.
	/// </summary>
	/// <param name="cornerRadius">The corner size of the rounded presets, in the same units as the box.</param>
	public static IReadOnlyList<PointD[]> GetContours (ShapePreset preset, double width, double height, double cornerRadius = DefaultCornerRadius)
	{
		double w = Math.Max (width, 0);
		double h = Math.Max (height, 0);

		return preset switch {
			ShapePreset.Rectangle => [Scale ([(0, 0), (1, 0), (1, 1), (0, 1)], w, h)],
			ShapePreset.RoundedRectangle => [RoundedRectangle (0, 0, w, h, cornerRadius, [])],
			ShapePreset.Ellipse => [Ellipse (w / 2, h / 2, w / 2, h / 2)],
			ShapePreset.Diamond => [Scale ([(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)], w, h)],
			ShapePreset.Trapezoid => [Scale ([(0.25, 0), (0.75, 0), (1, 1), (0, 1)], w, h)],
			ShapePreset.Parallelogram => [Scale ([(0.25, 0), (1, 0), (0.75, 1), (0, 1)], w, h)],
			ShapePreset.Triangle => [Scale ([(0.5, 0), (1, 1), (0, 1)], w, h)],
			ShapePreset.RightTriangle => [Scale ([(0, 0), (1, 1), (0, 1)], w, h)],

			ShapePreset.Pentagon => [Fit (Star (5, 1), w, h)],
			ShapePreset.Hexagon => [Fit (Star (6, 1), w, h)],
			ShapePreset.Heptagon => [Fit (Star (7, 1), w, h)],
			ShapePreset.Octagon => [Fit (Star (8, 1), w, h)],
			ShapePreset.ThreePointStar => [Fit (Star (3, 0.22), w, h)],
			ShapePreset.FourPointStar => [Fit (Star (4, 0.3), w, h)],
			ShapePreset.FivePointStar => [Fit (Star (5, 0.4), w, h)],
			ShapePreset.SixPointStar => [Fit (Star (6, 0.58), w, h)],

			ShapePreset.Arrow => [Scale ([(0, 0.25), (0.6, 0.25), (0.6, 0), (1, 0.5), (0.6, 1), (0.6, 0.75), (0, 0.75)], w, h)],
			ShapePreset.NotchedArrow => [Scale ([(0, 0.25), (0.6, 0.25), (0.6, 0), (1, 0.5), (0.6, 1), (0.6, 0.75), (0, 0.75), (0.15, 0.5)], w, h)],
			ShapePreset.PentagonArrow => [Scale ([(0, 0), (0.7, 0), (1, 0.5), (0.7, 1), (0, 1)], w, h)],
			ShapePreset.ChevronArrow => [Scale ([(0, 0), (0.65, 0), (1, 0.5), (0.65, 1), (0, 1), (0.35, 0.5)], w, h)],

			ShapePreset.RectangularCallout => [Scale ([(0, 0), (1, 0), (1, 0.75), (0.42, 0.75), (0.15, 1), (0.22, 0.75), (0, 0.75)], w, h)],
			ShapePreset.RoundedRectangleCallout => [RoundedRectangle (0, 0, w, h * 0.75, Math.Min (cornerRadius, w * 0.2), [new (w * 0.42, h * 0.75), new (w * 0.15, h), new (w * 0.22, h * 0.75)])],
			ShapePreset.EllipseCallout => [EllipseCallout (w, h)],
			ShapePreset.CloudCallout => CloudCallout (w, h),

			ShapePreset.LightningBolt => [Fit (Scale ([(0.4, 0), (0.78, 0), (0.56, 0.36), (0.82, 0.36), (0.22, 1), (0.42, 0.52), (0.18, 0.52)], 1, 1), w, h)],
			ShapePreset.CheckMark => [Scale ([(0, 0.58), (0.14, 0.44), (0.37, 0.66), (0.86, 0), (1, 0.13), (0.38, 1)], w, h)],
			ShapePreset.Multiply => [Scale (MultiplyOutline (0.2), w, h)],
			ShapePreset.Gear => Gear (w, h),
			ShapePreset.Heart => [Fit (Heart (), w, h)],
			_ => throw new ArgumentOutOfRangeException (nameof (preset)),
		};
	}

	private static PointD[] Scale (ReadOnlySpan<(double X, double Y)> unit, double w, double h)
	{
		PointD[] result = new PointD[unit.Length];
		for (int i = 0; i < unit.Length; i++)
			result[i] = new (unit[i].X * w, unit[i].Y * h);
		return result;
	}

	/// <summary>Stretches points so that their bounding box fills the w by h box.</summary>
	private static PointD[] Fit (IReadOnlyList<PointD> points, double w, double h)
	{
		if (points.Count == 0)
			return [];

		double minX = points.Min (p => p.X), maxX = points.Max (p => p.X);
		double minY = points.Min (p => p.Y), maxY = points.Max (p => p.Y);
		double sx = maxX > minX ? w / (maxX - minX) : 0;
		double sy = maxY > minY ? h / (maxY - minY) : 0;
		return points.Select (p => new PointD ((p.X - minX) * sx, (p.Y - minY) * sy)).ToArray ();
	}

	/// <summary>
	/// A regular polygon (<paramref name="innerRatio"/> 1) or star with <paramref name="points"/> tips, the first tip at the top.
	/// Even polygons are turned so that they sit on a flat side, as is usual for hexagons and octagons.
	/// </summary>
	private static List<PointD> Star (int points, double innerRatio)
	{
		bool polygon = innerRatio >= 1;
		int count = polygon ? points : points * 2;
		double start = -Math.PI / 2 + (polygon && points % 2 == 0 ? Math.PI / points : 0);
		List<PointD> result = new (count);
		for (int i = 0; i < count; i++) {
			double r = (polygon || i % 2 == 0) ? 1 : innerRatio;
			double a = start + i * 2 * Math.PI / count;
			result.Add (new (r * Math.Cos (a), r * Math.Sin (a)));
		}
		return result;
	}

	/// <summary>Number of segments for a curve of the given length: smooth when large, cheap when small.</summary>
	private static int Segments (double length)
		=> Math.Clamp ((int) Math.Ceiling (length / 2), 8, 720);

	private static PointD[] Ellipse (double cx, double cy, double rx, double ry)
	{
		int n = Segments (Math.PI * (rx + ry));
		PointD[] result = new PointD[n];
		for (int i = 0; i < n; i++) {
			double a = i * 2 * Math.PI / n;
			result[i] = new (cx + rx * Math.Cos (a), cy + ry * Math.Sin (a));
		}
		return result;
	}

	/// <summary>
	/// A clockwise rounded rectangle starting at the top-left. <paramref name="bottomInsert"/> is spliced
	/// into the bottom edge (going right to left), for a callout's tail.
	/// </summary>
	private static PointD[] RoundedRectangle (double x0, double y0, double x1, double y1, double radius, ReadOnlySpan<PointD> bottomInsert)
	{
		double r = Math.Clamp (radius, 0, Math.Min ((x1 - x0) / 2, (y1 - y0) / 2));
		List<PointD> result = [];

		void Corner (double cx, double cy, double startAngle)
		{
			int n = Segments (Math.PI * r / 2);
			for (int i = 0; i <= n; i++) {
				double a = startAngle + i * (Math.PI / 2) / n;
				result.Add (new (cx + r * Math.Cos (a), cy + r * Math.Sin (a)));
			}
		}

		if (r <= 0) {
			result.Add (new (x0, y0));
			result.Add (new (x1, y0));
			result.Add (new (x1, y1));
			result.AddRange (bottomInsert);
			result.Add (new (x0, y1));
			return [.. result];
		}

		Corner (x0 + r, y0 + r, Math.PI);           // top-left
		Corner (x1 - r, y0 + r, -Math.PI / 2);      // top-right
		Corner (x1 - r, y1 - r, 0);                 // bottom-right
		result.AddRange (bottomInsert);
		Corner (x0 + r, y1 - r, Math.PI / 2);       // bottom-left
		return [.. result];
	}

	private static PointD[] EllipseCallout (double w, double h)
	{
		// An ellipse over the top 80% with a tail to the bottom-left, cut in between 105 and 125 degrees.
		double cx = w / 2, cy = h * 0.4, rx = w / 2, ry = h * 0.4;
		PointD[] ellipse = Ellipse (cx, cy, rx, ry);
		List<PointD> result = [];
		bool tailAdded = false;
		double from = 105 * Math.PI / 180, to = 125 * Math.PI / 180;
		for (int i = 0; i < ellipse.Length; i++) {
			double a = i * 2 * Math.PI / ellipse.Length;
			if (a < from || a > to) {
				result.Add (ellipse[i]);
				continue;
			}
			if (!tailAdded) {
				result.Add (new (cx + rx * Math.Cos (from), cy + ry * Math.Sin (from)));
				result.Add (new (w * 0.12, h));
				result.Add (new (cx + rx * Math.Cos (to), cy + ry * Math.Sin (to)));
				tailAdded = true;
			}
		}
		return [.. result];
	}

	private static PointD[][] CloudCallout (double w, double h)
	{
		// Scallops bulging out from an ellipse over the top three quarters, then two small bubbles trailing to the bottom-left.
		double cx = w / 2, cy = h * 0.375;
		double rx = w * 0.42, ry = h * 0.3;
		const int BUMPS = 9;
		List<PointD> cloud = [];
		for (int i = 0; i < BUMPS; i++) {
			double a0 = (i + 0.3) * 2 * Math.PI / BUMPS;
			double a1 = (i + 1.3) * 2 * Math.PI / BUMPS;
			PointD p0 = new (cx + rx * Math.Cos (a0), cy + ry * Math.Sin (a0));
			PointD p1 = new (cx + rx * Math.Cos (a1), cy + ry * Math.Sin (a1));
			double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
			double chord = Math.Sqrt (dx * dx + dy * dy);
			if (chord <= 0)
				continue;
			// Outward normal of the chord (clockwise outline in screen space).
			PointD normal = new (dy / chord, -dx / chord);
			double bulge = chord * 0.45;
			int n = Segments (chord * 1.5);
			for (int k = 0; k < n; k++) {
				double t = (double) k / n;
				double b = bulge * Math.Sin (Math.PI * t);
				cloud.Add (new (p0.X + dx * t + normal.X * b, p0.Y + dy * t + normal.Y * b));
			}
		}

		return [
			Fit (cloud, w, h * 0.75),
			Ellipse (w * 0.22, h * 0.83, w * 0.06, h * 0.05),
			Ellipse (w * 0.1, h * 0.95, w * 0.04, h * 0.05),
		];
	}

	private static (double, double)[] MultiplyOutline (double t)
		=> [
			(t, 0), (0.5, 0.5 - t), (1 - t, 0), (1, t), (0.5 + t, 0.5), (1, 1 - t),
			(1 - t, 1), (0.5, 0.5 + t), (t, 1), (0, 1 - t), (0.5 - t, 0.5), (0, t),
		];

	private static PointD[][] Gear (double w, double h)
	{
		const int TEETH = 8;
		const double OUTER = 1, ROOT = 0.76, HOLE = 0.3;
		double period = 2 * Math.PI / TEETH;
		List<PointD> outline = [];
		for (int i = 0; i < TEETH; i++) {
			double a = i * period - Math.PI / 2;
			// Root arc, then the tooth's flanks and flat top.
			int n = 4;
			for (int k = 0; k <= n; k++) {
				double ra = a - period * 0.5 + period * 0.2 * k / n;
				outline.Add (new (ROOT * Math.Cos (ra), ROOT * Math.Sin (ra)));
			}
			outline.Add (new (OUTER * Math.Cos (a - period * 0.18), OUTER * Math.Sin (a - period * 0.18)));
			outline.Add (new (OUTER * Math.Cos (a + period * 0.18), OUTER * Math.Sin (a + period * 0.18)));
			for (int k = 0; k <= n; k++) {
				double ra = a + period * 0.3 + period * 0.2 * k / n;
				outline.Add (new (ROOT * Math.Cos (ra), ROOT * Math.Sin (ra)));
			}
		}

		// Fit the teeth to the box, and the hole with the same mapping.
		double minX = outline.Min (p => p.X), maxX = outline.Max (p => p.X);
		double minY = outline.Min (p => p.Y), maxY = outline.Max (p => p.Y);
		PointD Map (PointD p) => new ((p.X - minX) / (maxX - minX) * w, (p.Y - minY) / (maxY - minY) * h);
		PointD center = Map (new (0, 0));
		PointD[] hole = Ellipse (center.X, center.Y, HOLE * w / (maxX - minX), HOLE * h / (maxY - minY));
		return [outline.Select (Map).ToArray (), hole];
	}

	private static List<PointD> Heart ()
	{
		// The classic parametric heart, tip down.
		const int N = 120;
		List<PointD> result = new (N);
		for (int i = 0; i < N; i++) {
			double t = i * 2 * Math.PI / N;
			double s = Math.Sin (t);
			double x = 16 * s * s * s;
			double y = -(13 * Math.Cos (t) - 5 * Math.Cos (2 * t) - 2 * Math.Cos (3 * t) - Math.Cos (4 * t));
			result.Add (new (x, y));
		}
		return result;
	}
}
