using NUnit.Framework;
using Pinta.Core;
using Pinta.Effects.Tests;

namespace Pinta.Effects;

// Object > Drop Shadow, Photo > Straighten, Render > Turbulence
partial class EffectsTest
{
	[Test]
	public void DropShadow1 ()
	{
		DropShadowEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "dropshadow1.png", source_image_name: "alignobjectinput.png");
	}

	[Test]
	public void DropShadow2 ()
	{
		DropShadowEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.ShadowRadius = 15;
		effect.Data.Distance = 12;
		effect.Data.Opacity = 0.75;
		effect.Data.Color = new Cairo.Color (0.04, 0, 0.67);
		Utilities.TestEffect (effect, "dropshadow2.png", source_image_name: "alignobjectinput.png");
	}

	[Test]
	public void DropShadowBlurKeepsMassAndSpreads ()
	{
		const int size = 21;
		float[] alpha = new float[size * size];
		alpha[10 * size + 10] = 1;
		DropShadowEffect.BlurAlpha (alpha, size, size, 6);

		float total = 0;
		foreach (float a in alpha)
			total += a;
		Assert.That (total, Is.EqualTo (1).Within (1e-4));
		Assert.That (alpha[10 * size + 16], Is.GreaterThan (0)); // reaches the radius
		Assert.That (alpha[10 * size + 17], Is.EqualTo (0).Within (1e-6)); // and no further
		Assert.That (alpha[10 * size + 10], Is.GreaterThan (alpha[10 * size + 13]));
	}

	[Test]
	public void Straighten1 ()
	{
		StraightenEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Angle = 10;
		effect.Data.Sampling = StraightenEffect.StraightenSampling.Bilinear;
		Utilities.TestEffect (effect, "straighten1.png");
	}

	[TestCase (100, 100, 0, 1.0)]
	[TestCase (100, 100, 45, 1.4142135623730951)]
	[TestCase (100, 100, -45, 1.4142135623730951)]
	[TestCase (200, 100, 90, 2.0)]
	public void StraightenCoverScale (int width, int height, double degrees, double expected)
	{
		Assert.That (StraightenEffect.CoverScale (width, height, degrees), Is.EqualTo (expected).Within (1e-9));
	}

	[Test]
	public void Turbulence1 ()
	{
		TurbulenceEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "turbulence1.png");
	}

	[Test]
	public void Turbulence2 ()
	{
		TurbulenceEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Period = 40;
		effect.Data.BlendMode = TurbulenceEffect.TurbulenceBlendMode.Overwrite;
		Utilities.TestEffect (effect, "turbulence2.png");
	}
}
