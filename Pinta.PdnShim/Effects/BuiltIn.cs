using System;
using System.Drawing;
using PaintDotNet.PropertySystem;

// Paint.NET's built-in effects, which some plugins create and run on their own surfaces
// (for example a plugin that blurs, then recolors). These are Pinta's own implementations:
// the property names are the ones plugins pass, the algorithms are standard ones, so results
// look like Paint.NET's but are not pixel-identical.
namespace PaintDotNet.Effects
{
	/// <summary>Base for the built-ins: no icon, no menu, and a simple per-ROI render.</summary>
	public abstract class BuiltInEffect : PropertyBasedEffect
	{
		private protected BuiltInEffect (string name, string subMenu) : base (name, null, subMenu, EffectFlags.Configurable) { }

		protected sealed override void OnRender (Rectangle[] renderRects, int startIndex, int length)
		{
			Surface src = SrcArgs.Surface, dst = DstArgs.Surface;
			for (int i = startIndex; i < startIndex + length; i++) {
				Rectangle roi = Rectangle.Intersect (renderRects[i], Rectangle.Intersect (src.Bounds, dst.Bounds));
				if (!roi.IsEmpty && !IsCancelRequested)
					RenderRoi (src, dst, roi);
			}
		}

		private protected abstract void RenderRoi (Surface src, Surface dst, Rectangle roi);

		private protected int Int (object name) => Token.GetProperty<Int32Property> (name).Value;
		private protected double Double (object name) => Token.GetProperty<DoubleProperty> (name).Value;

		/// <summary>
		/// Separable convolution with alpha weighting: vertical sums for each needed column of the row,
		/// then a horizontal pass. Works per ROI, reading only source pixels.
		/// </summary>
		private protected static unsafe void Convolve (Surface src, Surface dst, Rectangle roi, int[] kernel)
		{
			int r = kernel.Length / 2;
			int x0 = Math.Max (0, roi.Left - r), x1 = Math.Min (src.Width, roi.Right + r);
			int span = x1 - x0;
			long[] sa = new long[span], sb = new long[span], sg = new long[span], sr = new long[span], sw = new long[span];
			for (int y = roi.Top; y < roi.Bottom; y++) {
				Array.Clear (sa); Array.Clear (sb); Array.Clear (sg); Array.Clear (sr); Array.Clear (sw);
				for (int k = -r; k <= r; k++) {
					int yy = y + k;
					if (yy < 0 || yy >= src.Height) continue;
					int w = kernel[k + r];
					ColorBgra* row = src.GetRowAddressUnchecked (yy);
					for (int i = 0; i < span; i++) {
						ColorBgra c = row[x0 + i];
						long wa = (long) w * c.A;
						sa[i] += wa; sb[i] += wa * c.B; sg[i] += wa * c.G; sr[i] += wa * c.R; sw[i] += w;
					}
				}
				ColorBgra* outRow = dst.GetRowAddressUnchecked (y);
				for (int x = roi.Left; x < roi.Right; x++) {
					long a = 0, b = 0, g = 0, rr = 0, wsum = 0;
					for (int k = -r; k <= r; k++) {
						int i = x + k - x0;
						if (i < 0 || i >= span) continue;
						int w = kernel[k + r];
						a += w * sa[i]; b += w * sb[i]; g += w * sg[i]; rr += w * sr[i]; wsum += w * sw[i];
					}
					outRow[x] = a == 0 || wsum == 0
						? ColorBgra.FromBgra (0, 0, 0, 0)
						: ColorBgra.FromBgra ((byte) (b / a), (byte) (g / a), (byte) (rr / a), (byte) Math.Min (255, a / wsum));
				}
			}
		}

		private protected static int[] GaussianKernel (int radius)
		{
			if (radius <= 0) return [1];
			double sigma = Math.Max (0.5, radius / 2.0);
			int[] k = new int[radius * 2 + 1];
			for (int i = -radius; i <= radius; i++)
				k[i + radius] = Math.Max (1, (int) Math.Round (1024 * Math.Exp (-(i * i) / (2 * sigma * sigma))));
			return k;
		}

