using System;
using System.Threading.Tasks;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Separable image resampling for Image > Resize, with the Paint.NET resampling modes.
/// Works on premultiplied pixels, optionally in linear light ("Use gamma correction").
/// </summary>
public static class Resampler
{
	public static ImageSurface Resize (ImageSurface source, Size newSize, ResamplingMode mode, bool gammaCorrection)
	{
		int srcW = source.Width;
		int srcH = source.Height;
		int dstW = newSize.Width;
		int dstH = newSize.Height;

		ImageSurface dest = CairoExtensions.CreateImageSurface (Format.Argb32, dstW, dstH);
		ReadOnlySpan<ColorBgra> srcPixels = source.GetReadOnlyPixelData ();

		if (mode == ResamplingMode.NearestNeighbor) {
			Span<ColorBgra> dst = dest.GetPixelData ();
			for (int y = 0; y < dstH; y++) {
				int sy = Math.Min (srcH - 1, (int) ((y + 0.5) * srcH / dstH));
				for (int x = 0; x < dstW; x++) {
					int sx = Math.Min (srcW - 1, (int) ((x + 0.5) * srcW / dstW));
					dst[y * dstW + x] = srcPixels[sy * srcW + sx];
				}
			}
			dest.MarkDirty ();
			return dest;
		}

		float[] src = ToFloats (srcPixels, gammaCorrection);

		Filter hFilter = ChooseFilter (mode, srcW, dstW);
		Filter vFilter = ChooseFilter (mode, srcH, dstH);
		Contributions columns = ComputeContributions (srcW, dstW, hFilter);
		Contributions rows = ComputeContributions (srcH, dstH, vFilter);

		// Horizontal pass: srcH rows of dstW pixels.
		float[] mid = new float[srcH * dstW * 4];
		Parallel.For (0, srcH, y => {
			int srcRow = y * srcW * 4;
			int midRow = y * dstW * 4;
			for (int x = 0; x < dstW; x++) {
				float b = 0, g = 0, r = 0, a = 0;
				int start = columns.Start[x];
				int offset = x * columns.MaxTaps;
				for (int t = 0; t < columns.Count[x]; t++) {
					float w = columns.Weights[offset + t];
					int i = srcRow + (start + t) * 4;
					b += src[i] * w;
					g += src[i + 1] * w;
					r += src[i + 2] * w;
					a += src[i + 3] * w;
				}
				int o = midRow + x * 4;
				mid[o] = b;
				mid[o + 1] = g;
				mid[o + 2] = r;
				mid[o + 3] = a;
			}
		});

		// Vertical pass straight into the destination.
		ColorBgra[] result = new ColorBgra[dstW * dstH];
		Parallel.For (0, dstH, y => {
			int start = rows.Start[y];
			int offset = y * rows.MaxTaps;
			for (int x = 0; x < dstW; x++) {
				float b = 0, g = 0, r = 0, a = 0;
				for (int t = 0; t < rows.Count[y]; t++) {
					float w = rows.Weights[offset + t];
					int i = ((start + t) * dstW + x) * 4;
					b += mid[i] * w;
					g += mid[i + 1] * w;
					r += mid[i + 2] * w;
					a += mid[i + 3] * w;
				}
				result[y * dstW + x] = ToColor (b, g, r, a, gammaCorrection);
			}
		});

		result.CopyTo (dest.GetPixelData ());
		dest.MarkDirty ();
		return dest;
	}

	private delegate double Kernel (double x);

	private readonly record struct Filter (Kernel Kernel, double Support, bool WidensWhenShrinking);

	private static Filter ChooseFilter (ResamplingMode mode, int srcLength, int dstLength)
	{
		bool enlarging = dstLength >= srcLength;
		return mode switch {
			ResamplingMode.Bicubic => new (x => Cubic (x, 0, 0.5), 2, true),
			ResamplingMode.BicubicSmooth => new (x => Cubic (x, 1, 0), 2, true),
			ResamplingMode.Bilinear => new (Triangle, 1, true),
			// Only the nearest 2x2 source pixels, however far the image shrinks.
			ResamplingMode.BilinearLowQuality => new (Triangle, 1, false),
			ResamplingMode.Lanczos => new (Lanczos3, 3, true),
			// An area average when shrinking; Bilinear when enlarging.
			ResamplingMode.Fant => enlarging ? new (Triangle, 1, true) : new (Box, 0.5, true),
			// ponytail: Adaptive picks Bicubic to enlarge and Lanczos to shrink; tune by eye against PDN if it reads soft.
			ResamplingMode.AdaptiveSharp => enlarging ? new (x => Cubic (x, 0, 0.5), 2, true) : new (Lanczos3, 3, true),
			_ => throw new ArgumentOutOfRangeException (nameof (mode)),
		};
	}

