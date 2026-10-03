using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using NUnit.Framework;
using PaintDotNet;

namespace Pinta.PdnPlugins.Tests;

[TestFixture]
internal sealed class RoiTests
{
	private static byte[] Mask (int w, int h, System.Func<int, int, bool> inside)
	{
		byte[] m = new byte[w * h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				m[y * w + x] = inside (x, y) ? (byte) 255 : (byte) 0;
		return m;
	}

	private static void AssertCovers (List<Rectangle> scans, int w, int h, System.Func<int, int, bool> inside, int ox = 0, int oy = 0)
	{
		int[,] count = new int[w, h];
		foreach (Rectangle r in scans)
			for (int y = r.Top; y < r.Bottom; y++)
				for (int x = r.Left; x < r.Right; x++)
					count[x - ox, y - oy]++;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				Assert.That (count[x, y], Is.EqualTo (inside (x, y) ? 1 : 0), $"pixel {x},{y}");
	}

	[Test]
	public void RectangularMaskIsOneRectangle ()
	{
		byte[] mask = Mask (10, 8, (x, y) => true);
		List<Rectangle> scans = RoiUtil.ScansFromMask (mask, 10, 8, 10, 5, 7);
		Assert.That (scans, Is.EqualTo (new[] { new Rectangle (5, 7, 10, 8) }));
	}

	[Test]
	public void LShapeAndHoleAreCoveredExactlyOnce ()
	{
		static bool L (int x, int y) => x < 3 || y > 5;
		List<Rectangle> scans = RoiUtil.ScansFromMask (Mask (10, 8, L), 10, 8, 10, 0, 0);
		AssertCovers (scans, 10, 8, L);

		static bool Ring (int x, int y) => !(x is > 2 and < 7 && y is > 2 and < 5);
		List<Rectangle> ring = RoiUtil.ScansFromMask (Mask (10, 8, Ring), 10, 8, 10, 100, 50);
		AssertCovers (ring, 10, 8, Ring, 100, 50);
		Assert.That (ring.Select (r => r.Y), Is.Ordered);
	}

	[Test]
	public void EllipseMaskWithStrideAndOffset ()
	{
		const int w = 37, h = 23, stride = 40;
		static bool E (int x, int y) => (x - 18) * (x - 18) / 324.0 + (y - 11) * (y - 11) / 121.0 <= 1;
		byte[] mask = new byte[stride * h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				mask[y * stride + x] = E (x, y) ? (byte) 1 : (byte) 0;
		List<Rectangle> scans = RoiUtil.ScansFromMask (mask, w, h, stride, 3, 4);
		AssertCovers (scans, w, h, E, 3, 4);
	}

	[Test]
	public void ClipWithoutSelectionReturnsTile ()
	{
		Rectangle tile = new (0, 5, 100, 1);
		Assert.That (RoiUtil.Clip (tile, null), Is.EqualTo (new[] { tile }));
		Assert.That (RoiUtil.Clip (Rectangle.Empty, null), Is.Empty);
	}

	[Test]
	public void ClipIntersectsTileWithScans ()
	{
		List<Rectangle> scans = [new (10, 0, 20, 10), new (0, 10, 5, 10), new (40, 10, 5, 10)];
		Assert.That (RoiUtil.Clip (new Rectangle (0, 4, 100, 1), scans), Is.EqualTo (new[] { new Rectangle (10, 4, 20, 1) }));
		Assert.That (RoiUtil.Clip (new Rectangle (0, 12, 42, 1), scans), Is.EqualTo (new[] { new Rectangle (0, 12, 5, 1), new Rectangle (40, 12, 2, 1) }));
		Assert.That (RoiUtil.Clip (new Rectangle (0, 30, 100, 1), scans), Is.Empty);
	}

	[Test]
	public void PdnRegionUnionAndExcludeKeepScansDisjoint ()
	{
		PdnRegion r = new (new Rectangle (0, 0, 10, 10));
		r.Union (new Rectangle (5, 5, 10, 10));
		Assert.That (r.GetArea64 (), Is.EqualTo (175));
		Assert.That (r.GetBoundsInt (), Is.EqualTo (new Rectangle (0, 0, 15, 15)));
		r.Exclude (new Rectangle (0, 0, 15, 2));
		Assert.That (r.GetArea64 (), Is.EqualTo (155));
		Assert.That (r.IsVisible (1, 1), Is.False);
		Assert.That (r.IsVisible (12, 12), Is.True);
	}
}
