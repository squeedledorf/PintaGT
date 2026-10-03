using System;
using System.Collections.Generic;
using System.Drawing;
using PaintDotNet.Rendering;

namespace PaintDotNet
{
	/// <summary>An operation applied to runs of pixels.</summary>
	[Serializable]
	public abstract unsafe class PixelOp
	{
		protected PixelOp () { }

		public virtual void Apply (ColorBgra* dst, ColorBgra* src, int length)
		{
			for (int i = 0; i < length; i++) dst[i] = src[i];
		}

		public virtual void Apply (Surface dst, Point dstOffset, Surface src, Point srcOffset, int scanLength)
			=> Apply (dst.GetPointAddress (dstOffset.X, dstOffset.Y), src.GetPointAddress (srcOffset.X, srcOffset.Y), scanLength);

		public void Apply (Surface dst, Point dstOffset, Surface src, Point srcOffset, Size roiSize)
		{
			for (int y = 0; y < roiSize.Height; y++)
				Apply (dst, new Point (dstOffset.X, dstOffset.Y + y), src, new Point (srcOffset.X, srcOffset.Y + y), roiSize.Width);
		}

		public void ApplyBase (Surface dst, Point dstOffset, Surface src, Point srcOffset, Size roiSize) => Apply (dst, dstOffset, src, srcOffset, roiSize);

		public void Apply (Surface dst, Surface src, Rectangle[] rois, int startIndex, int length)
		{
			for (int i = startIndex; i < startIndex + length; i++) {
				Rectangle r = Rectangle.Intersect (rois[i], Rectangle.Intersect (dst.Bounds, src.Bounds));
				if (!r.IsEmpty) Apply (dst, r.Location, src, r.Location, r.Size);
			}
		}

		public void Apply (Surface dst, Surface src, RectInt32[] rois, int startIndex, int length)
			=> Apply (dst, src, Array.ConvertAll (rois, r => (Rectangle) r), startIndex, length);

		public void Apply (Surface dst, Surface src, IList<RectInt32> rois, int startIndex, int length)
		{
			for (int i = startIndex; i < startIndex + length; i++) Apply (dst, src, [(Rectangle) rois[i]], 0, 1);
		}

		/// <summary>Alpha of <paramref name="ra"/> composited over <paramref name="la"/>.</summary>
		public static byte ComputeAlpha (byte la, byte ra) => (byte) ((la * (256 - (ra + (ra >> 7))) >> 8) + ra);
	}

	[Serializable]
	public abstract unsafe class UnaryPixelOp : PixelOp
	{
		protected UnaryPixelOp () { }

		public abstract ColorBgra Apply (ColorBgra color);

		public override void Apply (ColorBgra* dst, ColorBgra* src, int length)
		{
			for (int i = 0; i < length; i++) dst[i] = Apply (src[i]);
		}

		public virtual void Apply (ColorBgra* ptr, int length)
		{
			for (int i = 0; i < length; i++) ptr[i] = Apply (ptr[i]);
		}

		public override void Apply (Surface dst, Point dstOffset, Surface src, Point srcOffset, int scanLength)
			=> Apply (dst.GetPointAddress (dstOffset.X, dstOffset.Y), src.GetPointAddress (srcOffset.X, srcOffset.Y), scanLength);

		public void Apply (Surface dst, Surface src, Rectangle roi) => Apply (dst, src, [roi], 0, 1);

		public void Apply (Surface surface, Rectangle roi)
		{
			roi.Intersect (surface.Bounds);
			for (int y = roi.Top; y < roi.Bottom; y++)
				Apply (surface.GetPointAddressUnchecked (roi.X, y), roi.Width);
		}

		public void Apply (Surface surface, RectInt32 roi) => Apply (surface, (Rectangle) roi);
		public void Apply (Surface surface, Rectangle[] roi) => Apply (surface, roi, 0, roi.Length);
		public void Apply (Surface surface, RectInt32[] roi) => Apply (surface, roi, 0, roi.Length);