	/// <summary>Mitchell–Netravali cubic family; (0, 0.5) is Catmull-Rom, (1, 0) the B-spline.</summary>
	private static double Cubic (double x, double b, double c)
	{
		x = Math.Abs (x);
		if (x < 1)
			return ((12 - 9 * b - 6 * c) * x * x * x + (-18 + 12 * b + 6 * c) * x * x + (6 - 2 * b)) / 6;
		if (x < 2)
			return ((-b - 6 * c) * x * x * x + (6 * b + 30 * c) * x * x + (-12 * b - 48 * c) * x + (8 * b + 24 * c)) / 6;
		return 0;
	}

	private static double Triangle (double x)
		=> Math.Max (0, 1 - Math.Abs (x));

	private static double Box (double x)
		=> Math.Abs (x) <= 0.5 ? 1 : 0;

	private static double Lanczos3 (double x)
	{
		x = Math.Abs (x);
		if (x < 1e-8)
			return 1;
		if (x >= 3)
			return 0;
		double px = Math.PI * x;
		return 3 * Math.Sin (px) * Math.Sin (px / 3) / (px * px);
	}

	private sealed record Contributions (int[] Start, int[] Count, float[] Weights, int MaxTaps);

	private static Contributions ComputeContributions (int srcLength, int dstLength, Filter filter)
	{
		double scale = (double) srcLength / dstLength;
		double filterScale = filter.WidensWhenShrinking ? Math.Max (1, scale) : 1;
		double support = filter.Support * filterScale;
		int maxTaps = (int) Math.Ceiling (support) * 2 + 1;

		int[] start = new int[dstLength];
		int[] count = new int[dstLength];
		float[] weights = new float[dstLength * maxTaps];

		for (int d = 0; d < dstLength; d++) {
			double center = (d + 0.5) * scale - 0.5;
			int left = Math.Max (0, (int) Math.Ceiling (center - support));
			int right = Math.Min (srcLength - 1, (int) Math.Floor (center + support));
			if (right < left) // A tiny box filter can fall between two source pixels.
				left = right = Math.Clamp ((int) Math.Round (center), 0, srcLength - 1);
			right = Math.Min (right, left + maxTaps - 1);

			double total = 0;
			for (int i = left; i <= right; i++) {
				double w = filter.Kernel ((i - center) / filterScale);
				weights[d * maxTaps + i - left] = (float) w;
				total += w;
			}

			if (Math.Abs (total) < 1e-12) {
				// No source pixel has weight (a box between pixels): take the nearest one.
				int nearest = Math.Clamp ((int) Math.Round (center), left, right);
				Array.Clear (weights, d * maxTaps, maxTaps);
				weights[d * maxTaps + nearest - left] = 1;
			} else {
				for (int i = left; i <= right; i++)
					weights[d * maxTaps + i - left] /= (float) total;
			}

			start[d] = left;
			count[d] = right - left + 1;
		}

		return new (start, count, weights, maxTaps);
	}

	private static readonly float[] srgb_to_linear = CreateSrgbToLinear ();

	private static float[] CreateSrgbToLinear ()
	{
		float[] table = new float[256];
		for (int i = 0; i < 256; i++) {
			double c = i / 255.0;
			table[i] = (float) (c <= 0.04045 ? c / 12.92 : Math.Pow ((c + 0.055) / 1.055, 2.4));
		}
		return table;
	}

	private static float LinearToSrgb (float c)
		=> (float) (c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow (c, 1 / 2.4) - 0.055);

	/// <summary>Premultiplied pixels as floats in 0..1, converted to linear light when asked.</summary>
	private static float[] ToFloats (ReadOnlySpan<ColorBgra> pixels, bool linear)
	{
		float[] result = new float[pixels.Length * 4];
		for (int i = 0; i < pixels.Length; i++) {
			ColorBgra p = pixels[i];
			float a = p.A / 255f;
			int o = i * 4;
			result[o + 3] = a;
			if (!linear) {
				result[o] = p.B / 255f;
				result[o + 1] = p.G / 255f;
				result[o + 2] = p.R / 255f;
			} else if (p.A > 0) {
				result[o] = srgb_to_linear[Math.Min (255, p.B * 255 / p.A)] * a;
				result[o + 1] = srgb_to_linear[Math.Min (255, p.G * 255 / p.A)] * a;
				result[o + 2] = srgb_to_linear[Math.Min (255, p.R * 255 / p.A)] * a;
			}
		}
		return result;
	}

	private static ColorBgra ToColor (float b, float g, float r, float a, bool linear)
	{
		a = Math.Clamp (a, 0, 1);
		if (a <= 0)
			return ColorBgra.Transparent;

		// Sharpening kernels overshoot; keep the colour within the alpha (premultiplied range).
		b = Math.Clamp (b, 0, a);
		g = Math.Clamp (g, 0, a);
		r = Math.Clamp (r, 0, a);

		if (linear) {
			b = LinearToSrgb (b / a) * a;
			g = LinearToSrgb (g / a) * a;
			r = LinearToSrgb (r / a) * a;
		}

		return ColorBgra.FromBgra (
			(byte) Math.Round (b * 255),
			(byte) Math.Round (g * 255),
			(byte) Math.Round (r * 255),
			(byte) Math.Round (a * 255));
	}
}
