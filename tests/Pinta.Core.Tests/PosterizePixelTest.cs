using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class PosterizePixelTest
{
	[Test]
	public void TwoLevelsSnapToBlackOrWhite ()
	{
		UnaryPixelOps.PosterizePixel op = new (2, 2, 2);
		ColorBgra result = op.Apply (ColorBgra.FromBgra (20, 200, 100, 255));

		Assert.That (result, Is.EqualTo (ColorBgra.FromBgra (0, 255, 0, 255)));
	}

	// 256 levels is how an unticked channel in the dialog passes through unchanged.
	[Test]
	public void TwoHundredFiftySixLevelsIsIdentity ()
	{
		UnaryPixelOps.PosterizePixel op = new (256, 256, 256, 256);
		for (int v = 0; v < 256; v++) {
			ColorBgra c = ColorBgra.FromBgra ((byte) v, (byte) v, (byte) v, 255);
			Assert.That (op.Apply (c), Is.EqualTo (c));
		}
	}

	[TestCase (2)]
	[TestCase (16)]
	[TestCase (64)]
	public void OpaqueStaysOpaque (int alphaLevels)
	{
		UnaryPixelOps.PosterizePixel op = new (16, 16, 16, alphaLevels);
		Assert.That (op.Apply (ColorBgra.FromBgra (10, 20, 30, 255)).A, Is.EqualTo (255));
	}

	[Test]
	public void AlphaIsPosterizedOnStraightValues ()
	{
		UnaryPixelOps.PosterizePixel op = new (256, 256, 256, 2);

		// Straight white at alpha 100 (premultiplied 100,100,100) snaps to transparent.
		ColorBgra faint = op.Apply (ColorBgra.FromBgra (100, 100, 100, 100));
		Assert.That (faint, Is.EqualTo (ColorBgra.FromBgra (0, 0, 0, 0)));

		// Straight white at alpha 200 snaps to opaque white.
		ColorBgra strong = op.Apply (ColorBgra.FromBgra (200, 200, 200, 200));
		Assert.That (strong, Is.EqualTo (ColorBgra.FromBgra (255, 255, 255, 255)));
	}

	[Test]
	public void AlphaUntouchedWhenDisabled ()
	{
		UnaryPixelOps.PosterizePixel op = new (2, 2, 2);
		Assert.That (op.Apply (ColorBgra.FromBgra (100, 100, 100, 100)).A, Is.EqualTo (100));
	}
}
