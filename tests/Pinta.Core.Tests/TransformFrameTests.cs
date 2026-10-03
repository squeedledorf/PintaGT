using System;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class TransformFrameTests
{
	[OneTimeSetUp]
	public void Init ()
	{
		Cairo.Module.Initialize ();
	}

	private static TransformFrame CreateFrame ()
	{
		TransformFrame frame = new ();
		frame.Reset (new RectangleD (10, 20, 100, 50));
		return frame;
	}

	private static void AssertPoint (PointD actual, double x, double y)
	{
		Assert.That (actual.X, Is.EqualTo (x).Within (1e-6), "X");
		Assert.That (actual.Y, Is.EqualTo (y).Within (1e-6), "Y");
	}

	[Test]
	public void ComposeAppliesFirstThenSecond ()
	{
		Matrix translate = CairoExtensions.CreateIdentityMatrix ();
		translate.Translate (10, 0);
		Matrix scale = CairoExtensions.CreateIdentityMatrix ();
		scale.Scale (2, 2);

		AssertPoint (TransformFrame.Compose (translate, scale).TransformPoint (new PointD (1, 1)), 22, 2);
	}

	[Test]
	public void NubsSitOnCornersAndEdges ()
	{
		TransformFrame frame = CreateFrame ();
		AssertPoint (frame.GetNub (0), 10, 20);
		AssertPoint (frame.GetNub (1), 60, 20);
		AssertPoint (frame.GetNub (4), 110, 70);
		AssertPoint (frame.GetNub (7), 10, 45);
		AssertPoint (frame.Pivot, 60, 45);
	}

	[Test]
	public void CornerNubScalesFromTheOppositeCorner ()
	{
		TransformFrame frame = CreateFrame ();
		Matrix m = frame.ComputeScale (4, new PointD (210, 70), keepAspect: false, fromCenter: false);
		AssertPoint (m.TransformPoint (new PointD (10, 20)), 10, 20); // anchor stays
		AssertPoint (m.TransformPoint (new PointD (110, 70)), 210, 70);
	}

	[Test]
	public void ShiftKeepsTheAspectRatio ()
	{
		TransformFrame frame = CreateFrame ();
		Matrix m = frame.ComputeScale (4, new PointD (210, 70), keepAspect: true, fromCenter: false);
		AssertPoint (m.TransformPoint (new PointD (110, 70)), 210, 120);
	}

	[Test]
	public void AltScalesAboutTheCentre ()
	{
		TransformFrame frame = CreateFrame ();
		Matrix m = frame.ComputeScale (3, new PointD (160, 45), keepAspect: false, fromCenter: true);
		AssertPoint (m.TransformPoint (new PointD (60, 45)), 60, 45);
		AssertPoint (m.TransformPoint (new PointD (10, 20)), -40, 20);
	}

	[Test]
	public void DraggingPastTheOppositeNubFlips ()
	{
		TransformFrame frame = CreateFrame ();
		Matrix m = frame.ComputeScale (3, new PointD (-90, 45), keepAspect: false, fromCenter: false);
		AssertPoint (m.TransformPoint (new PointD (110, 20)), -90, 20);
	}

	[Test]
	public void ScalingFollowsARotatedFrame ()
	{
		TransformFrame frame = CreateFrame ();
		frame.Matrix.InitMatrix (frame.ComputeRotation (new PointD (100, 45), new PointD (60, 85), snap: false));
		Assert.That (frame.AngleDegrees, Is.EqualTo (-90).Within (1e-6));

		PointD nub = frame.GetNub (3); // the right edge now points down
		AssertPoint (nub, 60, 95);
		Matrix m = frame.ComputeScale (3, new PointD (60, 145), keepAspect: false, fromCenter: false);
		AssertPoint (m.TransformPoint (nub), 60, 145);
	}

	[Test]
	public void ShiftSnapsRotationTo15Degrees ()
	{
		TransformFrame frame = CreateFrame ();
		// Sweep about 20 degrees clockwise around the pivot (60, 45).
		double a = 20 * Math.PI / 180;
		Matrix m = frame.ComputeRotation (new PointD (160, 45), new PointD (60 + 100 * Math.Cos (a), 45 + 100 * Math.Sin (a)), snap: true);
		frame.Matrix.InitMatrix (m);
		Assert.That (frame.AngleDegrees, Is.EqualTo (-15).Within (1e-6));
	}

	[TestCase (50, 40, TransformFrameZone.Move)] // inside
	[TestCase (115, 40, TransformFrameZone.Rotate)] // just right of the frame
	[TestCase (50, 10, TransformFrameZone.Rotate)] // just above
	[TestCase (300, 300, TransformFrameZone.Move)] // well outside
	public void HitTestFindsTheRotateCorridor (double x, double y, TransformFrameZone expected)
	{
		PointD[] outline = [new (10, 20), new (110, 20), new (110, 70), new (10, 70)];
		Assert.That (TransformFrame.HitTest (outline, new PointD (x, y), 15), Is.EqualTo (expected));
	}

	[TestCase (190, -170)]
	[TestCase (-180, 180)]
	[TestCase (360, 0)]
	[TestCase (45, 45)]
	public void NormalizesDegrees (double input, double expected)
		=> Assert.That (TransformFrame.NormalizeDegrees (input), Is.EqualTo (expected).Within (1e-9));
}
