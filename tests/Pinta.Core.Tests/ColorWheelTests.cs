using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ColorWheelTests
{
	private const double R = 100;

	[TestCase (100, 0, 0, 1)] // 3 o'clock is red
	[TestCase (0, 50, 90, 0.5)] // hue runs clockwise on screen (y down)
	[TestCase (-100, 0, 180, 1)]
	[TestCase (0, -100, 270, 1)]
	[TestCase (300, 0, 0, 1)] // outside the rim clamps to full saturation
	public void PointToHueAndSat (double x, double y, double hue, double sat)
	{
		HsvColor hsv = ColorWheel.OffsetToHsv (new (x, y), R, new HsvColor (0, 0, 0.5));
		Assert.That (hsv.Hue, Is.EqualTo (hue).Within (1e-9));
		Assert.That (hsv.Sat, Is.EqualTo (sat).Within (1e-9));
		Assert.That (hsv.Val, Is.EqualTo (0.5));
	}

	[Test]
	public void RoundTrip ()
	{
		HsvColor hsv = new (123, 0.4, 0.7);
		HsvColor back = ColorWheel.OffsetToHsv (ColorWheel.HsvToOffset (hsv, R), R, hsv);
		Assert.That (back.Hue, Is.EqualTo (123).Within (1e-9));
		Assert.That (back.Sat, Is.EqualTo (0.4).Within (1e-9));
	}

	[Test]
	public void BlackPicksAtFullBrightness ()
		=> Assert.That (ColorWheel.OffsetToHsv (new (R, 0), R, HsvColor.Black).Val, Is.EqualTo (1));

	[Test]
	public void CtrlKeepsSaturation ()
	{
		HsvColor hsv = ColorWheel.OffsetToHsv (new (0, R), R, new HsvColor (10, 0.3, 1), keepSat: true);
		Assert.That (hsv.Hue, Is.EqualTo (90).Within (1e-9));
		Assert.That (hsv.Sat, Is.EqualTo (0.3));
	}

	[Test]
	public void AltKeepsHueAndProjectsOntoSpoke ()
	{
		// Current spoke points down (hue 90); a pointer down-right projects to y only.
		HsvColor hsv = ColorWheel.OffsetToHsv (new (40, 60), R, new HsvColor (90, 1, 1), keepHue: true);
		Assert.That (hsv.Hue, Is.EqualTo (90));
		Assert.That (hsv.Sat, Is.EqualTo (0.6).Within (1e-9));

		// Behind the centre clamps to grey rather than flipping hue.
		Assert.That (ColorWheel.OffsetToHsv (new (0, -50), R, new HsvColor (90, 1, 1), keepHue: true).Sat, Is.EqualTo (0));
	}

	[Test]
	public void DefaultPaletteIsSixRowsOfSixteen ()
	{
		Cairo.Color[] colors = [.. PaletteHelper.EnumerateDefaultColors ()];
		Assert.That (colors, Has.Length.EqualTo (96));
		Assert.That (colors[0], Is.EqualTo (new Cairo.Color (0, 0, 0)));
		Assert.That (colors[16], Is.EqualTo (new Cairo.Color (1, 1, 1)));
		Assert.That (colors[64], Is.EqualTo (new Cairo.Color (0, 0, 0, 0.5)));
	}

	[TestCase (8, 15)]
	[TestCase (7, 0)]
	[TestCase (-7, 0)]
	[TestCase (-8, 345)]
	public void ShiftSnapsHue (double degrees, double expected)
	{
		double rad = degrees * System.Math.PI / 180;
		PointD p = new (System.Math.Cos (rad) * 50, System.Math.Sin (rad) * 50);
		Assert.That (ColorWheel.OffsetToHsv (p, R, HsvColor.White, snapHue: true).Hue, Is.EqualTo (expected).Within (1e-9));
	}
}
