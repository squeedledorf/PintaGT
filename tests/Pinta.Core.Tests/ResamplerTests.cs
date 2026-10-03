using System;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ResamplerTests
{
	[OneTimeSetUp]
	public void Init () => Cairo.Module.Initialize ();

	private static ImageSurface Solid (int width, int height, ColorBgra color)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		surface.GetPixelData ().Fill (color);
		surface.MarkDirty ();
		return surface;
	}

	[Test]
	public void EveryModeKeepsAFlatColourFlat ([Values] ResamplingMode mode, [Values] bool gamma)
	{
		ColorBgra color = ColorBgra.FromBgra (40, 120, 200, 255);
		using ImageSurface source = Solid (37, 23, color);

		foreach (Size size in new Size[] { new (80, 50), new (9, 5), new (37, 23), new (1, 1) }) {
			using ImageSurface result = Resampler.Resize (source, size, mode, gamma);
			Assert.That (result.Width, Is.EqualTo (size.Width));
			Assert.That (result.Height, Is.EqualTo (size.Height));
			foreach (ColorBgra p in result.GetReadOnlyPixelData ().ToArray ()) {
				Assert.That (Math.Abs (p.B - color.B), Is.LessThanOrEqualTo (1), $"{mode} {size}");
				Assert.That (Math.Abs (p.G - color.G), Is.LessThanOrEqualTo (1), $"{mode} {size}");
				Assert.That (Math.Abs (p.R - color.R), Is.LessThanOrEqualTo (1), $"{mode} {size}");
				Assert.That (p.A, Is.EqualTo (255), $"{mode} {size}");
			}
		}
	}

	[Test]
	public void NearestNeighborDoublesPixelsExactly ()
	{
		using ImageSurface source = Solid (2, 1, ColorBgra.Black);
		source.GetPixelData ()[1] = ColorBgra.White;
		source.MarkDirty ();

		using ImageSurface result = Resampler.Resize (source, new Size (4, 2), ResamplingMode.NearestNeighbor, false);
		ColorBgra[] p = result.GetReadOnlyPixelData ().ToArray ();
		Assert.That (p, Is.EqualTo (new[] {
			ColorBgra.Black, ColorBgra.Black, ColorBgra.White, ColorBgra.White,
			ColorBgra.Black, ColorBgra.Black, ColorBgra.White, ColorBgra.White }));
	}

	// Shrinking black/white stripes to one pixel: an area average gives mid grey in sRGB (128),
	// and in linear light a brighter grey (about 188), which is what "Use gamma correction" is for.
	[TestCase (false, 128)]
	[TestCase (true, 188)]
	public void FantAveragesTheArea (bool gamma, int expected)
	{
		using ImageSurface source = Solid (8, 1, ColorBgra.Black);
		Span<ColorBgra> data = source.GetPixelData ();
		for (int x = 1; x < 8; x += 2)
			data[x] = ColorBgra.White;
		source.MarkDirty ();

		using ImageSurface result = Resampler.Resize (source, new Size (1, 1), ResamplingMode.Fant, gamma);
		Assert.That (result.GetReadOnlyPixelData ()[0].R, Is.EqualTo (expected).Within (1));
	}

	[Test]
	public void TransparentPixelsDoNotDarkenTheEdges ()
	{
		// Half transparent black, half opaque red: the result must stay pure red where it is not clear.
		using ImageSurface source = Solid (4, 4, ColorBgra.Transparent);
		Span<ColorBgra> data = source.GetPixelData ();
		for (int i = 0; i < 16; i += 2)
			data[i] = ColorBgra.FromBgra (0, 0, 255, 255);
		source.MarkDirty ();

		using ImageSurface result = Resampler.Resize (source, new Size (2, 2), ResamplingMode.Bicubic, true);
		foreach (ColorBgra p in result.GetReadOnlyPixelData ().ToArray ()) {
			Assert.That (p.A, Is.GreaterThan (0));
			Assert.That (p.R, Is.EqualTo (p.A).Within (1)); // premultiplied pure red
			Assert.That (p.G, Is.EqualTo (0));
		}
	}
}

[TestFixture]
internal sealed class PrintSizeTests
{
	// Values from the Paint.NET documentation's New and Canvas Size screenshots.
	[Test]
	public void MatchesThePaintNetScreenshots ()
	{
		Assert.That (PrintSize.PixelsToPrint (800, 96, PrintUnit.Inches), Is.EqualTo (8.33).Within (0.005));
		Assert.That (PrintSize.PixelsToPrint (600, 96, PrintUnit.Inches), Is.EqualTo (6.25).Within (0.005));
		Assert.That (PrintSize.DpiToResolution (96, PrintUnit.Centimeters), Is.EqualTo (37.80).Within (0.005));
		Assert.That (PrintSize.PixelsToPrint (800, 96, PrintUnit.Centimeters), Is.EqualTo (21.17).Within (0.005));
		Assert.That (PrintSize.PixelsToPrint (600, 96, PrintUnit.Centimeters), Is.EqualTo (15.88).Within (0.005));
	}

	[Test]
	public void PrintSizeTimesDpiIsPixelSize ()
	{
		// The documentation's example: two inches at 96 pixels/inch is 192 pixels.
		Assert.That (PrintSize.PrintToPixels (2, 96, PrintUnit.Inches), Is.EqualTo (192));
		Assert.That (PrintSize.ResolutionToDpi (PrintSize.DpiToResolution (300, PrintUnit.Centimeters), PrintUnit.Centimeters), Is.EqualTo (300).Within (1e-9));
	}

	[Test]
	public void NewSizeIsTheUncompressedSize ()
	{
		Assert.That (PrintSize.FormatImageMemory (new Size (800, 600)), Is.EqualTo ("1.8 MB"));
		Assert.That (PrintSize.FormatImageMemory (new Size (290, 434)), Is.EqualTo ("491.6 KB"));
	}
}
