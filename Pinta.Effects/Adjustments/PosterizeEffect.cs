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

public sealed class PosterizeEffect : BaseEffect
{
	public sealed override bool IsTileable => true;

	public override string Icon => Pinta.Resources.Icons.AdjustmentsPosterize;

	public override string Name => Translations.GetString ("Posterize");

	public override bool IsConfigurable => true;

	public override string AdjustmentMenuKey => "P";

	public PosterizeData Data => (PosterizeData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;

	public PosterizeEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();

		EffectData = new PosterizeData ();
	}

	public override async Task<bool> LaunchConfiguration ()
	{
		using PosterizeDialog dialog = PosterizeDialog.New (chrome, Data);
		dialog.Title = Name;
		dialog.IconName = Icon;

		Gtk.ResponseType response = await dialog.RunAsync ();

		dialog.Destroy ();

		return Gtk.ResponseType.Ok == response;
	}

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		PosterizeData data = Data;
		// 256 levels leave a channel unchanged, so an unticked channel passes through.
		UnaryPixelOps.PosterizePixel op = new (
			data.RedEnabled ? data.Red : 256,
			data.GreenEnabled ? data.Green : 256,
			data.BlueEnabled ? data.Blue : 256,
			data.AlphaEnabled ? data.Alpha : 256);

		op.Apply (dest, src, rois);
	}
}

public sealed class PosterizeData : EffectData
{
	public int Red { get; set; } = 16;
	public int Green { get; set; } = 16;
	public int Blue { get; set; } = 16;
	public int Alpha { get; set; } = 16;
	public bool RedEnabled { get; set; } = true;
	public bool GreenEnabled { get; set; } = true;
	public bool BlueEnabled { get; set; } = true;
	public bool AlphaEnabled { get; set; } = true;
	public bool Linked { get; set; } = true;
}
