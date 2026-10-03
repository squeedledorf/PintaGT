using System;

namespace Pinta.Core;

/// <summary>
/// The resampling mode to use when resizing an image, in Paint.NET's order.
/// </summary>
public enum ResamplingMode
{
	Bicubic,
	BicubicSmooth,
	Bilinear,
	BilinearLowQuality,
	AdaptiveSharp,
	Lanczos,
	Fant,
	NearestNeighbor,
}

public static class ResamplingModeExtensions
{
	/// <summary>
	/// Returns a user-facing label for a resampling mode.
	/// </summary>
	public static string GetLabel (this ResamplingMode mode)
	{
		return mode switch {
			ResamplingMode.Bicubic => Translations.GetString ("Bicubic"),
			ResamplingMode.BicubicSmooth => Translations.GetString ("Bicubic (Smooth)"),
			ResamplingMode.Bilinear => Translations.GetString ("Bilinear"),
			ResamplingMode.BilinearLowQuality => Translations.GetString ("Bilinear (Low Quality)"),
			ResamplingMode.AdaptiveSharp => Translations.GetString ("Adaptive (Sharp)"),
			ResamplingMode.Lanczos => Translations.GetString ("Lanczos"),
			ResamplingMode.Fant => Translations.GetString ("Fant"),
			ResamplingMode.NearestNeighbor => Translations.GetString ("Nearest Neighbor"),
			_ => throw new ArgumentOutOfRangeException (nameof (mode))
		};
	}

	/// <summary>
	/// Translates a resampling mode to the closest Cairo filter, for on-screen drawing.
	/// Resizing an image goes through <see cref="Resampler"/> instead.
	/// </summary>
	public static Cairo.Filter ToCairoFilter (this ResamplingMode mode)
	{
		return mode switch {
			ResamplingMode.NearestNeighbor => Cairo.Filter.Nearest,
			ResamplingMode.Bilinear or ResamplingMode.BilinearLowQuality => Cairo.Filter.Bilinear,
			_ => Cairo.Filter.Good,
		};
	}
}