		public void Apply (Surface surface, Rectangle[] roi, int startIndex, int length)
		{
			for (int i = startIndex; i < startIndex + length; i++) Apply (surface, roi[i]);
		}

		public void Apply (Surface surface, RectInt32[] roi, int startIndex, int length)
		{
			for (int i = startIndex; i < startIndex + length; i++) Apply (surface, (Rectangle) roi[i]);
		}

		public void Apply (Surface surface, PdnRegion roi) => Apply (surface, roi.GetRegionScansReadOnlyInt ());
	}

	[Serializable]
	public abstract unsafe class BinaryPixelOp : PixelOp
	{
		protected BinaryPixelOp () { }

		public abstract ColorBgra Apply (ColorBgra lhs, ColorBgra rhs);

		public virtual void Apply (ColorBgra* dst, ColorBgra* lhs, ColorBgra* rhs, int length)
		{
			for (int i = 0; i < length; i++) dst[i] = Apply (lhs[i], rhs[i]);
		}

		/// <summary>Draws <paramref name="src"/> onto <paramref name="dst"/> (dst is the left-hand side).</summary>
		public override void Apply (ColorBgra* dst, ColorBgra* src, int length)
		{
			for (int i = 0; i < length; i++) dst[i] = Apply (dst[i], src[i]);
		}

		public override void Apply (Surface dst, Point dstOffset, Surface src, Point srcOffset, int roiLength)
			=> Apply (dst.GetPointAddress (dstOffset.X, dstOffset.Y), src.GetPointAddress (srcOffset.X, srcOffset.Y), roiLength);

		public void Apply (Surface dst, Point dstOffset, Surface lhs, Point lhsOffset, Surface rhs, Point rhsOffset, Size roiSize)
		{
			for (int y = 0; y < roiSize.Height; y++)
				Apply (
					dst.GetPointAddress (dstOffset.X, dstOffset.Y + y),
					lhs.GetPointAddress (lhsOffset.X, lhsOffset.Y + y),
					rhs.GetPointAddress (rhsOffset.X, rhsOffset.Y + y),
					roiSize.Width);
		}

		public void Apply (Surface dst, Surface src)
		{
			Size size = new (Math.Min (dst.Width, src.Width), Math.Min (dst.Height, src.Height));
			for (int y = 0; y < size.Height; y++)
				Apply (dst.GetRowAddressUnchecked (y), src.GetRowAddressUnchecked (y), size.Width);
		}

		public void Apply (Surface dst, Surface lhs, Surface rhs) => Apply (dst, lhs, rhs, dst.Bounds);

		public void Apply (Surface dst, Surface lhs, Surface rhs, Rectangle rect)
		{
			rect.Intersect (dst.Bounds);
			rect.Intersect (lhs.Bounds);
			rect.Intersect (rhs.Bounds);
			if (!rect.IsEmpty) Apply (dst, rect.Location, lhs, rect.Location, rhs, rect.Location, rect.Size);
		}
	}

	/// <summary>A blend mode as shown in the layer properties.</summary>
	public enum LayerBlendMode
	{
		Normal = 0,
		Multiply = 1,
		Additive = 2,
		ColorBurn = 3,
		ColorDodge = 4,
		Reflect = 5,
		Glow = 6,
		Overlay = 7,
		Difference = 8,
		Negation = 9,
		Lighten = 10,
		Darken = 11,
		Screen = 12,
		Xor = 13,
	}

	/// <summary>
	/// Separable blend modes composited with source-over (W3C compositing formula), on straight alpha.
	/// The left-hand side is the bottom layer, the right-hand side the top layer.
	/// </summary>
	[Serializable]
	public abstract class UserBlendOp : BinaryPixelOp
	{
		private protected UserBlendOp (LayerBlendMode mode, int opacity)
		{
			Mode = mode;
			Opacity = Math.Clamp (opacity, 0, 255);
		}

		internal LayerBlendMode Mode { get; }
		internal int Opacity { get; }

