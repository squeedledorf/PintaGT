using System;

namespace Pinta.Core;

/// <summary>
/// Units for the Resolution and Print size fields of the New, Resize and Canvas Size dialogs.
/// </summary>
public enum PrintUnit
{
	Inches,
	Centimeters,
}

/// <summary>
/// Conversions between pixel size, resolution and print size ("Pixel size / DPI = Print size").
/// </summary>
public static class PrintSize
{
	public const double CentimetersPerInch = 2.54;

	/// <summary>Pixels per inch shown as pixels per <paramref name="unit"/>.</summary>
	public static double DpiToResolution (double dpi, PrintUnit unit)
		=> unit == PrintUnit.Inches ? dpi : dpi / CentimetersPerInch;

	/// <summary>A resolution in pixels per <paramref name="unit"/> as pixels per inch.</summary>
	public static double ResolutionToDpi (double resolution, PrintUnit unit)
		=> unit == PrintUnit.Inches ? resolution : resolution * CentimetersPerInch;

	public static double PixelsToPrint (int pixels, double dpi, PrintUnit unit)
		=> pixels / DpiToResolution (dpi, unit);

	public static int PrintToPixels (double length, double dpi, PrintUnit unit)
		=> Math.Max (1, (int) Math.Round (length * DpiToResolution (dpi, unit)));

	/// <summary>
	/// The uncompressed size of an image, as the dialogs' "New size: 1.8 MB" line shows it.
	/// </summary>
	public static string FormatImageMemory (Size size)
		=> FormatBytes ((long) size.Width * size.Height * 4);

	public static string FormatBytes (long bytes)
	{
		if (bytes < 1024)
			return Translations.GetString ("{0} bytes", bytes);

		string[] units = ["KB", "MB", "GB", "TB"];
		double value = bytes;
		int unit = -1;
		while (value >= 1024 && unit < units.Length - 1) {
			value /= 1024;
			unit++;
		}

		return $"{value:0.0} {units[unit]}";
	}
}
