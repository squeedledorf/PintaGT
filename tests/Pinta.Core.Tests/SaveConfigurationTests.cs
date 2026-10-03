using System;
using System.Collections.Generic;
using Cairo;
using GdkPixbuf;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class SaveConfigurationTests
{
	[OneTimeSetUp]
	public void Init ()
	{
		Gio.Module.Initialize ();
		GdkPixbuf.Module.Initialize ();
		Cairo.Module.Initialize ();
		Gdk.Module.Initialize ();
	}

	private static ImageSurface Stripes (int colors, byte alpha = 255)
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 64, 16);
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int i = 0; i < data.Length; i++) {
			int c = (int) ((uint) i * 2654435761u >> 20) % colors; // scattered, so zlib cannot shrink the true-colour file to nothing
			data[i] = ColorBgra.FromBgra ((byte) (c * 3), (byte) (255 - c), (byte) (c * 7 % 256), alpha).ToPremultipliedAlpha ();
		}
		surface.MarkDirty ();
		return surface;
	}

	private static Pixbuf Decode (byte[] data)
	{
		using GLib.Bytes bytes = GLib.Bytes.New (data);
		using Gio.MemoryInputStream stream = Gio.MemoryInputStream.NewFromBytes (bytes);
		return Pixbuf.NewFromStream (stream, null)!;
	}

	private static ColorBgra[] DecodePixels (byte[] data)
	{
		using Pixbuf pb = Decode (data);
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, pb.Width, pb.Height);
		using (Context g = new (surface))
			g.DrawPixbuf (pb, PointD.Zero);
		return surface.GetReadOnlyPixelData ().ToArray ();
	}

	private static byte ColorType (byte[] png) => png[25]; // IHDR colour type: 2 RGB, 3 palette, 6 RGBA

	[Test]
	public void AutoDetectPicksEightBitForFewColours ()
	{
		using ImageSurface image = Stripes (16);
		byte[] png = PngFormat.Encode (image, SaveConfiguration.Defaults, 96);
		Assert.That (ColorType (png), Is.EqualTo (3));

		// Nothing is lost.
		Assert.That (DecodePixels (png), Is.EqualTo (image.GetReadOnlyPixelData ().ToArray ()));
	}

	[Test]
	public void AutoDetectDropsAlphaForAnOpaquePhoto ()
	{
		using ImageSurface image = CairoExtensions.CreateImageSurface (Format.Argb32, 40, 40);
		Span<ColorBgra> data = image.GetPixelData ();
		for (int i = 0; i < data.Length; i++)
			data[i] = ColorBgra.FromBgra ((byte) i, (byte) (i / 7), (byte) (i * 13), 255);
		image.MarkDirty ();

		Assert.That (ColorType (PngFormat.Encode (image, SaveConfiguration.Defaults, 96)), Is.EqualTo (2));
	}

	[TestCase (PngBitDepth.Bpp32, 6)]
	[TestCase (PngBitDepth.Bpp24, 2)]
	[TestCase (PngBitDepth.Bpp8, 3)]
	public void ExplicitBitDepths (PngBitDepth depth, int colorType)
	{
		using ImageSurface image = Stripes (300 % 64);
		byte[] png = PngFormat.Encode (image, SaveConfiguration.Defaults with { BitDepth = depth }, 96);
		Assert.That (ColorType (png), Is.EqualTo (colorType));
		using Pixbuf decoded = Decode (png);
		Assert.That (decoded.Width, Is.EqualTo (64));
	}

	[Test]
	public void EightBitQuantizesToAtMost256Colours ()
	{
		using ImageSurface image = CairoExtensions.CreateImageSurface (Format.Argb32, 256, 256);
		Span<ColorBgra> data = image.GetPixelData ();
		for (int y = 0; y < 256; y++)
			for (int x = 0; x < 256; x++)
				data[y * 256 + x] = ColorBgra.FromBgra ((byte) x, (byte) y, (byte) (x ^ y), 255);
		image.MarkDirty ();

		foreach (int dither in new[] { 0, 8 }) {
			byte[] png = IndexedPng.EncodeQuantized (image, dither, 128);
			ColorBgra[] pixels = DecodePixels (png);
			HashSet<ColorBgra> colours = [];
			long error = 0;
			for (int i = 0; i < 256 * 256; i++) {
				colours.Add (pixels[i]);
				error += Math.Abs (pixels[i].R - (i % 256 ^ i / 256)) + Math.Abs (pixels[i].B - i % 256);
			}
			Assert.That (colours.Count, Is.LessThanOrEqualTo (256));
			Assert.That (colours.Count, Is.GreaterThan (64));
			Assert.That (error / (256 * 256 * 2.0), Is.LessThan (24), $"dither {dither}");
		}
	}

	[Test]
	public void TransparencyThresholdSplitsAlpha ()
	{
		using ImageSurface low = Stripes (4, alpha: 100);
		Assert.That (DecodePixels (IndexedPng.EncodeQuantized (low, 7, 128))[0].A, Is.EqualTo (0));
		Assert.That (DecodePixels (IndexedPng.EncodeQuantized (low, 7, 50))[0].A, Is.EqualTo (255));
	}

	[Test]
	public void DpiRoundTripsThroughPngAndJpeg ()
	{
		using ImageSurface image = Stripes (4);
		byte[] png = PngFormat.Encode (image, SaveConfiguration.Defaults with { BitDepth = PngBitDepth.Bpp32 }, 300);
		Assert.That (ImageDpi.Read (png), Is.EqualTo (300).Within (0.01));
		Assert.That (Decode (png).Width, Is.EqualTo (64)); // still a valid PNG

		using Pixbuf pb = image.ToPixbuf (includeAlpha: false);
		byte[] jpeg = ImageDpi.Write (pb.SaveToBuffer ("jpeg", ["quality"], ["90"]), 72);
		Assert.That (ImageDpi.Read (jpeg), Is.EqualTo (72));
		Assert.That (Decode (jpeg).Width, Is.EqualTo (64));

		// Rewriting replaces the stored value rather than adding a second one.
		Assert.That (ImageDpi.Read (ImageDpi.Write (png, 150)), Is.EqualTo (150).Within (0.01));
		Assert.That (ImageDpi.Read (ImageDpi.Write (jpeg, 150)), Is.EqualTo (150));
	}

	[Test]
	public void DpiReadIgnoresAChunkLengthPastTheData ()
	{
		// A chunk claiming 2 GB must not overflow the scan.
		byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x7F, 0xFF, 0xFF, 0xFF, (byte) 'i', (byte) 'C', (byte) 'C', (byte) 'P', 0, 0, 0, 0];
		Assert.That (ImageDpi.Read (png), Is.Null);
	}
}
