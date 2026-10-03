using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class ImageListLayoutTests
{
	[TestCase (800, 600, 40, 53)] // landscape keeps its aspect ratio
	[TestCase (600, 800, 40, 30)] // portrait
	[TestCase (10000, 100, 40, 120)] // very wide is clamped to the maximum
	[TestCase (10, 10000, 40, 16)] // very tall is clamped to the minimum
	[TestCase (0, 0, 40, 40)] // empty image falls back to a square
	public void ThumbnailWidth (int width, int height, int thumbHeight, int expected)
	{
		Assert.That (ImageListLayout.ThumbnailWidth (new Size (width, height), thumbHeight, 16, 120), Is.EqualTo (expected));
	}

	// Four thumbnails with centres at 10, 30, 50, 70.
	[TestCase (0, 0, 0)] // dropped on itself
	[TestCase (0, 35, 1)] // first moved past the second
	[TestCase (0, 100, 3)] // first moved to the end
	[TestCase (3, 0, 0)] // last moved to the start
	[TestCase (3, 45, 2)] // last moved between the second and third
	[TestCase (1, 60, 2)] // middle moved right by one
	public void DropIndex (int dragged, double x, int expected)
	{
		double[] centers = [10, 30, 50, 70];
		Assert.That (ImageListLayout.DropIndex (centers, dragged, x), Is.EqualTo (expected));
	}
}
