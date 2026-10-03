using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;

namespace Pinta.Core;

public static class PaletteHelper
{
	public static Palette CreateDefault ()
	{
		return new (EnumerateDefaultColors ());
	}

	public static void Save (this Palette palette, Gio.File file, IPaletteSaver saver)
	{
		saver.Save (palette.Colors, file);
	}

	public static void LoadDefault (this Palette palette)
	{
		palette.Load (EnumerateDefaultColors ());
	}

	public static void Load (this Palette palette, PaletteFormatManager paletteFormats, Gio.File file)
	{
		List<Color> loadedColors = LoadColorsFromFile (paletteFormats, file);
		palette.Load (loadedColors);
	}

	private static List<Color> LoadColorsFromFile (PaletteFormatManager paletteFormats, Gio.File file)
	{
		var loader = paletteFormats.GetFormatByFilename (file.GetDisplayName ())?.Loader;

		if (loader != null)
			return loader.Load (file);

		StringBuilder errors = new ();

		// Not a recognized extension, so attempt all formats
		foreach (var format in paletteFormats.Formats.Where (f => !f.IsWriteOnly ())) {
			try {
				var loaded_colors = format.Loader.Load (file);
				if (loaded_colors != null)
					return loaded_colors;
			} catch (Exception e) {
				// Record errors in case none of the formats work.
				errors.AppendLine ($"Failed to load palette as {format.Filter.Name}:");
				errors.Append (e.ToString ());
				errors.AppendLine ();
			}
		}

		throw new PaletteLoadException (
			file.GetParseName (),
			errors.ToString ());
	}

	// Paint.NET 5's default layout: 16 columns, read row by row. Columns 0-1 are greys,
	// then 14 hues as full colour, dark, pastel and muted rows.
	private static readonly string[] default_rows = [
		"000000 404040 FF0000 FF6A00 FFD800 B6FF00 4CFF00 00FF21 00FF90 00FFFF 0094FF 0026FF 4800FF B200FF FF00DC FF006E",
		"FFFFFF 808080 7F0000 7F3300 7F6A00 5B7F00 267F00 007F0E 007F46 007F7F 004A7F 00137F 21007F 57007F 7F006E 7F0037",
		"A0A0A0 303030 FF7F7F FFB27F FFE97F DAFF7F A5FF7F 7FFF8E 7FFFC5 7FFFFF 7FC9FF 7F92FF A17FFF D67FFF FF7FED FF7FB6",
		"C0C0C0 606060 7F3F3F 7F593F 7F743F 6D7F3F 527F3F 3F7F47 3F7F62 3F7F7F 3F647F 3F497F 503F7F 6B3F7F 7F3F76 7F3F5B",
	];

	/// <summary>
	/// The 96-colour default palette (16 x 6). The last two rows repeat the first two at half opacity.
	/// </summary>
	public static IEnumerable<Color> EnumerateDefaultColors ()
	{
		Color[] opaque = [.. default_rows.SelectMany (row => row.Split (' ')).Select (hex => Color.FromHex (hex)!.Value)];
		return [.. opaque, .. opaque.Take (32).Select (c => c with { A = 0.5 })];
	}
}
