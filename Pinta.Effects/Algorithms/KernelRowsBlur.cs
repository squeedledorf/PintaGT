using System;
using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Averages each pixel's neighbourhood, described as a set of horizontal kernel rows.
/// Each row covers [x - HalfWidth, x + HalfWidth] of source row y + Dy; a fractional
/// half width gives the outermost pixels partial weight. Pixels outside the image are
/// left out of the average, and colours are weighted by alpha.
/// Cost per output row is O(rows * width), whatever the half widths are.
/// </summary>
public static class KernelRowsBlur
{
	public readonly record struct KernelRow (int Dy, double Weight, double HalfWidth);

	/// <summary>A (2r+1) square, with fractional edges when the radius is fractional.</summary>
	public static KernelRow[] Square (double radius)
	{
		int whole = (int) Math.Floor (radius);
		double frac = radius - whole;
		int extent = frac > 0 ? whole + 1 : whole;
		List<KernelRow> rows = [];
		for (int dy = -extent; dy <= extent; ++dy)
			rows.Add (new (dy, Math.Abs (dy) <= whole ? 1 : frac, radius));
		return rows.ToArray ();
	}

	/// <summary>
	/// A disc. When the disc is taller than maxRows, only maxRows evenly spaced rows are
	/// sampled (each standing in for its strip), which is faster but shows banding.
	/// </summary>
	public static KernelRow[] Disc (double radius, int maxRows)
	{
		int whole = (int) Math.Floor (radius);
		List<KernelRow> rows = [];
		if (2 * whole + 1 <= maxRows) {
			for (int dy = -whole; dy <= whole; ++dy)
				rows.Add (new (dy, 1, Math.Sqrt (radius * radius - dy * dy)));
		} else {
			double strip = 2 * radius / maxRows;
			for (int k = 0; k < maxRows; ++k) {
				double c = -radius + (k + 0.5) * strip;
				rows.Add (new (
					(int) Math.Round (c),
					strip,
					Math.Sqrt (Math.Max (0, radius * radius - c * c))));
			}
		}
		return rows.ToArray ();
	}

	public static void Render (
		ImageSurface src,
		ImageSurface dst,
		RectangleI rect,
		ReadOnlySpan<KernelRow> rows,
		GammaBoost gamma)
	{
		int width = src.Width;
		int height = src.Height;
		ReadOnlySpan<ColorBgra> src_data = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dst_data = dst.GetPixelData ();

		int maxHalf = 0;
		foreach (var row in rows)
			maxHalf = Math.Max (maxHalf, (int) Math.Ceiling (row.HalfWidth));

		// Prefix sums cover the source columns [x0, x1].
		int x0 = Math.Max (0, rect.Left - maxHalf);
		int x1 = Math.Min (width - 1, rect.Right + maxHalf);
		int span = x1 - x0 + 1;

		// Per source column of the current kernel row: alpha and alpha-weighted (gamma space) colour.
		double[] pa = new double[span + 1], pb = new double[span + 1], pg = new double[span + 1], pr = new double[span + 1];
		double[] ca = new double[span], cb = new double[span], cg = new double[span], cr = new double[span];

		int outWidth = rect.Width;
		double[] sw = new double[outWidth], sa = new double[outWidth], sb = new double[outWidth], sg = new double[outWidth], sr = new double[outWidth];

		for (int y = rect.Top; y <= rect.Bottom; ++y) {
			Array.Clear (sw);
			Array.Clear (sa);
			Array.Clear (sb);
			Array.Clear (sg);
			Array.Clear (sr);

			foreach (var row in rows) {
				int sy = y + row.Dy;
				if (sy < 0 || sy >= height)
					continue;

				var src_row = src_data.Slice (sy * width + x0, span);
				for (int i = 0; i < span; ++i) {
					ColorBgra c = src_row[i].ToStraightAlpha ();
					double a = c.A;
					ca[i] = a;
					cb[i] = gamma[c.B] * a;
					cg[i] = gamma[c.G] * a;
					cr[i] = gamma[c.R] * a;
					pa[i + 1] = pa[i] + ca[i];
					pb[i + 1] = pb[i] + cb[i];
					pg[i + 1] = pg[i] + cg[i];
					pr[i + 1] = pr[i] + cr[i];
				}

				int whole = (int) Math.Floor (row.HalfWidth);
				double frac = row.HalfWidth - whole;
				double wy = row.Weight;

				for (int x = rect.Left; x <= rect.Right; ++x) {
					int o = x - rect.Left;
					int lo = Math.Max (x - whole, 0) - x0;
					int hi = Math.Min (x + whole, width - 1) - x0;
					sw[o] += wy * (hi - lo + 1);
					sa[o] += wy * (pa[hi + 1] - pa[lo]);
					sb[o] += wy * (pb[hi + 1] - pb[lo]);
					sg[o] += wy * (pg[hi + 1] - pg[lo]);
					sr[o] += wy * (pr[hi + 1] - pr[lo]);

					if (frac <= 0)
						continue;

					double wf = wy * frac;
					int l = x - whole - 1;
					if (l >= 0) {
						int i = l - x0;
						sw[o] += wf;
						sa[o] += wf * ca[i];
						sb[o] += wf * cb[i];
						sg[o] += wf * cg[i];
						sr[o] += wf * cr[i];
					}
					int r = x + whole + 1;
					if (r < width) {
						int i = r - x0;
						sw[o] += wf;
						sa[o] += wf * ca[i];
						sb[o] += wf * cb[i];
						sg[o] += wf * cg[i];
						sr[o] += wf * cr[i];
					}
				}
			}

			var dst_row = dst_data.Slice (y * width, width);
			for (int x = rect.Left; x <= rect.Right; ++x) {
				int o = x - rect.Left;
				if (sa[o] <= 0 || sw[o] <= 0) {
					dst_row[x] = ColorBgra.Transparent;
					continue;
				}
				dst_row[x] = ColorBgra.FromBgra (
					gamma.Inverse (sb[o] / sa[o]),
					gamma.Inverse (sg[o] / sa[o]),
					gamma.Inverse (sr[o] / sa[o]),
					(byte) Math.Clamp (Math.Round (sa[o] / sw[o]), 0, 255)).ToPremultipliedAlpha ();
			}
		}
	}
}
