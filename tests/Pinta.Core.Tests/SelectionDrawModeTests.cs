using NUnit.Framework;
using Pinta.Tools;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class SelectionDrawModeTests
{
	private static readonly Size image = new (400, 300);

	private static RectangleD Draw (SelectionDrawMode mode, PointD anchor, PointD pointer, double w = 0, double h = 0, bool shift = false)
		=> SelectTool.ComputeDrawRectangle (mode, anchor, pointer, w, h, image, shift);

	[Test]
	public void AnySizeFollowsThePointerInEveryDirection ()
	{
		Assert.That (Draw (SelectionDrawMode.AnySize, new (10, 20), new (50, 60)), Is.EqualTo (new RectangleD (10, 20, 40, 40)));
		Assert.That (Draw (SelectionDrawMode.AnySize, new (50, 60), new (10, 20)), Is.EqualTo (new RectangleD (10, 20, 40, 40)));
	}

	[Test]
	public void ShiftMakesAnySizeSquare ()
	{
		Assert.That (Draw (SelectionDrawMode.AnySize, new (10, 10), new (50, 30), shift: true), Is.EqualTo (new RectangleD (10, 10, 40, 40)));
		Assert.That (Draw (SelectionDrawMode.AnySize, new (100, 100), new (90, 40), shift: true), Is.EqualTo (new RectangleD (40, 40, 60, 60)));
	}

	[Test]
	public void FixedRatioKeepsTheRatioAndReachesThePointer ()
	{
		Assert.That (Draw (SelectionDrawMode.FixedRatio, new (0, 0), new (40, 10), 4, 3), Is.EqualTo (new RectangleD (0, 0, 40, 30)));
		Assert.That (Draw (SelectionDrawMode.FixedRatio, new (0, 0), new (10, 30), 4, 3), Is.EqualTo (new RectangleD (0, 0, 40, 30)));
		Assert.That (Draw (SelectionDrawMode.FixedRatio, new (100, 100), new (60, 90), 4, 3), Is.EqualTo (new RectangleD (60, 70, 40, 30)));
	}

	[Test]
	public void FixedRatioStopsAtTheCanvasEdge ()
	{
		// 2:1 from (300, 0): only 100 px of room to the right, so 100 × 50 however far the pointer goes.
		Assert.That (Draw (SelectionDrawMode.FixedRatio, new (300, 0), new (900, 900), 2, 1), Is.EqualTo (new RectangleD (300, 0, 100, 50)));
	}

	[Test]
	public void FixedSizePlacesAnExactBoxInsideTheCanvas ()
	{
		Assert.That (Draw (SelectionDrawMode.FixedSize, new (0, 0), new (10, 20), 100, 100), Is.EqualTo (new RectangleD (10, 20, 100, 100)));
		Assert.That (Draw (SelectionDrawMode.FixedSize, new (0, 0), new (390, 290), 100, 100), Is.EqualTo (new RectangleD (300, 200, 100, 100)));
		Assert.That (Draw (SelectionDrawMode.FixedSize, new (0, 0), new (5, 5), 1000, 50), Is.EqualTo (new RectangleD (0, 5, 400, 50)));
	}

	[Test]
	public void StraightAlphaUnpremultipliesForTheToleranceCompare ()
	{
		ColorBgra[] pixels = [ColorBgra.FromBgra (0, 0, 128, 128), ColorBgra.FromBgra (0, 0, 255, 255), ColorBgra.Zero];
		FloodTool.ToStraightAlpha (pixels);

		Assert.That (pixels[0], Is.EqualTo (ColorBgra.FromBgra (0, 0, 255, 128)));
		Assert.That (pixels[1], Is.EqualTo (ColorBgra.FromBgra (0, 0, 255, 255)));
		Assert.That (pixels[2], Is.EqualTo (ColorBgra.Zero));
	}
}
