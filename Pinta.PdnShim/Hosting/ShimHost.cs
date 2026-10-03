using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo ("Pinta.PdnPlugins")]
[assembly: InternalsVisibleTo ("Pinta.PdnPlugins.Tests")]

namespace PaintDotNet.Hosting;

/// <summary>A decoded image: straight-alpha BGRA rows packed without padding.</summary>
public sealed record DecodedImage (int Width, int Height, byte[] Bgra);

/// <summary>
/// Services the shim needs from the host application (image codecs, fonts, palettes).
/// Pinta fills these in before loading plugins; the shim stays free of GTK.
/// </summary>
public static class ShimHost
{
	/// <summary>Decodes PNG/JPEG/BMP/GIF bytes, or returns null.</summary>
	public static Func<byte[], DecodedImage> DecodeImage { get; set; }

	/// <summary>Encodes straight-alpha BGRA pixels as PNG.</summary>
	public static Func<DecodedImage, byte[]> EncodePng { get; set; }

	/// <summary>Installed font family names.</summary>
	public static Func<IReadOnlyList<string>> FontFamilies { get; set; }

	/// <summary>The current palette, as straight-alpha BGRA.</summary>
	public static Func<IReadOnlyList<ColorBgra>> CurrentPalette { get; set; }

	/// <summary>The image on the clipboard, if any (straight-alpha BGRA).</summary>
	public static Func<DecodedImage> ClipboardImage { get; set; }

	/// <summary>Opens a URL in the browser.</summary>
	public static Action<string> LaunchUrl { get; set; }

	/// <summary>A per-user writable directory for plugin data.</summary>
	public static string UserDataDirectory { get; set; } = Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData);
}
