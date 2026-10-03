using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>Averages the square area around each pixel: a fast, rough stand-in for Bokeh Blur.</summary>
public sealed class SquareBlurEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsDistortPixelate;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Square Blur");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Blurs");

	public SquareBlurData Data => (SquareBlurData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public SquareBlurEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new SquareBlurData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.Radius <= 0)
			return; // Copy src to dest

		KernelRowsBlur.KernelRow[] rows = KernelRowsBlur.Square (Data.Radius);
		GammaBoost gamma = new (Data.GammaBoost);
		foreach (var rect in rois)
			KernelRowsBlur.Render (src, dest, rect, rows, gamma);
	}

	public sealed class SquareBlurData : EffectData
	{
		[Caption ("Radius")]
		[MinimumValue (0), MaximumValue (200)]
		[IncrementValue (0.1), DigitsValue (1)]
		public double Radius { get; set; } = 6;

		[Caption ("Gamma Boost")]
		[MinimumValue ((int) Effects.GammaBoost.Min), MaximumValue ((int) Effects.GammaBoost.Max)]
		public double GammaBoost { get; set; } = 0;

		[Skip]
		public override bool IsDefault => Radius == 0;
	}
}
