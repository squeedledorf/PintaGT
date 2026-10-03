/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Krzysztof Marecki <marecki.krzysztof@gmail.com>         //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class SharpenEffect : BaseEffect
{
	public override string Icon => Pinta.Resources.Icons.EffectsPhotoSharpen;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Sharpen");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Photo");

	public SharpenData Data => (SharpenData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public SharpenEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new SharpenData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		// Threshold is a percentage of the channel range.
		int threshold = (int) Math.Round (Data.Threshold * 255 / 100);
		LocalHistogram.RenderRect (Apply, Data.Amount, source, destination, roi);

		ColorBgra Apply (ColorBgra src, int area, Span<int> hb, Span<int> hg, Span<int> hr, Span<int> ha)
		{
			ColorBgra median = LocalHistogram.GetPercentile (50, area, hb, hg, hr, ha);
			return IsBelowThreshold (src, median, threshold) ? src : ColorBgra.Lerp (src, median, -0.5f);
		}
	}

	/// <summary>Pixels whose largest channel difference from the local median is within the threshold stay as they are.</summary>
	public static bool IsBelowThreshold (ColorBgra src, ColorBgra median, int threshold)
		=> threshold > 0
		&& Math.Abs (src.B - median.B) <= threshold
		&& Math.Abs (src.G - median.G) <= threshold
		&& Math.Abs (src.R - median.R) <= threshold
		&& Math.Abs (src.A - median.A) <= threshold;
}

public sealed class SharpenData : EffectData
{
	[Caption ("Amount")]
	[MinimumValue (1), MaximumValue (20)]
	public int Amount { get; set; } = 2;

	[Caption ("Threshold")]
	[MinimumValue (0), MaximumValue (100)]
	public double Threshold { get; set; } = 0;
}

