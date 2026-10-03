using System;
using System.Linq;
using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ToolBlendModeTests
{
	// Paint.NET's Layer Properties and tool bar list (getpaint.net/doc/latest/BlendModes.html).
	[Test]
	public void BlendModeList_IsPaintNetsFourteenInOrder ()
	{
		Assert.That (UserBlendOps.GetAllBlendModes (), Is.EqualTo (new[] {
			BlendMode.Normal, BlendMode.Multiply, BlendMode.Additive, BlendMode.ColorBurn, BlendMode.ColorDodge,
			BlendMode.Reflect, BlendMode.Glow, BlendMode.Overlay, BlendMode.Difference, BlendMode.Negation,
			BlendMode.Lighten, BlendMode.Darken, BlendMode.Screen, BlendMode.Xor,
		}));
	}

	[Test]
	public void BlendModeNames_RoundTrip ()
	{
		foreach (BlendMode mode in Enum.GetValues<BlendMode> ())
			Assert.That (UserBlendOps.GetBlendModeByName (UserBlendOps.GetBlendModeName (mode)), Is.EqualTo (mode));
	}

	[Test]
	public void OraCompositeOp_RoundTripsEveryMode ()
	{
		foreach (BlendMode mode in Enum.GetValues<BlendMode> ())
			Assert.That (OraFormat.StandardToBlendMode (OraFormat.BlendModeToStandard (mode)), Is.EqualTo (mode));
	}

	// Multiply through Cairo's operator: red over grey darkens the green and blue channels.
	[Test]
	public void PaintWithBlendMode_Multiply_Darkens ()
	{
		ColorBgra result = PaintOnePixel (BlendMode.Multiply, ColorBgra.FromBgra (128, 128, 128, 255), ColorBgra.FromBgra (0, 0, 255, 255));
		Assert.That (result.R, Is.EqualTo (128).Within (1));
		Assert.That (result.G, Is.EqualTo (0));
		Assert.That (result.B, Is.EqualTo (0));
	}

	// The modes Cairo lacks are blended in software with Paint.NET's ops.
	[TestCase (BlendMode.Additive)]
	[TestCase (BlendMode.Reflect)]
	[TestCase (BlendMode.Glow)]
	[TestCase (BlendMode.Negation)]
	public void PaintWithBlendMode_SoftwareModes_MatchTheirOp (BlendMode mode)
	{
		ColorBgra bottom = ColorBgra.FromBgra (40, 120, 200, 255);
		ColorBgra top = ColorBgra.FromBgra (90, 60, 30, 255);
		ColorBgra expected = UserBlendOps.GetSoftwareBlendOp (mode)!.Apply (bottom, top);

		ColorBgra result = PaintOnePixel (mode, bottom, top);

		Assert.That (result, Is.EqualTo (expected));
	}

	[Test]
	public void PaintWithBlendMode_SoftwareMode_LeavesUncoveredPixels ()
	{
		using ImageSurface dst = CairoExtensions.CreateImageSurface (Format.Argb32, 2, 1);
		using ImageSurface src = CairoExtensions.CreateImageSurface (Format.Argb32, 2, 1);
		dst.GetPixelData ().Fill (ColorBgra.FromBgra (10, 20, 30, 255));
		dst.MarkDirty ();
		src.GetPixelData ()[1] = ColorBgra.FromBgra (200, 200, 200, 255);
		src.MarkDirty ();

		using (Context g = new (dst))
			g.BlendSurface (src, BlendMode.Additive);

		dst.Flush ();
		Assert.That (dst.GetPixelData ()[0], Is.EqualTo (ColorBgra.FromBgra (10, 20, 30, 255)));
		Assert.That (dst.GetPixelData ()[1], Is.EqualTo (ColorBgra.FromBgra (210, 220, 230, 255)));
	}

	// Selection Quality: pixelated clipping gives a diagonal selection hard edges.
	[TestCase (false, ExpectedResult = true)]
	[TestCase (true, ExpectedResult = false)]
	public bool Clip_PixelatedQuality_HasHardEdges (bool antialiased)
	{
		DocumentSelection selection = new ();
		selection.SelectionPolygons.Add ([new (0, 0), new (16, 0), new (0, 16)]);

		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 16, 16);
		bool previous = DocumentSelection.AntialiasedClipping;
		try {
			DocumentSelection.AntialiasedClipping = antialiased;
			using Context g = new (surface);
			selection.Clip (g);
			g.SetSourceRgba (0, 0, 0, 1);
			g.Paint ();
		} finally {
			DocumentSelection.AntialiasedClipping = previous;
		}

		surface.Flush ();
		return surface.GetPixelData ().ToArray ().All (c => c.A is 0 or 255);
	}

	private static ColorBgra PaintOnePixel (BlendMode mode, ColorBgra bottom, ColorBgra top)
	{
		using ImageSurface dst = CairoExtensions.CreateImageSurface (Format.Argb32, 1, 1);
		using ImageSurface src = CairoExtensions.CreateImageSurface (Format.Argb32, 1, 1);
		dst.GetPixelData ()[0] = bottom;
		dst.MarkDirty ();
		src.GetPixelData ()[0] = top;
		src.MarkDirty ();

		using (Context g = new (dst))
			g.BlendSurface (src, mode);

		dst.Flush ();
		return dst.GetPixelData ()[0];
	}
}
