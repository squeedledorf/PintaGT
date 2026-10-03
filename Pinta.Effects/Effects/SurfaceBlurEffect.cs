using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Smooths away soft detail and noise but keeps edges: each channel is averaged only
/// over neighbours whose value is close to the pixel's own, with weights falling off
/// linearly to zero at 2.5x the threshold.
/// </summary>
public sealed class SurfaceBlurEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsNoiseReduceNoise;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Surface Blur");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Blurs");

	public SurfaceBlurData Data => (SurfaceBlurData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public SurfaceBlurEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new SurfaceBlurData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		// Threshold is a percentage of the channel range.
		double reach = 2.5 * Data.Threshold * 255 / 100;
		if (reach <= 0)
			return; // Copy src to dest

		LocalHistogram.RenderRect (Apply, Data.Radius, source, destination, roi);

		ColorBgra Apply (ColorBgra src, int area, Span<int> hb, Span<int> hg, Span<int> hr, Span<int> ha)
		{
			ColorBgra s = src.ToStraightAlpha ();
			return ColorBgra.FromBgra (
				SmoothChannel (s.B, hb, reach),
				SmoothChannel (s.G, hg, reach),
				SmoothChannel (s.R, hr, reach),
				SmoothChannel (s.A, ha, reach)).ToPremultipliedAlpha ();
		}
	}

	/// <summary>Weighted mean of the histogram around <paramref name="center"/>.</summary>
	public static byte SmoothChannel (byte center, ReadOnlySpan<int> histogram, double reach)
	{
		int lo = Math.Max (0, (int) Math.Ceiling (center - reach));
		int hi = Math.Min (255, (int) Math.Floor (center + reach));
		double sum = 0, weights = 0;
		for (int i = lo; i <= hi; ++i) {
			if (histogram[i] == 0)
				continue;
			double w = histogram[i] * (1 - Math.Abs (i - center) / reach);
			sum += w * i;
			weights += w;
		}
		return weights > 0 ? (byte) Math.Clamp (Math.Round (sum / weights), 0, 255) : center;
	}

	public sealed class SurfaceBlurData : EffectData
	{
		[Caption ("Radius")]
		[MinimumValue (1), MaximumValue (100)]
		public int Radius { get; set; } = 6;

		[Caption ("Threshold")]
		[MinimumValue (0), MaximumValue (100)]
		public int Threshold { get; set; } = 15;

		[Skip]
		public override bool IsDefault => Threshold == 0;
	}
}
