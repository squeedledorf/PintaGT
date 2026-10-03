using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using NUnit.Framework;
using Pinta.Tools;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class ShapeToolsTests
{
	[OneTimeSetUp]
	public void Init ()
	{
		// Preset names go through Translations, which uses GLib.
		GLib.Module.Initialize ();
		Cairo.Module.Initialize ();
	}

	private static void AssertPoint (PointD actual, double x, double y)
	{
		Assert.That (actual.X, Is.EqualTo (x).Within (1e-6), "X");
		Assert.That (actual.Y, Is.EqualTo (y).Within (1e-6), "Y");
	}

	[Test]
	public void ThereAre29PresetsInFiveGroups ()
	{
		Assert.That (ShapePresets.All, Has.Count.EqualTo (29));
		Assert.That (ShapePresets.Groups, Has.Count.EqualTo (5));
		Assert.That (ShapePresets.All.Select (ShapePresets.GetName).Distinct ().Count (), Is.EqualTo (29));
	}

	[Test]
	public void EveryPresetFillsItsBox ([ValueSource (typeof (ShapePresets), nameof (ShapePresets.All))] ShapePreset preset)
	{
		const double W = 200, H = 120;
		IReadOnlyList<PointD[]> contours = ShapePresets.GetContours (preset, W, H);

		Assert.That (contours, Is.Not.Empty);
		PointD[] all = contours.SelectMany (c => c).ToArray ();
		Assert.That (contours.All (c => c.Length >= 3), "every contour is a polygon");
		Assert.That (all.All (p => double.IsFinite (p.X) && double.IsFinite (p.Y)));

		// Inside the box, and touching all four sides of it.
		const double TOLERANCE = 0.5;
		Assert.That (all.Min (p => p.X), Is.EqualTo (0).Within (TOLERANCE), "left");
		Assert.That (all.Max (p => p.X), Is.EqualTo (W).Within (TOLERANCE), "right");
		Assert.That (all.Min (p => p.Y), Is.EqualTo (0).Within (TOLERANCE), "top");
		Assert.That (all.Max (p => p.Y), Is.EqualTo (H).Within (TOLERANCE), "bottom");
	}

	[Test]
	public void EveryPresetCopesWithAnEmptyBox ([ValueSource (typeof (ShapePresets), nameof (ShapePresets.All))] ShapePreset preset)
	{
		// While a shape is being dragged out, its box can be zero wide or high.
		Assert.DoesNotThrow (() => ShapePresets.GetContours (preset, 0, 0));
		Assert.DoesNotThrow (() => ShapePresets.GetContours (preset, 50, 0));
		Assert.DoesNotThrow (() => ShapePresets.GetContours (preset, 0, 50));
	}

	[Test]
	public void RoundedRectangleCornersKeepTheirSizeAndStayInTheBox ()
	{
		PointD[] outline = ShapePresets.GetContours (ShapePreset.RoundedRectangle, 100, 50, cornerRadius: 10)[0];

		// No point near the sharp corner: the corner is cut by the radius.
		Assert.That (outline.Min (p => Math.Sqrt (p.X * p.X + p.Y * p.Y)), Is.GreaterThan (10 * (Math.Sqrt (2) - 1) - 0.01));

		// A huge radius is limited to half the shorter side.
		PointD[] capsule = ShapePresets.GetContours (ShapePreset.RoundedRectangle, 100, 50, cornerRadius: 1000)[0];
		Assert.That (capsule.All (p => p.X >= -1e-9 && p.X <= 100 + 1e-9 && p.Y >= -1e-9 && p.Y <= 50 + 1e-9));
	}

	[Test]
	public void StraightCurveIsTheNubPolyline ()
	{
		PointD[] nubs = [new (0, 0), new (10, 5), new (20, 0), new (30, 5)];
		IReadOnlyList<CubicSegment> segments = CurveGeometry.GetSegments (nubs, CurveType.Straight);

		Assert.That (segments, Has.Count.EqualTo (3));
		for (int i = 0; i < 3; i++) {
			Assert.That (segments[i].Start, Is.EqualTo (nubs[i]));
			Assert.That (segments[i].End, Is.EqualTo (nubs[i + 1]));
		}
	}

	[Test]
	public void SplinePassesThroughEveryNubSmoothly ()
	{
		PointD[] nubs = [new (0, 0), new (10, 10), new (20, 0), new (30, 10)];
		IReadOnlyList<CubicSegment> segments = CurveGeometry.GetSegments (nubs, CurveType.Spline);

		Assert.That (segments, Has.Count.EqualTo (3));
		Assert.That (segments[0].Start, Is.EqualTo (nubs[0]));
		Assert.That (segments[^1].End, Is.EqualTo (nubs[3]));

		// Where two segments meet, the tangents line up (no kink at the nub).
		for (int i = 0; i < 2; i++) {
			PointD into = new (segments[i].End.X - segments[i].Control2.X, segments[i].End.Y - segments[i].Control2.Y);
			PointD outOf = new (segments[i + 1].Control1.X - segments[i + 1].Start.X, segments[i + 1].Control1.Y - segments[i + 1].Start.Y);
			Assert.That (segments[i].End, Is.EqualTo (nubs[i + 1]));
			Assert.That (into.X * outOf.Y - into.Y * outOf.X, Is.EqualTo (0).Within (1e-9));
		}
	}

	[Test]
	public void BezierUsesTheMiddleNubsAsControls ()
	{
		PointD[] nubs = [new (0, 0), new (10, 10), new (20, 10), new (30, 0)];
		IReadOnlyList<CubicSegment> segments = CurveGeometry.GetSegments (nubs, CurveType.Bezier);

		Assert.That (segments, Is.EqualTo (new[] { new CubicSegment (nubs[0], nubs[1], nubs[2], nubs[3]) }));
	}

	[Test]
	public void EndDirectionFallsBackWhenControlsSitOnTheEnd ()
	{
		CubicSegment straight = new (new (0, 0), new (0, 0), new (10, 0), new (10, 0));
		AssertPoint (CurveGeometry.EndDirection (straight), 1, 0);
		AssertPoint (CurveGeometry.EndDirection (CurveGeometry.Reverse (straight)), -1, 0);
	}

	[Test]
	public void LineStartsStraightWithNubsAtTheThirds ()
	{
		LineShape line = LineShape.FromEnds (new (0, 0), new (30, 60), useSecondaryColor: false);

		AssertPoint (line.Points[1], 10, 20);
		AssertPoint (line.Points[2], 20, 40);
		AssertPoint (line.Pivot, 15, 30);
	}

	[Test]
	public void ResizingARotatedBoxNeverSkewsIt ()
	{
		BoxShape box = new ();
		box.Frame.Reset (new RectangleD (0, 0, 100, 50));
		box.Transform (box.Frame.ComputeRotation (new PointD (100, 25), new PointD (100, 60), snap: false));

		// Drag the bottom-right corner nub somewhere arbitrary.
		box.Transform (box.Frame.ComputeScale (4, new PointD (170, 140), keepAspect: false, fromCenter: false));

		PointD[] c = box.Outline;
		for (int i = 0; i < 4; i++) {
			PointD a = c[i], b = c[(i + 1) % 4], d = c[(i + 2) % 4];
			double dot = (b.X - a.X) * (d.X - b.X) + (b.Y - a.Y) * (d.Y - b.Y);
			Assert.That (dot, Is.EqualTo (0).Within (1e-6), "every corner is a right angle");
		}

		// The rectangle preset follows the box exactly.
		PointD[] rect = box.GetContours (ShapePreset.Rectangle, 0).Single ();
		for (int i = 0; i < 4; i++)
			AssertPoint (rect[i], c[i].X, c[i].Y);
	}

	[Test]
	public void BoxSizeIncludesTheTransformScale ()
	{
		BoxShape box = new ();
		box.Frame.Reset (new RectangleD (0, 0, 100, 50));
		box.Transform (box.Frame.ComputeScale (4, new PointD (200, 150), keepAspect: false, fromCenter: false));

		(double w, double h) = box.Size;
		Assert.That (w, Is.EqualTo (200).Within (1e-6));
		Assert.That (h, Is.EqualTo (150).Within (1e-6));
	}

	[Test]
	public void FlattenFollowsTheCurveFromStartToEnd ()
	{
		// A Bézier pulled up by its controls: the samples start and end on the nubs and bulge upwards in the middle.
		PointD[] nubs = [new (0, 0), new (0, -100), new (100, -100), new (100, 0)];
		PointD[] path = CurveGeometry.Flatten (CurveGeometry.GetSegments (nubs, CurveType.Bezier), steps: 8).ToArray ();

		Assert.That (path, Has.Length.EqualTo (9));
		AssertPoint (path[0], 0, 0);
		AssertPoint (path[^1], 100, 0);
		AssertPoint (path[4], 50, -75);
	}
}
