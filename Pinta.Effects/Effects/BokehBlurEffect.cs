using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Averages the circular area around each pixel, like the out-of-focus part of a photo.
/// Raising the gamma boost makes highlights bloom into bright discs.
/// </summary>
public sealed class BokehBlurEffect : BaseEffect
{
	// Paint.NET's note: Bokeh replaced Unfocus and shows the icon Unfocus used to have.
	public override string Icon => Resources.Icons.EffectsBlursUnfocus;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Bokeh Blur");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Blurs");

	public BokehBlurData Data => (BokehBlurData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public BokehBlurEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new BokehBlurData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>How many disc rows a quality level samples at most.</summary>
	public static int MaxRowsForQuality (int quality) => 16 * quality + 1;

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.Radius <= 0)
			return; // Copy src to dest

		KernelRowsBlur.KernelRow[] rows = KernelRowsBlur.Disc (Data.Radius, MaxRowsForQuality (Data.Quality));
		GammaBoost gamma = new (Data.GammaBoost);
		foreach (var rect in rois)
			KernelRowsBlur.Render (src, dest, rect, rows, gamma);
	}

	public sealed class BokehBlurData : EffectData
	{
		[Caption ("Radius")]
		[MinimumValue (0), MaximumValue (300)]
		[IncrementValue (0.1), DigitsValue (1)]
		public double Radius { get; set; } = 6;

		[Caption ("Gamma Boost")]
		[MinimumValue ((int) Effects.GammaBoost.Min), MaximumValue ((int) Effects.GammaBoost.Max)]
		public double GammaBoost { get; set; } = 0;

		[Caption ("Quality")]
		[MinimumValue (1), MaximumValue (10)]
		public int Quality { get; set; } = 3;

		[Skip]
		public override bool IsDefault => Radius == 0;
	}
}
