using System.Linq;
using NUnit.Framework;
using Pinta.Tools;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class FillPatternsTest
{
	private static int Coverage (FillPatterns.Pattern pattern)
		=> Enumerable.Range (0, 64).Count (i => pattern.IsOn (i % 8, i / 8));

	[Test]
	public void Solid_Color_Is_First_And_Covers_Everything ()
	{
		Assert.That (FillPatterns.All[0].IsSolid, Is.True);
		Assert.That (Coverage (FillPatterns.All[0]), Is.EqualTo (64));
	}

	[TestCase (5, 3)]
	[TestCase (25, 16)]
	[TestCase (50, 32)]
	[TestCase (90, 57)]
	public void Percent_Patterns_Cover_Their_Share (int percent, int cells)
	{
		FillPatterns.Pattern pattern = FillPatterns.All.Single (p => p.Name == $"Percent {percent:00}");
		Assert.That (Coverage (pattern), Is.EqualTo (cells));
	}

	[Test]
	public void Patterns_Tile_Every_Eight_Pixels ()
	{
		foreach (FillPatterns.Pattern pattern in FillPatterns.All)
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++)
					Assert.That (pattern.IsOn (x + 8, y + 16), Is.EqualTo (pattern.IsOn (x, y)), pattern.Name);
	}

	[Test]
	public void Hatch_Patterns_Are_Not_Solid_Or_Empty ()
	{
		foreach (FillPatterns.Pattern pattern in FillPatterns.All.Skip (1))
			Assert.That (Coverage (pattern), Is.InRange (1, 63), pattern.Name);
	}
}
