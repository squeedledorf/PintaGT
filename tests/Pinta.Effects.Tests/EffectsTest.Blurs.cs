using System;
using System.Linq;
using NUnit.Framework;
using Pinta.Effects.Tests;

namespace Pinta.Effects;

partial class EffectsTest
{
	[Test]
	public void BokehBlur1 ()
	{
		BokehBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "bokehblur1.png");
	}

	[Test]
	public void BokehBlur2 ()
	{
		BokehBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Radius = 40;
		effect.Data.GammaBoost = 1.5;
		effect.Data.Quality = 1;
		Utilities.TestEffect (effect, "bokehblur2.png");
	}

	[Test]
	public void Fragment1 ()
	{
		FragmentEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "fragment1.png");
	}

	[Test]
	public void Fragment2 ()
	{
		FragmentEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Fragments = 25;
		effect.Data.Distance = 60;
		effect.Data.Rotation = new (90);
		Utilities.TestEffect (effect, "fragment2.png");
	}

	[Test]
	public void GaussianBlur1 ()
	{
		GaussianBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "gaussianblur1.png");
	}

	[Test]
	public void GaussianBlur2 ()
	{
		GaussianBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Radius = 100;
		Utilities.TestEffect (effect, "gaussianblur2.png");
	}

	[Test]
	public void GaussianBlur3 ()
	{
		GaussianBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Radius = 20;
		effect.Data.GammaBoost = 1;
		effect.Data.Quality = 2;
		Utilities.TestEffect (effect, "gaussianblur3.png");
	}

	[Test]
	public void GaussianQualityKeepsEveryTapAtTop ()
	{
		var row = GaussianBlurEffect.CreateGaussianBlurRow (40);
		Assert.That (GaussianBlurEffect.ApplyQuality (row, 40, GaussianBlurEffect.MaxQuality), Is.EqualTo (row));
		// Small radii keep every tap even at the lowest quality.
		var small = GaussianBlurEffect.CreateGaussianBlurRow (3);
		Assert.That (GaussianBlurEffect.ApplyQuality (small, 3, 1), Is.EqualTo (small));
	}

	[Test]
	public void GaussianQualityThinsTaps ()
	{
		var thinned = GaussianBlurEffect.ApplyQuality (GaussianBlurEffect.CreateGaussianBlurRow (40), 40, 1);
		Assert.That (thinned[40], Is.Not.Zero); // centre tap survives
		Assert.That (thinned[44], Is.Not.Zero);
		Assert.That (thinned[41], Is.Zero);
		Assert.That (thinned.Count (w => w != 0), Is.EqualTo (21));
	}

	[Test]
	public void MotionBlur1 ()
	{
		MotionBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "motionblur1.png");
	}

	[Test]
	public void MotionBlur2 ()
	{
		MotionBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Angle = new (50);
		effect.Data.Distance = 25;
		effect.Data.Centered = false;
		Utilities.TestEffect (effect, "motionblur2.png");
	}

	[Test]
	public void RadialBlur1 ()
	{
		RadialBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "radialblur1.png");
	}

	[Test]
	public void RadialBlur2 ()
	{
		RadialBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Angle = new (90);
		effect.Data.Offset = new (20, 20);
		effect.Data.Quality = 4;
		Utilities.TestEffect (effect, "radialblur2.png");
	}

	[Test]
	public void SketchBlur1 ()
	{
		SketchBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "sketchblur1.png");
	}

	[Test]
	public void SquareBlur1 ()
	{
		SquareBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "squareblur1.png");
	}

	[Test]
	public void SquareBlur2 ()
	{
		SquareBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Radius = 12.5;
		effect.Data.GammaBoost = -0.5;
		Utilities.TestEffect (effect, "squareblur2.png");
	}

	[Test]
	public void SurfaceBlur1 ()
	{
		SurfaceBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "surfaceblur1.png");
	}

	[Test]
	public void SurfaceBlurIgnoresValuesBeyondReach ()
	{
		int[] histogram = new int[256];
		histogram[100] = 3;
		histogram[110] = 1;
		histogram[200] = 50; // an edge: too far from 100 to count
		// Weights: 100 -> 3 * 1, 110 -> 1 * 0.5 (reach 20).
		Assert.That (SurfaceBlurEffect.SmoothChannel (100, histogram, 20), Is.EqualTo (101));
		Assert.That (SurfaceBlurEffect.SmoothChannel (150, histogram, 20), Is.EqualTo (150));
	}

	[Test]
	public void GammaBoostRoundTrips ()
	{
		foreach (double boost in new[] { -1, -0.5, 0, 1, 2 }) {
			GammaBoost gamma = new (boost);
			for (int v = 0; v < 256; ++v)
				Assert.That (gamma.Inverse (gamma[(byte) v]), Is.EqualTo (v), $"boost {boost}, value {v}");
		}
		Assert.That (new GammaBoost (0).IsIdentity, Is.True);
		// A positive boost pulls a 50/50 black-white mix towards white.
		GammaBoost high = new (1);
		Assert.That (high.Inverse ((high[0] + high[255]) / 2), Is.GreaterThan (128));
	}

	[Test]
	public void KernelRowsCoverTheExpectedArea ()
	{
		Assert.That (KernelRowsBlur.Square (2).Length, Is.EqualTo (5));
		var frac = KernelRowsBlur.Square (1.5);
		Assert.That (frac.Length, Is.EqualTo (5));
		Assert.That (frac[0].Weight, Is.EqualTo (0.5));
		Assert.That (KernelRowsBlur.Disc (10, 100).Length, Is.EqualTo (21));
		var limited = KernelRowsBlur.Disc (100, 17);
		Assert.That (limited.Length, Is.EqualTo (17));
		// The sampled strips still add up to the disc's height.
		Assert.That (limited.Sum (r => r.Weight), Is.EqualTo (200).Within (1e-9));
	}

	[Test]
	public void P2EstimatesTheMedian ()
	{
		P2QuantileEstimator median = new (0.5);
		P2QuantileEstimator low = new (0.1);
		Random random = new (1);
		foreach (int v in Enumerable.Range (0, 1001).OrderBy (_ => random.Next ())) {
			median.Add (v);
			low.Add (v);
		}
		Assert.That (median.Estimate, Is.EqualTo (500).Within (25));
		Assert.That (low.Estimate, Is.EqualTo (100).Within (25));

		P2QuantileEstimator few = new (0.5);
		few.Add (7);
		few.Add (1);
		few.Add (4);
		Assert.That (few.Estimate, Is.EqualTo (4));
	}

	[Test]
	public void Unfocus1 ()
	{
		UnfocusEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "unfocus1.png");
	}

	[Test]
	public void Unfocus2 ()
	{
		UnfocusEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Radius = 50;
		Utilities.TestEffect (effect, "unfocus2.png");
	}

	[Test]
	public void ZoomBlur1 ()
	{
		ZoomBlurEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "zoomblur1.png");
	}

	[Test]
	public void ZoomBlur2 ()
	{
		ZoomBlurEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Amount = 50;
		effect.Data.Offset = new (-1, -1);
		Utilities.TestEffect (effect, "zoomblur2.png");
	}
}
