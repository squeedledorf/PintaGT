using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using PaintDotNet;
using PaintDotNet.Effects;

namespace Pinta.PdnPlugins.Tests;

/// <summary>Runs a tiny classic effect through the adapter's render session, as Pinta's tile threads do.</summary>
[TestFixture]
internal sealed class RenderSessionTests
{
	/// <summary>Inverts colour, keeps alpha: the way a typical Paint.NET effect works on straight alpha.</summary>
	public sealed class InvertTestEffect : Effect
	{
		public InvertTestEffect () : base ("Invert test", null, EffectFlags.None) { }

		public override void Render (EffectConfigToken token, RenderArgs dstArgs, RenderArgs srcArgs, Rectangle[] rois, int startIndex, int length)
		{
			for (int i = startIndex; i < startIndex + length; i++) {
				Rectangle r = rois[i];
				for (int y = r.Top; y < r.Bottom; y++)
					for (int x = r.Left; x < r.Right; x++) {
						ColorBgra c = srcArgs.Surface[x, y];
						dstArgs.Surface[x, y] = ColorBgra.FromBgra ((byte) (255 - c.B), (byte) (255 - c.G), (byte) (255 - c.R), c.A);
					}
			}
		}
	}

	[OneTimeSetUp]
	public void InitCairo () => Cairo.Module.Initialize ();

	private static PdnPluginInfo Info () => new () {
		EffectType = typeof (InvertTestEffect),
		File = "test",
		Name = "Invert test",
		MenuCategory = "",
		IconName = "",
		Category = EffectCategory.Effect,
		ClassicOptions = new EffectOptions (),
	};

	private static uint Pixel (Cairo.ImageSurface s, int x, int y)
		=> MemoryMarshal.Cast<byte, uint> (s.GetData ().Slice (y * s.Stride + x * 4, 4))[0];

	[Test]
	public void StraightAlphaInvertAndSelectionClip ()
	{
		const int W = 64, H = 64;
		using Cairo.ImageSurface src = new (Cairo.Format.Argb32, W, H);
		using Cairo.ImageSurface dst = new (Cairo.Format.Argb32, W, H);
		// Premultiplied 50% red: straight (255, 0, 0, 128).
		Span<uint> px = MemoryMarshal.Cast<byte, uint> (src.GetData ());
		px.Fill (0x80800000);
		MemoryMarshal.Cast<byte, uint> (dst.GetData ()).Fill (0xDEADBEEF);
		src.MarkDirty ();

		// Selection: an L of two rectangles.
		List<Rectangle> scans = [new (8, 8, 16, 8), new (8, 16, 8, 16)];
		RenderSession session = new (Info (), null, new RenderEnvironment (default, default, 2, scans));

		// Rows rendered concurrently, like Pinta's tile threads.
		Parallel.For (0, H, y => session.Render (src, dst, new Rectangle (0, y, W, 1)));

		// Inverted straight colour is (0, 255, 255, 128); premultiplied that is 0x80008080.
		Assert.That (Pixel (dst, 8, 8), Is.EqualTo (0x80008080u).Within (0x00010101u));
		Assert.That (Pixel (dst, 23, 15), Is.EqualTo (Pixel (dst, 8, 8)));
		Assert.That (Pixel (dst, 15, 31), Is.EqualTo (Pixel (dst, 8, 8)));
		// Outside the selection nothing is written.
		Assert.That (Pixel (dst, 16, 16), Is.EqualTo (0xDEADBEEFu));
		Assert.That (Pixel (dst, 7, 8), Is.EqualTo (0xDEADBEEFu));
		Assert.That (Pixel (dst, 8, 32), Is.EqualTo (0xDEADBEEFu));
	}

	[Test]
	public void CancelledSessionWritesNothing ()
	{
		using Cairo.ImageSurface src = new (Cairo.Format.Argb32, 4, 4);
		using Cairo.ImageSurface dst = new (Cairo.Format.Argb32, 4, 4);
		MemoryMarshal.Cast<byte, uint> (dst.GetData ()).Fill (0xDEADBEEF);
		RenderSession session = new (Info (), null, new RenderEnvironment (default, default, 2, null));
		session.Cancel ();
		session.Render (src, dst, new Rectangle (0, 0, 4, 4));
		Assert.That (Pixel (dst, 1, 1), Is.EqualTo (0xDEADBEEFu));
	}
}
