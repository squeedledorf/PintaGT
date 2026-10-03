using System;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// Paint.NET's PNG Bit Depth choices.
/// </summary>
public enum PngBitDepth
{
	/// <summary>The smallest file that loses nothing: 8-bit when the image has 256 colours or fewer, else 24-bit when it is opaque, else 32-bit.</summary>
	AutoDetect,
	Bpp32,
	Bpp24,
	Bpp8,
}

/// <summary>
/// The settings of Paint.NET's Save Configuration dialog for the formats Pinta configures.
/// </summary>
public sealed record SaveConfiguration (
	int JpegQuality,
	PngBitDepth BitDepth,
	int DitheringLevel,
	int TransparencyThreshold)
{
	/// <summary>Paint.NET's defaults (the "Defaults" button).</summary>
	public static SaveConfiguration Defaults { get; } = new (95, PngBitDepth.AutoDetect, 7, 128);

	public const int MaxDitheringLevel = 8;

	internal static SaveConfiguration Load (ISettingsService settings)
		=> new (
			Math.Clamp (settings.GetSetting (SettingNames.JPG_QUALITY, Defaults.JpegQuality), 1, 100),
			(PngBitDepth) Math.Clamp (settings.GetSetting (SettingNames.PNG_BIT_DEPTH, (int) Defaults.BitDepth), 0, 3),
			Math.Clamp (settings.GetSetting (SettingNames.PNG_DITHERING_LEVEL, Defaults.DitheringLevel), 0, MaxDitheringLevel),
			Math.Clamp (settings.GetSetting (SettingNames.PNG_TRANSPARENCY_THRESHOLD, Defaults.TransparencyThreshold), 0, 255));

	internal void Store (ISettingsService settings)
	{
		settings.PutSetting (SettingNames.JPG_QUALITY, JpegQuality);
		settings.PutSetting (SettingNames.PNG_BIT_DEPTH, (int) BitDepth);
		settings.PutSetting (SettingNames.PNG_DITHERING_LEVEL, DitheringLevel);
		settings.PutSetting (SettingNames.PNG_TRANSPARENCY_THRESHOLD, TransparencyThreshold);
	}
}

/// <summary>
/// Asks the GUI to show the Save Configuration dialog: the settings for <see cref="FileType"/>
/// and a live preview of <see cref="Encode"/>'s output with its file size.
/// </summary>
public sealed class SaveConfigurationEventArgs : EventArgs
{
	public SaveConfigurationEventArgs (
		string fileType,
		ImageSurface image,
		SaveConfiguration configuration,
		Func<SaveConfiguration, byte[]> encode,
		Gtk.Window parent)
	{
		FileType = fileType;
		Image = image;
		Configuration = configuration;
		Encode = encode;
		ParentWindow = parent;
	}

	/// <summary>"jpeg" or "png".</summary>
	public string FileType { get; }

	/// <summary>The flattened image being saved.</summary>
	public ImageSurface Image { get; }

	public SaveConfiguration Configuration { get; set; }

	/// <summary>Encodes the image as the file would hold it. Safe to call off the main thread.</summary>
	public Func<SaveConfiguration, byte[]> Encode { get; }

	public Gtk.Window ParentWindow { get; }

	public bool Cancel { get; set; }
}
