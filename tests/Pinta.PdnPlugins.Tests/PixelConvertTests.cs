using System;
using NUnit.Framework;
using PaintDotNet;

namespace Pinta.PdnPlugins.Tests;

[TestFixture]
internal sealed class PixelConvertTests
{
	private static uint Pack (uint a, uint r, uint g, uint b) => (a << 24) | (r << 16) | (g << 8) | b;

	[Test]
	public void OpaqueAndTransparentPixels ()
	{
		uint[] premul = [Pack (255, 10, 20, 30), Pack (0, 0, 0, 0)];
		uint[] straight = new uint[2];
		PixelConvert.ToStraight (premul, straight);
		Assert.That (straight, Is.EqualTo (premul));

		uint[] back = new uint[2];
		PixelConvert.ToPremultiplied ([Pack (255, 1, 2, 3), Pack (0, 200, 100, 50)], back);
		Assert.That (back, Is.EqualTo (new[] { Pack (255, 1, 2, 3), 0u }));
	}

	[Test]
	public void HalfTransparentValues ()
	{
		uint[] straight = new uint[1];
		PixelConvert.ToStraight ([Pack (128, 64, 128, 0)], straight);
		Assert.That (straight[0], Is.EqualTo (Pack (128, 128, 255, 0)));

		uint[] premul = new uint[1];
		PixelConvert.ToPremultiplied ([Pack (128, 255, 128, 0)], premul);
		Assert.That (premul[0], Is.EqualTo (Pack (128, 128, 64, 0)));
	}

	/// <summary>Cairo pixels survive the trip through a plugin's straight-alpha surface unchanged.</summary>
	[Test]
	public void PremultipliedRoundTripIsLossless ()
	{
		for (uint a = 1; a < 255; a++) {
			for (uint c = 0; c <= a; c++) {
				uint p = Pack (a, c, a - c, c / 2);
				uint[] straight = new uint[1], back = new uint[1];
				PixelConvert.ToStraight ([p], straight);
				PixelConvert.ToPremultiplied (straight, back);
				Assert.That (back[0], Is.EqualTo (p), $"a={a} c={c}");
			}
		}
	}

	[Test]
	public void StraightRoundTripErrorIsBounded ()
	{
		foreach (uint a in new uint[] { 1, 2, 17, 64, 128, 200, 254 }) {
			for (uint c = 0; c < 256; c++) {
				uint[] premul = new uint[1], back = new uint[1];
				PixelConvert.ToPremultiplied ([Pack (a, c, c, c)], premul);
				PixelConvert.ToStraight (premul, back);
				int error = Math.Abs ((int) (back[0] & 0xff) - (int) c);
				Assert.That (error, Is.LessThanOrEqualTo (255 / (2 * (int) a) + 1), $"a={a} c={c}");
				Assert.That (back[0] >> 24, Is.EqualTo (a));
			}
		}
	}

	[Test]
	public void ShimColorBgraAgreesWithHostConversion ()
	{
		for (int a = 0; a < 256; a += 5) {
			ColorBgra c = ColorBgra.FromBgra (200, 100, 50, (byte) a);
			uint[] premul = new uint[1];
			PixelConvert.ToPremultiplied ([c.Bgra], premul);
			Assert.That (c.ConvertToPremultipliedAlpha ().Bgra, Is.EqualTo (premul[0]));

			uint[] straight = new uint[1];
			PixelConvert.ToStraight (premul, straight);
			Assert.That (ColorBgra.FromUInt32 (premul[0]).ConvertFromPremultipliedAlpha ().Bgra, Is.EqualTo (straight[0]));
		}
	}

	[Test]
	public void ColorBgraLayoutIsBgra ()
	{
		ColorBgra c = ColorBgra.FromBgra (1, 2, 3, 4);
		Assert.That (c.Bgra, Is.EqualTo (0x04030201u));
		Assert.That (ColorBgra.ToOpaqueInt32 (ColorBgra.FromBgr (0x33, 0x22, 0x11)), Is.EqualTo (0x112233));
		Assert.That (ColorBgra.FromOpaqueInt32 (0x112233), Is.EqualTo (ColorBgra.FromBgra (0x33, 0x22, 0x11, 255)));
	}
}
