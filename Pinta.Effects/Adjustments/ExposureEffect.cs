using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class ExposureEffect : BaseEffect
{
	public sealed override bool IsTileable
		=> true;

	public override string Icon
		=> Resources.Icons.AdjustmentsExposure;

	public override string Name
		=> Translations.GetString ("Exposure");

	public override bool IsConfigurable
		=> true;

	public ExposureData Data
		=> (ExposureData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public ExposureEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new ExposureData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		// One stop doubles the light reaching every channel.
		byte[] table = ChannelTable.Create (Math.Pow (2, Data.Exposure / 50d));
		ChannelTable.Apply (source, destination, roi, table, table, table);
	}

	public sealed class ExposureData : EffectData
	{
		// Fiftieths of a stop. Paint.NET 5 shows a whole-number slider (24 sits just right of centre, and
		// brightens mid-tones by about a sixth); the range and scale are fitted from that screenshot.
		[Caption ("Exposure"), MinimumValue (-200), MaximumValue (200)]
		public int Exposure { get; set; } = 0;

		[Skip]
		public override bool IsDefault
			=> Exposure == 0;
	}
}

/// <summary>
/// 256-entry per-channel lookup tables, applied to the straight (un-premultiplied) color of each pixel.
/// </summary>
internal static class ChannelTable
{
	/// <summary>
	/// A table that multiplies each level by <paramref name="gain"/> in linear light.
	/// </summary>
	public static byte[] Create (double gain)
	{
		byte[] table = new byte[256];
		for (int i = 0; i < 256; i++)
			table[i] = (byte) (0.5 + 255 * SrgbUtility.ToSrgbClamped (SrgbUtility.ToLinear ((byte) i) * gain));
		return table;
	}

	public static ColorBgra Apply (in ColorBgra color, byte[] b, byte[] g, byte[] r)
	{
		if (color.A == 0)
			return color;
		ColorBgra straight = color.ToStraightAlpha ();
		ColorBgra result = ColorBgra.FromBgra (b[straight.B], g[straight.G], r[straight.R], straight.A);
		return color.A == 255 ? result : result.ToPremultipliedAlpha ();
	}

	public static void Apply (ImageSurface source, ImageSurface destination, RectangleI roi, byte[] b, byte[] g, byte[] r)
	{
		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();
		foreach (var pixel in Tiling.GeneratePixelOffsets (roi, source.GetSize ()))
			destinationData[pixel.memoryOffset] = Apply (sourceData[pixel.memoryOffset], b, g, r);
	}
}
