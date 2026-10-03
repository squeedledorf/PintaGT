using System;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class InvertAlphaEffect : BaseEffect
{
	public InvertAlphaEffect (IServiceProvider _) { }

	public sealed override bool IsTileable
		=> true;

	public override string Icon
		=> Resources.Icons.AdjustmentsInvertAlpha;

	public override string Name
		=> Translations.GetString ("Invert Alpha");

	public override string AdjustmentMenuKey
		=> "I";

	public override string AdjustmentMenuKeyModifiers
		=> "<Primary><Alt>";

	/// <summary>
	/// Inverts the opacity of a premultiplied pixel, keeping its straight color.
	/// A fully transparent pixel has no color left to keep, so it becomes opaque black.
	/// </summary>
	public static ColorBgra InvertAlpha (in ColorBgra color)
		=> color.NewAlpha ((byte) (255 - color.A));

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();
		foreach (var pixel in Tiling.GeneratePixelOffsets (roi, source.GetSize ()))
			destinationData[pixel.memoryOffset] = InvertAlpha (sourceData[pixel.memoryOffset]);
	}
}