		public override ColorBgra Apply (ColorBgra lhs, ColorBgra rhs) => BlendMath.Composite (Mode, lhs, rhs, Opacity);
		public abstract UserBlendOp CreateWithOpacity (int opacity);
		protected virtual Rendering.CompositionOp CreateCorrespondingCompositionOp (int opacity) => new Rendering.CompositionOp.Blend (Mode, opacity);
		public override string ToString () => Mode.ToString ();
	}

	public static class UserBlendOps
	{
		[Serializable] public sealed class NormalBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Normal, opacity) { public NormalBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new NormalBlendOp (opacity); }
		[Serializable] public sealed class MultiplyBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Multiply, opacity) { public MultiplyBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new MultiplyBlendOp (opacity); }
		[Serializable] public sealed class AdditiveBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Additive, opacity) { public AdditiveBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new AdditiveBlendOp (opacity); }
		[Serializable] public sealed class ColorBurnBlendOp (int opacity) : UserBlendOp (LayerBlendMode.ColorBurn, opacity) { public ColorBurnBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new ColorBurnBlendOp (opacity); }
		[Serializable] public sealed class ColorDodgeBlendOp (int opacity) : UserBlendOp (LayerBlendMode.ColorDodge, opacity) { public ColorDodgeBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new ColorDodgeBlendOp (opacity); }
		[Serializable] public sealed class ReflectBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Reflect, opacity) { public ReflectBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new ReflectBlendOp (opacity); }
		[Serializable] public sealed class GlowBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Glow, opacity) { public GlowBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new GlowBlendOp (opacity); }
		[Serializable] public sealed class OverlayBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Overlay, opacity) { public OverlayBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new OverlayBlendOp (opacity); }
		[Serializable] public sealed class DifferenceBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Difference, opacity) { public DifferenceBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new DifferenceBlendOp (opacity); }
		[Serializable] public sealed class NegationBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Negation, opacity) { public NegationBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new NegationBlendOp (opacity); }
		[Serializable] public sealed class LightenBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Lighten, opacity) { public LightenBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new LightenBlendOp (opacity); }
		[Serializable] public sealed class DarkenBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Darken, opacity) { public DarkenBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new DarkenBlendOp (opacity); }
		[Serializable] public sealed class ScreenBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Screen, opacity) { public ScreenBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new ScreenBlendOp (opacity); }
		[Serializable] public sealed class XorBlendOp (int opacity) : UserBlendOp (LayerBlendMode.Xor, opacity) { public XorBlendOp () : this (255) { } public override UserBlendOp CreateWithOpacity (int opacity) => new XorBlendOp (opacity); }

		public static UserBlendOp CreateBlendOp (LayerBlendMode mode) => FromLayerBlendMode (mode);
		public static UserBlendOp CreateDefaultBlendOp () => new NormalBlendOp ();
		public static UserBlendOp GetDefaultBlendOp () => new NormalBlendOp ();

		public static UserBlendOp FromLayerBlendMode (LayerBlendMode mode) => mode switch {
			LayerBlendMode.Multiply => new MultiplyBlendOp (),
			LayerBlendMode.Additive => new AdditiveBlendOp (),
			LayerBlendMode.ColorBurn => new ColorBurnBlendOp (),
			LayerBlendMode.ColorDodge => new ColorDodgeBlendOp (),
			LayerBlendMode.Reflect => new ReflectBlendOp (),
			LayerBlendMode.Glow => new GlowBlendOp (),
			LayerBlendMode.Overlay => new OverlayBlendOp (),
			LayerBlendMode.Difference => new DifferenceBlendOp (),
			LayerBlendMode.Negation => new NegationBlendOp (),
			LayerBlendMode.Lighten => new LightenBlendOp (),
			LayerBlendMode.Darken => new DarkenBlendOp (),
			LayerBlendMode.Screen => new ScreenBlendOp (),
			LayerBlendMode.Xor => new XorBlendOp (),
			_ => new NormalBlendOp (),
		};
	}

	public static class LayerBlendModeUtil
	{
		public static Rendering.CompositionOp CreateCompositionOp (LayerBlendMode blendMode, byte opacity = 255)
			=> new Rendering.CompositionOp.Blend (blendMode, opacity);
	}

	internal static class BlendMath
	{
		/// <summary>Blend function on 0..255 channel values: f(bottom, top).</summary>
		private static int Mix (LayerBlendMode mode, int b, int t) => mode switch {
			LayerBlendMode.Multiply => b * t / 255,
			LayerBlendMode.Additive => Math.Min (255, b + t),
			LayerBlendMode.ColorBurn => t == 0 ? 0 : Math.Max (0, 255 - (255 - b) * 255 / t),
			LayerBlendMode.ColorDodge => t == 255 ? 255 : Math.Min (255, b * 255 / (255 - t)),
			LayerBlendMode.Reflect => t == 255 ? 255 : Math.Min (255, b * b / (255 - t)),
			LayerBlendMode.Glow => b == 255 ? 255 : Math.Min (255, t * t / (255 - b)),
			LayerBlendMode.Overlay => b < 128 ? 2 * b * t / 255 : 255 - 2 * (255 - b) * (255 - t) / 255,
			LayerBlendMode.Difference => Math.Abs (b - t),
			LayerBlendMode.Negation => 255 - Math.Abs (255 - b - t),
			LayerBlendMode.Lighten => Math.Max (b, t),
			LayerBlendMode.Darken => Math.Min (b, t),
			LayerBlendMode.Screen => 255 - (255 - b) * (255 - t) / 255,
			LayerBlendMode.Xor => b ^ t,
			_ => t,
		};

		public static ColorBgra Composite (LayerBlendMode mode, ColorBgra bottom, ColorBgra top, int opacity)
		{
			int sa = top.A * opacity / 255;
			int da = bottom.A;
			int ra = sa + da - sa * da / 255;
			if (ra == 0) return default;
			int C (int cb, int cs)
			{
				int mixed = Mix (mode, cb, cs);
				// (1 - as) * ab * Cb + (1 - ab) * as * Cs + as * ab * B(Cb, Cs), divided by result alpha
				long num = (long) (255 - sa) * da * cb + (long) (255 - da) * sa * cs + (long) sa * da * mixed;
				return (int) Math.Clamp (num / (255L * ra), 0, 255);
			}
			return ColorBgra.FromBgra ((byte) C (bottom.B, top.B), (byte) C (bottom.G, top.G), (byte) C (bottom.R, top.R), (byte) ra);
		}
	}

	public static class UnaryPixelOps
	{
		[Serializable]
		public sealed class Identity : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color) => color;
		}

		[Serializable]
		public sealed class Invert : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color) => ColorBgra.FromBgra ((byte) (255 - color.B), (byte) (255 - color.G), (byte) (255 - color.R), color.A);
		}

		[Serializable]
		public sealed class Desaturate : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color)
			{
				byte i = color.GetIntensityByte ();
				return ColorBgra.FromBgra (i, i, i, color.A);
			}
		}

		[Serializable]
		public sealed class SetAlphaChannel (byte alpha) : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color) => color.NewAlpha (alpha);
		}

		[Serializable]
		public sealed class SetAlphaChannelTo255 : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color) => color.NewAlpha (255);
		}

		[Serializable]
		public sealed class Constant (ColorBgra setColor) : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color) => setColor;
		}

		/// <summary>Blends a constant color over each pixel by the constant's alpha, keeping the pixel's alpha.</summary>
		[Serializable]
		public sealed class BlendConstant (ColorBgra blendColor) : UnaryPixelOp
		{
			public override ColorBgra Apply (ColorBgra color)
			{
				int a = blendColor.A, ia = 255 - a;
				return ColorBgra.FromBgra (
					(byte) ((color.B * ia + blendColor.B * a) / 255),
					(byte) ((color.G * ia + blendColor.G * a) / 255),
					(byte) ((color.R * ia + blendColor.R * a) / 255),
					color.A);
			}
		}

		[Serializable]
		public sealed class HueSaturationLightness : UnaryPixelOp
		{
			private readonly int hue_delta;
			private readonly int sat_factor;
			private readonly UnaryPixelOp blend;

			public HueSaturationLightness (int hueDelta, int satDelta, int lightness)
			{
				hue_delta = hueDelta;
				sat_factor = satDelta * 1024 / 100;
				blend = lightness switch {
					0 => new Identity (),
					> 0 => new BlendConstant (ColorBgra.FromBgra (255, 255, 255, (byte) (lightness * 255 / 100))),
					_ => new BlendConstant (ColorBgra.FromBgra (0, 0, 0, (byte) (-lightness * 255 / 100))),
				};
			}

			public override ColorBgra Apply (ColorBgra color)
			{
				byte i = color.GetIntensityByte ();
				ColorBgra c = ColorBgra.FromBgra (
					Int32Util.ClampToByte ((i * 1024 + (color.B - i) * sat_factor) >> 10),
					Int32Util.ClampToByte ((i * 1024 + (color.G - i) * sat_factor) >> 10),
					Int32Util.ClampToByte ((i * 1024 + (color.R - i) * sat_factor) >> 10),
					color.A);
				HsvColorF hsv = HsvColorF.FromRgb (c.R, c.G, c.B);
				double hue = ((hsv.Hue + hue_delta) % 360 + 360) % 360;
				(byte r, byte g, byte b) = new HsvColorF (hue, hsv.Saturation, hsv.Value).ToRgb ();
				return blend.Apply (ColorBgra.FromBgra (b, g, r, color.A)).NewAlpha (color.A);
			}
		}

		/// <summary>Levels: maps [in_lo, in_hi] to [out_lo, out_hi] per channel with a gamma curve.</summary>
		[Serializable]
		public sealed class Level : UnaryPixelOp
		{
			private readonly byte[][] curves = new byte[3][];

			public Level (ColorBgra in_lo, ColorBgra in_hi, float[] gamma, ColorBgra out_lo, ColorBgra out_hi)
			{
				ColorInLow = in_lo;
				ColorInHigh = in_hi;
				ColorOutLow = out_lo;
				ColorOutHigh = out_hi;
				this.gamma = (float[]) gamma.Clone ();
				for (int c = 0; c < 3; c++) {
					curves[c] = new byte[256];
					for (int v = 0; v < 256; v++) {
						double range = Math.Max (1, in_hi[c] - in_lo[c]);
						double x = Math.Clamp ((v - in_lo[c]) / range, 0, 1);
						x = Math.Pow (x, gamma[c] <= 0 ? 1 : gamma[c]);
						curves[c][v] = (byte) Math.Clamp ((int) Math.Round (out_lo[c] + (out_hi[c] - out_lo[c]) * x), 0, 255);
					}
				}
			}

			private readonly float[] gamma;
			public ColorBgra ColorInLow { get; }
			public ColorBgra ColorInHigh { get; }
			public ColorBgra ColorOutLow { get; }
			public ColorBgra ColorOutHigh { get; }
			public float GetGamma (int index) => gamma[index];

			public override ColorBgra Apply (ColorBgra color)
				=> ColorBgra.FromBgra (curves[0][color.B], curves[1][color.G], curves[2][color.R], color.A);
		}
	}
}

namespace PaintDotNet.Rendering
{
	/// <summary>A layer-style composition of the right-hand side over the left-hand side.</summary>
	[Serializable]
	public abstract class CompositionOp : BinaryPixelOp
	{
		protected CompositionOp () { }

		internal sealed class Blend (LayerBlendMode mode, int opacity) : CompositionOp
		{
			public override ColorBgra Apply (ColorBgra lhs, ColorBgra rhs) => BlendMath.Composite (mode, lhs, rhs, opacity);
		}
	}
}
