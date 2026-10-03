using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class BrushDabStrokeTests
{
	[Test]
	public void FirstPointStampsOnce ()
	{
		BrushDabStroke stroke = new (10, 0.75, 0.15, smoothing: false, antialias: true);
		Assert.That (stroke.AddPoint (new PointD (5, 5)), Is.EqualTo (new List<PointD> { new (5, 5) }));
		// The same position again adds nothing.
		Assert.That (stroke.AddPoint (new PointD (5, 5)), Is.Empty);
	}

	[Test]
	public void DabsAreSpacedAlongThePath ()
	{
		// Size 20 at 50% spacing: a dab every 10 px, carried across segments.
		BrushDabStroke stroke = new (20, 1, 0.5, smoothing: false, antialias: true);
		stroke.AddPoint (new PointD (0, 0));
		List<PointD> dabs = stroke.AddPoint (new PointD (25, 0));
		Assert.That (dabs.Select (p => p.X), Is.EqualTo (new[] { 10.0, 20.0 }));
		dabs = stroke.AddPoint (new PointD (32, 0));
		Assert.That (dabs.Select (p => p.X), Is.EqualTo (new[] { 30.0 }));
	}

	[Test]
	public void WideSpacingLeavesGaps ()
	{
		// Spacing 200%: dab centres two diameters apart, so the dabs do not touch.
		BrushDabStroke stroke = new (10, 1, 2, smoothing: false, antialias: true);
		stroke.AddPoint (new PointD (0, 0));
		List<PointD> dabs = stroke.AddPoint (new PointD (100, 0));
		Assert.That (dabs, Has.Count.EqualTo (5));
		Assert.That (dabs[1].X - dabs[0].X, Is.EqualTo (20).Within (1e-9));
	}

	[Test]
	public void SmoothingTrailsOnePointAndFinishFlushes ()
	{
		BrushDabStroke stroke = new (4, 1, 0.25, smoothing: true, antialias: true);
		stroke.AddPoint (new PointD (0, 0));
		Assert.That (stroke.AddPoint (new PointD (10, 0)), Is.Empty);
		List<PointD> first = stroke.AddPoint (new PointD (20, 10));
		Assert.That (first, Is.Not.Empty);
		Assert.That (first.Max (p => p.X), Is.LessThanOrEqualTo (10 + 1e-9));
		List<PointD> rest = stroke.Finish ();
		Assert.That (rest, Is.Not.Empty);
		Assert.That (rest.Last ().X, Is.GreaterThan (18));
		Assert.That (stroke.Finish (), Is.Empty);
	}

	[Test]
	public void CatmullRomPassesThroughItsEndPoints ()
	{
		PointD p0 = new (0, 0), p1 = new (10, 0), p2 = new (20, 10), p3 = new (30, 10);
		PointD start = BrushDabStroke.CatmullRom (p0, p1, p2, p3, 0);
		PointD end = BrushDabStroke.CatmullRom (p0, p1, p2, p3, 1);
		Assert.That (start.X, Is.EqualTo (10).Within (1e-9));
		Assert.That (end.Y, Is.EqualTo (10).Within (1e-9));
		// Repeated points must not divide by zero.
		PointD mid = BrushDabStroke.CatmullRom (p1, p1, p2, p2, 0.5);
		Assert.That (double.IsFinite (mid.X) && double.IsFinite (mid.Y));
	}

	[Test]
	public void HardnessSoftensTheEdge ()
	{
		BrushDabStroke hard = new (20, 1, 0.15, false, antialias: true);
		BrushDabStroke soft = new (20, 0, 0.15, false, antialias: true);
		Assert.That (hard.Coverage (0), Is.EqualTo (1));
		Assert.That (hard.Coverage (8), Is.EqualTo (1));
		Assert.That (hard.Coverage (11), Is.EqualTo (0));
		Assert.That (soft.Coverage (5), Is.GreaterThan (0).And.LessThan (1));
		Assert.That (soft.Coverage (8), Is.LessThan (soft.Coverage (5)));
		Assert.That (soft.Coverage (10), Is.EqualTo (0));
	}

	[Test]
	public void AliasedDabIgnoresHardness ()
	{
		BrushDabStroke stroke = new (20, 0, 0.15, false, antialias: false);
		Assert.That (stroke.Coverage (9.9), Is.EqualTo (1));
		Assert.That (stroke.Coverage (10), Is.EqualTo (0));
	}

	private static ColorBgra[] StampOne (BrushDabStroke stroke, PointD at, int size = 9)
	{
		ColorBgra[] mask = new ColorBgra[size * size];
		stroke.Stamp (mask, size, size, at);
		return mask;
	}

	[Test]
	public void OnePixelAliasedDabCoversOnePixel ()
	{
		ColorBgra[] mask = StampOne (new BrushDabStroke (1, 1, 0.15, false, antialias: false), new PointD (4.2, 4.9));
		Assert.That (mask.Count (c => c.A > 0), Is.EqualTo (1));
		Assert.That (mask[4 * 9 + 4].A, Is.EqualTo (255));
	}

	[Test]
	public void TwoPixelAliasedDabIsASquare ()
	{
		ColorBgra[] mask = StampOne (new BrushDabStroke (2, 1, 0.15, false, antialias: false), new PointD (4.2, 4.1));
		Assert.That (mask.Count (c => c.A > 0), Is.EqualTo (4));
	}

	[Test]
	public void SquareTipFillsTheCorners ()
	{
		ColorBgra[] circle = StampOne (new BrushDabStroke (5, 1, 0.15, false, false, BrushTip.Circle), new PointD (4.5, 4.5));
		ColorBgra[] square = StampOne (new BrushDabStroke (5, 1, 0.15, false, false, BrushTip.Square), new PointD (4.5, 4.5));
		Assert.That (square.Count (c => c.A > 0), Is.EqualTo (25));
		Assert.That (circle.Count (c => c.A > 0), Is.LessThan (25));
	}

	[Test]
	public void OverlappingDabsKeepTheHighestCoverage ()
	{
		BrushDabStroke stroke = new (6, 0, 0.15, false, antialias: true);
		ColorBgra[] mask = new ColorBgra[81];
		stroke.Stamp (mask, 9, 9, new PointD (4.5, 4.5));
		byte centre = mask[4 * 9 + 4].A;
		stroke.Stamp (mask, 9, 9, new PointD (4.5, 4.5));
		Assert.That (mask[4 * 9 + 4].A, Is.EqualTo (centre));
		Assert.That (mask.All (c => c.R == 0 && c.G == 0 && c.B == 0));
	}

	[Test]
	public void StampOutsideTheMaskIsEmpty ()
	{
		BrushDabStroke stroke = new (4, 1, 0.15, false, antialias: true);
		ColorBgra[] mask = new ColorBgra[81];
		Assert.That (stroke.Stamp (mask, 9, 9, new PointD (-20, -20)).IsEmpty);
	}

	[TestCase (0, 0)]
	[TestCase (-5, 0)]
	[TestCase (150, 100)]
	[TestCase (75, 50)]
	public void ToolBarSliderLinear (double x, double expected)
		=> Assert.That (ToolBarSliderMath.ToValue (x / 150, 0, 100, 1), Is.EqualTo (expected));

	[Test]
	public void ToolBarSliderCurveRoundTrips ()
	{
		// Spacing: 1 to 500 with the low values given most of the bar.
		double fraction = ToolBarSliderMath.ToFraction (15, 1, 500, 2);
		Assert.That (fraction, Is.GreaterThan (0.15).And.LessThan (0.2));
		Assert.That (ToolBarSliderMath.ToValue (fraction, 1, 500, 1, 2), Is.EqualTo (15));
		Assert.That (ToolBarSliderMath.ToValue (0, 1, 500, 1, 2), Is.EqualTo (1));
		Assert.That (ToolBarSliderMath.ToValue (1, 1, 500, 1, 2), Is.EqualTo (500));
	}
}
