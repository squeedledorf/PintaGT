using System;
using Cairo;
using GdkPixbuf;

namespace Pinta.Core;

/// <summary>
/// PNG with Paint.NET's Bit Depth choice: Auto-detect, 32-bit, 24-bit or 8-bit.
/// </summary>
public sealed class PngFormat : GdkPixbufFormat
{
	public PngFormat ()
		: base ("png")
	{
	}

	protected override void DoSave (ImageSurface flattenedImage, Document document, Gio.File file, Gtk.Window parent)
	{
		SaveConfiguration config = SaveConfiguration.Load (PintaCore.Settings);
		byte[] Encode (SaveConfiguration c) => PngFormat.Encode (flattenedImage, c, document.Dpi);

		// As in Paint.NET, the Save Configuration dialog comes up on the first save to this file in the session.
		if (!document.HasBeenSavedInSession)
			config = PintaCore.Actions.File.RaiseSaveConfiguration ("png", flattenedImage, config, Encode, parent)
				?? throw new OperationCanceledException ();

		config.Store (PintaCore.Settings);
		WriteFile (file, Encode (config));
	}

	public static byte[] Encode (ImageSurface image, SaveConfiguration config, double dpi)
		=> ImageDpi.Write (
			config.BitDepth switch {
				PngBitDepth.Bpp32 => EncodeTrueColor (image, includeAlpha: true),
				PngBitDepth.Bpp24 => EncodeTrueColor (image, includeAlpha: false),
				PngBitDepth.Bpp8 => IndexedPng.EncodeQuantized (image, config.DitheringLevel, config.TransparencyThreshold),
				_ => AutoDetect (image),
			},
			dpi);

	/// <summary>
	/// The smallest of the bit depths that lose nothing: 24-bit when every pixel is opaque
	/// (else 32-bit), or 8-bit when the image has 256 colours or fewer.
	/// </summary>
	private static byte[] AutoDetect (ImageSurface image)
	{
		bool opaque = true;
		foreach (ColorBgra pixel in image.GetReadOnlyPixelData ()) {
			if (pixel.A != 255) {
				opaque = false;
				break;
			}
		}

		byte[] trueColor = EncodeTrueColor (image, includeAlpha: !opaque);
		byte[]? palette = IndexedPng.EncodeExact (image);
		return palette is not null && palette.Length < trueColor.Length ? palette : trueColor;
	}

	private static byte[] EncodeTrueColor (ImageSurface image, bool includeAlpha)
	{
		using Pixbuf pb = image.ToPixbuf (includeAlpha);
		return pb.SaveToBuffer ("png");
	}
}