		private protected static int[] BoxKernel (int radius)
		{
			int[] k = new int[Math.Max (0, radius) * 2 + 1];
			Array.Fill (k, 1);
			return k;
		}
	}

	public sealed class GaussianBlurEffect : BuiltInEffect
	{
		public enum PropertyNames { Radius = 0 }
		public GaussianBlurEffect () : base ("Gaussian Blur", SubmenuNames.Blurs) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Radius, 2, 0, 200)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int r = Int (PropertyNames.Radius);
			if (r == 0) dst.CopySurface (src, roi.Location, roi);
			else Convolve (src, dst, roi, GaussianKernel (r));
		}
	}

	public sealed class UnfocusEffect : BuiltInEffect
	{
		public enum PropertyNames { Radius = 0 }
		public UnfocusEffect () : base ("Unfocus", SubmenuNames.Blurs) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Radius, 4, 1, 200)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi) => Convolve (src, dst, roi, BoxKernel (Int (PropertyNames.Radius)));
	}

	public sealed class SharpenEffect : BuiltInEffect
	{
		public enum PropertyNames { Amount = 0 }
		public SharpenEffect () : base ("Sharpen", SubmenuNames.Photo) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Amount, 2, 1, 20)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int amount = Int (PropertyNames.Amount);
			Convolve (src, dst, roi, GaussianKernel (amount));
			double k = amount / 10.0 + 0.5;
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					ColorBgra s = src[x, y], b = dst[x, y];
					dst[x, y] = ColorBgra.FromBgraClamped (
						(int) (s.B + k * (s.B - b.B)), (int) (s.G + k * (s.G - b.G)), (int) (s.R + k * (s.R - b.R)), s.A);
				}
		}
	}

	public sealed class PixelateEffect : BuiltInEffect
	{
		public enum PropertyNames { CellSize = 0 }
		public PixelateEffect () : base ("Pixelate", SubmenuNames.Distort) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.CellSize, 2, 1, 100)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int size = Int (PropertyNames.CellSize);
			for (int cy = roi.Top / size * size; cy < roi.Bottom; cy += size)
				for (int cx = roi.Left / size * size; cx < roi.Right; cx += size) {
					Rectangle cell = Rectangle.Intersect (new Rectangle (cx, cy, size, size), src.Bounds);
					long a = 0, b = 0, g = 0, r = 0;
					for (int y = cell.Top; y < cell.Bottom; y++)
						for (int x = cell.Left; x < cell.Right; x++) {
							ColorBgra c = src[x, y];
							a += c.A; b += c.B * c.A; g += c.G * c.A; r += c.R * c.A;
						}
					ColorBgra avg = a == 0 ? default : ColorBgra.FromBgra ((byte) (b / a), (byte) (g / a), (byte) (r / a), (byte) (a / (cell.Width * cell.Height)));
					dst.Clear (Rectangle.Intersect (cell, roi), avg);
				}
		}
	}

	public sealed class EdgeDetectEffect : BuiltInEffect
	{
		public enum PropertyNames { Angle = 0 }
		public EdgeDetectEffect () : base ("Edge Detect", SubmenuNames.Stylize) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new DoubleProperty (PropertyNames.Angle, 45, -180, 180)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			double t = Double (PropertyNames.Angle) * Math.PI / 180;
			double dx = Math.Cos (t), dy = Math.Sin (t);
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					ColorBgra a = src.GetBilinearSampleClamped ((float) (x - dx), (float) (y - dy));
					ColorBgra b = src.GetBilinearSampleClamped ((float) (x + dx), (float) (y + dy));
					dst[x, y] = ColorBgra.FromBgraClamped (128 + b.B - a.B, 128 + b.G - a.G, 128 + b.R - a.R, src[x, y].A);
				}
		}
	}

	public sealed class MedianEffect : BuiltInEffect
	{
		public enum PropertyNames { Radius = 0, Percentile = 1 }
		public MedianEffect () : base ("Median", SubmenuNames.Noise) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Radius, 10, 1, 200), new Int32Property (PropertyNames.Percentile, 50, 0, 100)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int r = Int (PropertyNames.Radius), pct = Int (PropertyNames.Percentile);
			int[][] hist = [new int[256], new int[256], new int[256], new int[256]];
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					foreach (int[] h in hist) Array.Clear (h);
					int n = 0;
					for (int v = Math.Max (0, y - r); v <= Math.Min (src.Height - 1, y + r); v++)
						for (int u = Math.Max (0, x - r); u <= Math.Min (src.Width - 1, x + r); u++) {
							ColorBgra c = src[u, v];
							hist[0][c.B]++; hist[1][c.G]++; hist[2][c.R]++; hist[3][c.A]++; n++;
						}
					int target = Math.Max (1, n * pct / 100);
					byte Pick (int[] h) { int s = 0; for (int i = 0; i < 256; i++) { s += h[i]; if (s >= target) return (byte) i; } return 255; }
					dst[x, y] = ColorBgra.FromBgra (Pick (hist[0]), Pick (hist[1]), Pick (hist[2]), Pick (hist[3]));
				}
		}
	}

	public sealed class FrostedGlassEffect : BuiltInEffect
	{
		public enum PropertyNames { Amount = 0 }
		public FrostedGlassEffect () : base ("Frosted Glass", SubmenuNames.Distort) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new DoubleProperty (PropertyNames.Amount, 10, 0, 200)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			double amount = Double (PropertyNames.Amount);
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					uint h = (uint) (x * 73856093 ^ y * 19349663);
					h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
					double ox = ((h & 0xffff) / 65535.0 * 2 - 1) * amount, oy = ((h >> 16) / 65535.0 * 2 - 1) * amount;
					dst[x, y] = src.GetBilinearSampleClamped ((float) (x + ox), (float) (y + oy));
				}
		}
	}

	public sealed class OilPaintingEffect : BuiltInEffect
	{
		public enum PropertyNames { BrushSize = 0, Coarseness = 1 }
		public OilPaintingEffect () : base ("Oil Painting", SubmenuNames.Artistic) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.BrushSize, 3, 1, 8), new Int32Property (PropertyNames.Coarseness, 50, 3, 255)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int r = Int (PropertyNames.BrushSize), levels = Int (PropertyNames.Coarseness);
			int[] count = new int[levels];
			long[] sb = new long[levels], sg = new long[levels], sr = new long[levels], sa = new long[levels];
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					Array.Clear (count); Array.Clear (sb); Array.Clear (sg); Array.Clear (sr); Array.Clear (sa);
					for (int v = Math.Max (0, y - r); v <= Math.Min (src.Height - 1, y + r); v++)
						for (int u = Math.Max (0, x - r); u <= Math.Min (src.Width - 1, x + r); u++) {
							ColorBgra c = src[u, v];
							int bin = c.GetIntensityByte () * (levels - 1) / 255;
							count[bin]++; sb[bin] += c.B; sg[bin] += c.G; sr[bin] += c.R; sa[bin] += c.A;
						}
					int best = 0;
					for (int i = 1; i < levels; i++) if (count[i] > count[best]) best = i;
					int n = Math.Max (1, count[best]);
					dst[x, y] = ColorBgra.FromBgra ((byte) (sb[best] / n), (byte) (sg[best] / n), (byte) (sr[best] / n), (byte) (sa[best] / n));
				}
		}
	}

	public sealed class PencilSketchEffect : BuiltInEffect
	{
		public enum PropertyNames { PencilTipSize = 0, ColorRange = 1 }
		public PencilSketchEffect () : base ("Pencil Sketch", SubmenuNames.Artistic) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.PencilTipSize, 2, 1, 20), new Int32Property (PropertyNames.ColorRange, 0, -20, 20)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			Convolve (src, dst, roi, GaussianKernel (Int (PropertyNames.PencilTipSize)));
			int range = Int (PropertyNames.ColorRange);
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					int s = src[x, y].GetIntensityByte ();
					int b = 255 - dst[x, y].GetIntensityByte ();
					int dodge = b >= 255 ? 255 : Math.Min (255, s * 255 / (255 - b));
					int v = Math.Clamp (dodge + range * 5, 0, 255);
					dst[x, y] = ColorBgra.FromBgra ((byte) v, (byte) v, (byte) v, src[x, y].A);
				}
		}
	}

	[EffectCategory (EffectCategory.Adjustment)]
	public sealed class BrightnessAndContrastAdjustment : BuiltInEffect
	{
		public enum PropertyNames { Brightness = 0, Contrast = 1 }
		public BrightnessAndContrastAdjustment () : base ("Brightness / Contrast", null) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Brightness, 0, -100, 100), new Int32Property (PropertyNames.Contrast, 0, -100, 100)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int brightness = Int (PropertyNames.Brightness), contrast = Int (PropertyNames.Contrast);
			double k = contrast < 0 ? (100 + contrast) / 100.0 : 100.0 / Math.Max (1, 100 - contrast);
			byte[] lut = new byte[256];
			for (int i = 0; i < 256; i++)
				lut[i] = (byte) Math.Clamp ((int) Math.Round ((i - 127.5) * k + 127.5 + brightness * 255 / 100.0), 0, 255);
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					ColorBgra c = src[x, y];
					dst[x, y] = ColorBgra.FromBgra (lut[c.B], lut[c.G], lut[c.R], c.A);
				}
		}
	}

	[EffectCategory (EffectCategory.Adjustment)]
	public sealed class HueAndSaturationAdjustment : BuiltInEffect
	{
		public enum PropertyNames { Hue = 0, Saturation = 1, Lightness = 2 }
		public HueAndSaturationAdjustment () : base ("Hue / Saturation", null) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Hue, 0, -180, 180), new Int32Property (PropertyNames.Saturation, 100, 0, 200), new Int32Property (PropertyNames.Lightness, 0, -100, 100)]);
		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			UnaryPixelOps.HueSaturationLightness op = new (Int (PropertyNames.Hue), Int (PropertyNames.Saturation) - 100, Int (PropertyNames.Lightness));
			op.Apply (dst, src, roi);
		}
	}

	public sealed class CloudsEffect : BuiltInEffect
	{
		public enum PropertyNames { Scale = 0, Power = 1, Seed = 2 }
		public CloudsEffect () : base ("Clouds", SubmenuNames.Render) { }
		protected override PropertyCollection OnCreatePropertyCollection () => new ([new Int32Property (PropertyNames.Scale, 250, 2, 1000), new DoubleProperty (PropertyNames.Power, 0.5, 0, 1), new Int32Property (PropertyNames.Seed, 0, 0, int.MaxValue)]);

		private protected override void RenderRoi (Surface src, Surface dst, Rectangle roi)
		{
			int scale = Int (PropertyNames.Scale), seed = Int (PropertyNames.Seed);
			double power = Double (PropertyNames.Power);
			ColorBgra c1 = EnvironmentParameters.PrimaryColor, c2 = EnvironmentParameters.SecondaryColor;
			for (int y = roi.Top; y < roi.Bottom; y++)
				for (int x = roi.Left; x < roi.Right; x++) {
					double v = 0, amp = 1, total = 0, freq = 1.0 / scale;
					for (int octave = 0; octave < 8; octave++) {
						v += amp * Noise (x * freq, y * freq, seed + octave);
						total += amp;
						amp *= power;
						freq *= 2;
					}
					double t = Math.Clamp ((v / total + 1) / 2, 0, 1);
					dst[x, y] = ColorBgra.Lerp (c1, c2, t);
				}
		}

		private static double Noise (double x, double y, int seed)
		{
			int x0 = (int) Math.Floor (x), y0 = (int) Math.Floor (y);
			double fx = x - x0, fy = y - y0;
			double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
			double n00 = Hash (x0, y0, seed), n10 = Hash (x0 + 1, y0, seed), n01 = Hash (x0, y0 + 1, seed), n11 = Hash (x0 + 1, y0 + 1, seed);
			return (n00 + (n10 - n00) * sx) + ((n01 + (n11 - n01) * sx) - (n00 + (n10 - n00) * sx)) * sy;
		}

		private static double Hash (int x, int y, int seed)
		{
			uint h = (uint) (x * 374761393 + y * 668265263 + seed * 144665);
			h = (h ^ (h >> 13)) * 1274126177;
			return ((h ^ (h >> 16)) & 0xffff) / 32767.5 - 1;
		}
	}
}
