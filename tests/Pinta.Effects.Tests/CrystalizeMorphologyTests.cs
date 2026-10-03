using System;
using Cairo;
using NUnit.Framework;
using Pinta.Core;
using Pinta.Effects.Tests;

namespace Pinta.Effects;

[TestFixture]
internal sealed class CrystalizeMorphologyTests
{
	private static readonly ColorBgra black = ColorBgra.FromBgra (0, 0, 0, 255);
	private static readonly ColorBgra white = ColorBgra.FromBgra (255, 255, 255, 255);

	private static ColorBgra[] Dot (int width, int height, int x, int y, ColorBgra background, ColorBgra dot)
	{
		ColorBgra[] pixels = new ColorBgra[width * height];
		Array.Fill (pixels, background);
		pixels[y * width + x] = dot;
		return pixels;
	}

	[Test]
	public void DilateGrowsALightPixelIntoARectangle ()
	{
		ColorBgra[] src = Dot (9, 9, 4, 4, black, white);
		ColorBgra[] dst = new ColorBgra[src.Length];
		MorphologyEffect.Apply (src, dst, 9, 9, new RectangleI (0, 0, 9, 9), 2, 1, dilate: true);

		for (int y = 0; y < 9; ++y)
			for (int x = 0; x < 9; ++x) {
				bool inside = Math.Abs (x - 4) <= 2 && Math.Abs (y - 4) <= 1;
				Assert.That (dst[y * 9 + x], Is.EqualTo (inside ? white : black), $"({x},{y})");
			}
	}

	[Test]
	public void ErodeGrowsADarkPixelAndClampsAtTheEdge ()
	{
		ColorBgra[] src = Dot (9, 9, 0, 0, white, black);
		ColorBgra[] dst = new ColorBgra[src.Length];
		MorphologyEffect.Apply (src, dst, 9, 9, new RectangleI (0, 0, 9, 9), 1, 1, dilate: false);

		for (int y = 0; y < 9; ++y)
			for (int x = 0; x < 9; ++x)
				Assert.That (dst[y * 9 + x], Is.EqualTo (x <= 1 && y <= 1 ? black : white), $"({x},{y})");
	}

	[Test]
	public void MorphologyTileMatchesWholeImage ()
	{
		Random random = new (1);
		ColorBgra[] src = new ColorBgra[20 * 20];
		for (int i = 0; i < src.Length; ++i)
			src[i] = random.RandomColorBgra ();

		ColorBgra[] whole = new ColorBgra[src.Length];
		ColorBgra[] tiled = new ColorBgra[src.Length];
		MorphologyEffect.Apply (src, whole, 20, 20, new RectangleI (0, 0, 20, 20), 3, 2, dilate: true);
		MorphologyEffect.Apply (src, tiled, 20, 20, new RectangleI (0, 0, 20, 7), 3, 2, dilate: true);
		MorphologyEffect.Apply (src, tiled, 20, 20, new RectangleI (0, 7, 20, 13), 3, 2, dilate: true);

		Assert.That (tiled, Is.EqualTo (whole));
	}

	[Test]
	public void CrystalizeNearestSiteIsTheTrueNearest ()
	{
		const int cellSize = 10;
		for (int y = 0; y < 60; y += 3)
			for (int x = 0; x < 60; x += 3) {
				PointD found = CrystalizeEffect.NearestSite (x, y, cellSize, seed: 7);
				double foundDistance = Distance (found, x, y);

				// Brute force over a wide neighbourhood of cells.
				for (int j = -3; j < 10; ++j)
					for (int i = -3; i < 10; ++i) {
						PointD site = CrystalizeEffect.Site (i, j, cellSize, seed: 7);
						Assert.That (foundDistance, Is.LessThanOrEqualTo (Distance (site, x, y) + 1e-9));
					}
			}

		static double Distance (PointD p, double x, double y)
			=> (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
	}

	[Test]
	public void CrystalizeTileMatchesWholeImage ()
	{
		CrystalizeEffect effect = new (Utilities.CreateMockServices ());
		effect.Data.CellSize = 12;

		using ImageSurface source = Utilities.LoadImage ("input.png");
		using ImageSurface whole = CairoExtensions.CreateImageSurface (Format.Argb32, source.Width, source.Height);
		using ImageSurface tiled = CairoExtensions.CreateImageSurface (Format.Argb32, source.Width, source.Height);

		effect.Render (source, whole, [source.GetBounds ()]);
		int half = source.Height / 2;
		effect.Render (source, tiled, [new RectangleI (0, 0, source.Width, half), new RectangleI (0, half, source.Width, source.Height - half)]);

		Utilities.CompareImages (tiled, whole);
	}
}
