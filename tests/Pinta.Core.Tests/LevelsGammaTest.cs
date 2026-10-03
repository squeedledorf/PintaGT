using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class LevelsGammaTest
{
	// Paint.NET's Levels dialog (its documentation screenshot shows 0.78 with the grey arrow
	// above centre on a brightened image) treats the gamma as the output exponent:
	// below 1 brightens the midtones, above 1 darkens them.
	[TestCase (0.78f, true)]
	[TestCase (1.4f, false)]
	public void GammaBelowOneBrightens (float gamma, bool brighter)
	{
		UnaryPixelOps.Level level = new (
			ColorBgra.Black,
			ColorBgra.White,
			[gamma, gamma, gamma],
			ColorBgra.Black,
			ColorBgra.White);

		ColorBgra grey = ColorBgra.FromBgra (128, 128, 128, 255);
		ColorBgra result = level.Apply (grey);

		Assert.That (result.R > grey.R, Is.EqualTo (brighter));
		Assert.That (result.R, Is.Not.EqualTo (grey.R));
	}
}
