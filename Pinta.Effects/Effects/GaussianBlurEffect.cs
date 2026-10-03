/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class GaussianBlurEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsBlursGaussianBlur;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Gaussian Blur");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Blurs");

	public GaussianBlurData Data => (GaussianBlurData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public GaussianBlurEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new GaussianBlurData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	#region Algorithm Code Ported From PDN

	public static ImmutableArray<int> CreateGaussianBlurRow (int amount)
	{
		int size = 1 + (amount * 2);
		var weights = ImmutableArray.CreateBuilder<int> (size);
		weights.Count = size;

		for (int i = 0; i <= amount; ++i) {
			// 1 + aa - aa + 2ai - ii
			weights[i] = 16 * (i + 1);
			weights[size - i - 1] = weights[i];
		}

		return weights.MoveToImmutable ();
	}

	/// <summary>
	/// Below the top quality, only source pixels on a coarser grid (every n-th row and column)
	/// are sampled: faster, and the result is a smooth but less detailed blur, like blurring a
	/// downscaled copy. The grid is fixed to the image rather than to the kernel, so it cannot
	/// show up as a repeating pattern. At least four grid steps fit in the radius.
	/// </summary>
	public static int QualityStride (int radius, int quality)
		=> Math.Max (1, Math.Min (MaxQuality + 1 - quality, radius / 4));

	public const int MaxQuality = 4;

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.Radius == 0)
			return; // Copy src to dest

		int r = Data.Radius;
		ImmutableArray<int> w = CreateGaussianBlurRow (r);
		int stride = QualityStride (r, Data.Quality);
		int wlen = w.Length;
		GammaBoost gamma = new (Data.GammaBoost);

		// Without a boost, truncate exactly like the original integer code did,
		// since Glow, Pencil Sketch and Soften Portrait build on this output.
		byte ToChannel (double value)
			=> gamma.IsIdentity ? (byte) Math.Clamp ((long) value, 0, 255) : gamma.Inverse (value);

		Span<long> waSums = stackalloc long[wlen];
		Span<long> wcSums = stackalloc long[wlen];
		Span<long> aSums = stackalloc long[wlen];
		Span<double> bSums = stackalloc double[wlen];
		Span<double> gSums = stackalloc double[wlen];
		Span<double> rSums = stackalloc double[wlen];

		// Cache these for a massive performance boost
		int src_width = src.Width;
		int src_height = src.Height;
		ReadOnlySpan<ColorBgra> src_data = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dst_data = dest.GetPixelData ();

		foreach (var rect in rois) {

			if (rect.Height < 1 || rect.Width < 1)
				continue;

			for (int y = rect.Top; y <= rect.Bottom; ++y) {
				long waSum = 0;
				long wcSum = 0;
				long aSum = 0;
				double bSum = 0;
				double gSum = 0;
				double rSum = 0;

				var dst_row = dst_data.Slice (y * src_width, src_width);

				for (int wx = 0; wx < wlen; ++wx) {
					int srcX = rect.Left + wx - r;
					waSums[wx] = 0;
					wcSums[wx] = 0;
					aSums[wx] = 0;
					bSums[wx] = 0;
					gSums[wx] = 0;
					rSums[wx] = 0;

					if (srcX < 0 || srcX >= src_width || srcX % stride != 0)
						continue;

					for (int wy = 0; wy < wlen; ++wy) {
						int srcY = y + wy - r;

						if (srcY < 0 || srcY >= src_height || srcY % stride != 0)
							continue;

						PointI pixelPosition = new (srcX, srcY);

						ColorBgra c = src.GetColorBgra (src_data, src_width, pixelPosition).ToStraightAlpha ();
						int wp = w[wy];

						waSums[wx] += wp;
						wp *= c.A + (c.A >> 7);
						wcSums[wx] += wp;
						wp >>= 8;

						if (c.A > 0) {
							aSums[wx] += wp * c.A;
							bSums[wx] += wp * gamma[c.B];
							gSums[wx] += wp * gamma[c.G];
							rSums[wx] += wp * gamma[c.R];
						}
					}

					int wwx = w[wx];
					waSum += wwx * waSums[wx];
					wcSum += wwx * wcSums[wx];
					aSum += wwx * aSums[wx];
					bSum += wwx * bSums[wx];
					gSum += wwx * gSums[wx];
					rSum += wwx * rSums[wx];
				}

				wcSum >>= 8;

				if (waSum == 0 || wcSum == 0) {
					dst_row[rect.Left] = ColorBgra.Zero;
				} else {
					byte alpha = (byte) (aSum / waSum);
					byte blue = ToChannel (bSum / wcSum);
					byte green = ToChannel (gSum / wcSum);
					byte red = ToChannel (rSum / wcSum);

					dst_row[rect.Left] = ColorBgra.FromBgra (blue, green, red, alpha).ToPremultipliedAlpha ();
				}

				for (int x = rect.Left + 1; x <= rect.Right; ++x) {
					for (int i = 0; i < wlen - 1; ++i) {
						waSums[i] = waSums[i + 1];
						wcSums[i] = wcSums[i + 1];
						aSums[i] = aSums[i + 1];
						bSums[i] = bSums[i + 1];
						gSums[i] = gSums[i + 1];
						rSums[i] = rSums[i + 1];
					}

					waSum = 0;
					wcSum = 0;
					aSum = 0;
					bSum = 0;
					gSum = 0;
					rSum = 0;

					int wx;
					for (wx = 0; wx < wlen - 1; ++wx) {
						long wwx = w[wx];
						waSum += wwx * waSums[wx];
						wcSum += wwx * wcSums[wx];
						aSum += wwx * aSums[wx];
						bSum += wwx * bSums[wx];
						gSum += wwx * gSums[wx];
						rSum += wwx * rSums[wx];
					}

					wx = wlen - 1;

					waSums[wx] = 0;
					wcSums[wx] = 0;
					aSums[wx] = 0;
					bSums[wx] = 0;
					gSums[wx] = 0;
					rSums[wx] = 0;

					int srcX = x + wx - r;

					if (srcX >= 0 && srcX < src_width && srcX % stride == 0) {
						for (int wy = 0; wy < wlen; ++wy) {
							int srcY = y + wy - r;

							if (srcY < 0 || srcY >= src_height || srcY % stride != 0)
								continue;

							ColorBgra c = src.GetColorBgra (src_data, src_width, new (srcX, srcY)).ToStraightAlpha ();
							int wp = w[wy];

							waSums[wx] += wp;
							wp *= c.A + (c.A >> 7);
							wcSums[wx] += wp;
							wp >>= 8;

							if (c.A > 0) {
								aSums[wx] += wp * (long) c.A;
								bSums[wx] += wp * gamma[c.B];
								gSums[wx] += wp * gamma[c.G];
								rSums[wx] += wp * gamma[c.R];
							}
						}

						int wr = w[wx];
						waSum += wr * waSums[wx];
						wcSum += wr * wcSums[wx];
						aSum += wr * aSums[wx];
						bSum += wr * bSums[wx];
						gSum += wr * gSums[wx];
						rSum += wr * rSums[wx];
					}

					wcSum >>= 8;

					if (waSum == 0 || wcSum == 0) {
						dst_row[x] = ColorBgra.Zero;
					} else {
						byte alpha = (byte) (aSum / waSum);
						byte blue = ToChannel (bSum / wcSum);
						byte green = ToChannel (gSum / wcSum);
						byte red = ToChannel (rSum / wcSum);

						dst_row[x] = ColorBgra.FromBgra (blue, green, red, alpha).ToPremultipliedAlpha ();
					}
				}
			}
		}
	}
	#endregion

	public sealed class GaussianBlurData : EffectData
	{
		[Caption ("Radius")]
		[MinimumValue (0), MaximumValue (200)]
		public int Radius { get; set; } = 2;

		[Caption ("Gamma Boost")]
		[MinimumValue ((int) Effects.GammaBoost.Min), MaximumValue ((int) Effects.GammaBoost.Max)]
		public double GammaBoost { get; set; } = 0;

		[Caption ("Quality")]
		[MinimumValue (1), MaximumValue (MaxQuality)]
		public int Quality { get; set; } = MaxQuality;

		[Skip]
		public override bool IsDefault => Radius == 0;
	}
}
