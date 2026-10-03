using System;
using System.Collections.Generic;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>Paint.NET's three Line/Curve types.</summary>
public enum CurveType
{
	Straight,
	Spline,
	Bezier,
}

/// <summary>The end caps offered for a Line/Curve's start and end.</summary>
public enum LineCapStyle
{
	Flat,
	Arrow,
	FilledArrow,
	Rounded,
}

/// <summary>One cubic Bézier segment.</summary>
public readonly record struct CubicSegment (PointD Start, PointD Control1, PointD Control2, PointD End);

public static class CurveGeometry
{
	/// <summary>
	/// The cubic segments of a curve through (or, for Bézier, controlled by) the given nubs:
	/// straight lines between them, a cubic spline through them, or one Bézier curve from the first to the last.
	/// </summary>
	public static IReadOnlyList<CubicSegment> GetSegments (IReadOnlyList<PointD> nubs, CurveType type)
	{
		List<CubicSegment> result = [];
		int n = nubs.Count;
		if (n < 2)
			return result;

		switch (type) {
			case CurveType.Bezier when n == 4:
				result.Add (new (nubs[0], nubs[1], nubs[2], nubs[3]));
				break;

			case CurveType.Spline:
			case CurveType.Bezier:
				// Catmull-Rom: each nub's tangent is half the vector between its neighbours.
				for (int i = 0; i < n - 1; i++) {
					PointD p0 = nubs[Math.Max (i - 1, 0)];
					PointD p1 = nubs[i];
					PointD p2 = nubs[i + 1];
					PointD p3 = nubs[Math.Min (i + 2, n - 1)];
					result.Add (new (
						p1,
						new (p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6),
						new (p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6),
						p2));
				}
				break;

			default:
				for (int i = 0; i < n - 1; i++)
					result.Add (new (nubs[i], nubs[i], nubs[i + 1], nubs[i + 1]));
				break;
		}

		return result;
	}

	/// <summary>
	/// The direction the curve leaves its end point, for drawing an end cap; the start cap uses the reversed segments.
	/// Falls back through the control points when they coincide with the end.
	/// </summary>
	public static PointD EndDirection (CubicSegment last)
	{
		foreach (PointD from in (ReadOnlySpan<PointD>) [last.Control2, last.Control1, last.Start]) {
			PointD d = new (last.End.X - from.X, last.End.Y - from.Y);
			double len = Math.Sqrt (d.X * d.X + d.Y * d.Y);
			if (len > 1e-9)
				return new (d.X / len, d.Y / len);
		}
		return new (1, 0);
	}

	/// <summary>The same segment traversed backwards.</summary>
	public static CubicSegment Reverse (CubicSegment s)
		=> new (s.End, s.Control2, s.Control1, s.Start);
}
