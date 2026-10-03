using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class HighlightsShadowsEffect : BaseEffect
{
	public sealed override bool IsTileable
		=> true;

	public override string Icon
		=> Resources.Icons.AdjustmentsHighlightsShadows;

	public override string Name
		=> Translations.GetString ("Highlights / Shadows");

	public override bool IsConfigurable
		=> true;

	public HighlightsShadowsData Data
		=> (HighlightsShadowsData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public HighlightsShadowsEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new HighlightsShadowsData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>
	/// Level shift for each luma value. Shadows acts most around a third of the way up,
	/// highlights around two thirds; +100 moves those tones by a quarter of the range.
	/// </summary>
	// ponytail: per-pixel tone curve, no blurred local-luminance mask; add one if flat areas look washed out.
	public static int[] CreateShiftTable (int shadows, int highlights)
	{
		const double STRENGTH = 0.25 * 27 / 4; // 27/4 normalises L(1-L)^2 to a peak of 1
		int[] table = new int[256];
		for (int i = 0; i < 256; i++) {
			double l = i / 255d;
			double shift =
				shadows / 100d * STRENGTH * l * (1 - l) * (1 - l)
				+ highlights / 100d * STRENGTH * l * l * (1 - l);
			table[i] = (int) Math.Round (shift * 255);
		}
		return table;
	}

	public static ColorBgra Apply (in ColorBgra color, int[] shifts)
	{
		if (color.A == 0)
			return color;
		ColorBgra straight = color.ToStraightAlpha ();
		int shift = shifts[straight.GetIntensityByte ()];
		ColorBgra result = ColorBgra.FromBgraClamped (straight.B + shift, straight.G + shift, straight.R + shift, straight.A);
		return color.A == 255 ? result : result.ToPremultipliedAlpha ();
	}

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		int[] shifts = CreateShiftTable (Data.Shadows, Data.Highlights);
		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();
		foreach (var pixel in Tiling.GeneratePixelOffsets (roi, source.GetSize ()))
			destinationData[pixel.memoryOffset] = Apply (sourceData[pixel.memoryOffset], shifts);
	}

	public sealed class HighlightsShadowsData : EffectData
	{
		[Caption ("Shadows"), MinimumValue (-100), MaximumValue (100)]
		public int Shadows { get; set; } = 0;

		[Caption ("Highlights"), MinimumValue (-100), MaximumValue (100)]
		public int Highlights { get; set; } = 0;

		[Skip]
		public override bool IsDefault
			=> Shadows == 0 && Highlights == 0;
	}
}
