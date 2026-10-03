using System.Collections.Generic;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Effects.Tests;

[TestFixture]
[Parallelizable (ParallelScope.Children)]
internal sealed class AdjustmentsTest
{
	[Test]
	public void AutoLevel ()
	{
		AutoLevelEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "autolevel1.png");
	}

	[Test]
	public void BlackAndWhite ()
	{
		BlackAndWhiteEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "blackandwhite1.png");
	}

	[Test]
	public void BrightnessContrastDefault ()
	{
		BrightnessContrastEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "brightnesscontrast1.png");
	}

	[Test]
	public void BrightnessContrast ()
	{
		BrightnessContrastEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Brightness = 80;
		effect.Data.Contrast = 20;
		Utilities.TestEffect (effect, "brightnesscontrast2.png");
	}

	[Test]
	public void Curves ()
	{
		CurvesEffect effect = new (Utilities.CreateMockServices ());
		SortedList<int, int> points = new () {
			{ 0, 0 },
			{ 75, 110 },
			{ 225, 175 },
			{ 255, 255 }
		};

		effect.Data.ControlPoints = [points];
		effect.Data.Mode = ColorTransferMode.Luminosity;

		Utilities.TestEffect (effect, "curves1.png");
	}

	[Test]
	public void HueSaturationDefault ()
	{
		HueSaturationEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "huesaturation1.png");
	}

	[Test]
	public void HueSaturation ()
	{
		HueSaturationEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Hue = 12;
		effect.Data.Saturation = 50;
		effect.Data.Lightness = 50;
		Utilities.TestEffect (effect, "huesaturation2.png");
	}

	[Test]
	public void InvertColors ()
	{
		InvertColorsEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "invertcolors1.png");
	}

	[Test]
	public void Level ()
	{
		LevelsEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Levels = new UnaryPixelOps.Level (
			ColorBgra.Black, ColorBgra.White,
			[0.7f, 0.8f, 0.9f],
			ColorBgra.Red, ColorBgra.Green);

		Utilities.TestEffect (effect, "level1.png");
	}

	[Test]
	public void Posterize ()
	{
		PosterizeEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Red = 6;
		effect.Data.Green = 5;
		effect.Data.Blue = 4;
		Utilities.TestEffect (effect, "posterize1.png");
	}

	[Test]
	public void Sepia1 ()
	{
		SepiaEffect effect = new (Utilities.CreateMockServices ());
		Utilities.TestEffect (effect, "sepia1.png");
	}

	[Test]
	public void Sepia2 ()
	{
		SepiaEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.Strength = 50;
		Utilities.TestEffect (effect, "sepia2.png");
	}

	[Test]
	public void InvertAlpha ()
	{
		ColorBgra opaque = ColorBgra.FromBgra (10, 20, 30, 255);
		Assert.That (InvertAlphaEffect.InvertAlpha (opaque).A, Is.EqualTo (0));
		Assert.That (InvertAlphaEffect.InvertAlpha (ColorBgra.Zero), Is.EqualTo (ColorBgra.Black));

		// Half-transparent pixels keep their straight color and invert their alpha.
		ColorBgra half = ColorBgra.FromBgra (200, 100, 0, 255).NewAlpha (128);
		ColorBgra inverted = InvertAlphaEffect.InvertAlpha (half);
		Assert.That (inverted.A, Is.EqualTo (127));
		Assert.That (inverted.ToStraightAlpha ().B, Is.InRange (198, 202));
	}

	[Test]
	public void ExposureTable ()
	{
		byte[] same = ChannelTable.Create (1);
		for (int i = 0; i < 256; i++)
			Assert.That (same[i], Is.EqualTo (i));

		byte[] brighter = ChannelTable.Create (2);
		Assert.That (brighter[0], Is.EqualTo (0));
		Assert.That (brighter[100], Is.GreaterThan (100));
		Assert.That (brighter[255], Is.EqualTo (255));
	}

	[Test]
	public void TemperatureTintGains ()
	{
		Assert.That (TemperatureTintEffect.GetGains (0, 0), Is.EqualTo ((1d, 1d, 1d)));
		var warm = TemperatureTintEffect.GetGains (50, 0);
		Assert.That (warm.R, Is.GreaterThan (1));
		Assert.That (warm.B, Is.LessThan (1));
		var green = TemperatureTintEffect.GetGains (0, 50);
		Assert.That (green.G, Is.GreaterThan (green.R));
	}

	[Test]
	public void HighlightsShadowsTable ()
	{
		Assert.That (HighlightsShadowsEffect.CreateShiftTable (0, 0), Is.All.EqualTo (0));

		int[] lift = HighlightsShadowsEffect.CreateShiftTable (100, 0);
		Assert.That (lift[0], Is.EqualTo (0));
		Assert.That (lift[255], Is.EqualTo (0));
		Assert.That (lift[85], Is.GreaterThan (lift[200]));
		Assert.That (lift[85], Is.InRange (60, 66));

		int[] dim = HighlightsShadowsEffect.CreateShiftTable (0, -100);
		Assert.That (dim[170], Is.LessThan (dim[60]));

		ColorBgra grey = ColorBgra.FromBgr (85, 85, 85);
		Assert.That (HighlightsShadowsEffect.Apply (grey, lift).R, Is.GreaterThan (140));
	}
}
