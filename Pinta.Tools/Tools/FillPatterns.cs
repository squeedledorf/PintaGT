using System;
using System.Collections.Generic;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// The Paint Bucket's fill types: a solid colour or an 8×8 hatch pattern of the primary colour over the secondary,
/// named as in Paint.NET's Fill list. The patterns are drawn by rule rather than copied pixel for pixel.
/// </summary>
public static class FillPatterns
{
	public readonly record struct Pattern (string Name, Func<int, int, bool>? IsForeground)
	{
		public bool IsSolid => IsForeground is null;

		/// <summary>Whether the image pixel (x, y) takes the foreground colour. Patterns tile from the image origin.</summary>
		public bool IsOn (int x, int y)
			=> IsForeground is null || IsForeground (x & 7, y & 7);
	}

	// An ordered-dither matrix: a pixel is on in "Percent NN" when its rank is below NN% of 64.
	private static readonly int[,] bayer = {
		{ 0, 32, 8, 40, 2, 34, 10, 42 },
		{ 48, 16, 56, 24, 50, 18, 58, 26 },
		{ 12, 44, 4, 36, 14, 46, 6, 38 },
		{ 60, 28, 52, 20, 62, 30, 54, 22 },
		{ 3, 35, 11, 43, 1, 33, 9, 41 },
		{ 51, 19, 59, 27, 49, 17, 57, 25 },
		{ 15, 47, 7, 39, 13, 45, 5, 37 },
		{ 63, 31, 55, 23, 61, 29, 53, 21 },
	};

	private static Func<int, int, bool> Percent (int percent)
		=> (x, y) => bayer[y, x] < percent * 64 / 100;

	private static int Mod (int v, int m) => ((v % m) + m) % m;

	public static IReadOnlyList<Pattern> All { get; } = [
		new (Translations.GetString ("Solid Color"), null),
		new (Translations.GetString ("Horizontal"), (x, y) => y == 0),
		new (Translations.GetString ("Vertical"), (x, y) => x == 0),
		new (Translations.GetString ("Forward Diagonal"), (x, y) => x == y),
		new (Translations.GetString ("Backward Diagonal"), (x, y) => x == 7 - y),
		new (Translations.GetString ("Large Grid"), (x, y) => x == 0 || y == 0),
		new (Translations.GetString ("Diagonal Cross"), (x, y) => x == y || x == 7 - y),
		new (Translations.GetString ("Percent 05"), Percent (5)),
		new (Translations.GetString ("Percent 10"), Percent (10)),
		new (Translations.GetString ("Percent 20"), Percent (20)),
		new (Translations.GetString ("Percent 25"), Percent (25)),
		new (Translations.GetString ("Percent 30"), Percent (30)),
		new (Translations.GetString ("Percent 40"), Percent (40)),
		new (Translations.GetString ("Percent 50"), Percent (50)),
		new (Translations.GetString ("Percent 60"), Percent (60)),
		new (Translations.GetString ("Percent 70"), Percent (70)),
		new (Translations.GetString ("Percent 75"), Percent (75)),
		new (Translations.GetString ("Percent 80"), Percent (80)),
		new (Translations.GetString ("Percent 90"), Percent (90)),
		new (Translations.GetString ("Light Downward Diagonal"), (x, y) => Mod (x - y, 4) == 0),
		new (Translations.GetString ("Light Upward Diagonal"), (x, y) => Mod (x + y, 4) == 3),
		new (Translations.GetString ("Dark Downward Diagonal"), (x, y) => Mod (x - y, 4) < 2),
		new (Translations.GetString ("Dark Upward Diagonal"), (x, y) => Mod (x + y, 4) >= 2),
		new (Translations.GetString ("Wide Downward Diagonal"), (x, y) => Mod (x - y, 8) < 3),
		new (Translations.GetString ("Wide Upward Diagonal"), (x, y) => Mod (x + y, 8) >= 5),
		new (Translations.GetString ("Light Vertical"), (x, y) => x % 4 == 0),
		new (Translations.GetString ("Light Horizontal"), (x, y) => y % 4 == 0),
		new (Translations.GetString ("Narrow Vertical"), (x, y) => x % 2 == 0),
		new (Translations.GetString ("Narrow Horizontal"), (x, y) => y % 2 == 0),
		new (Translations.GetString ("Dark Vertical"), (x, y) => x % 4 < 2),
		new (Translations.GetString ("Dark Horizontal"), (x, y) => y % 4 < 2),
		new (Translations.GetString ("Small Grid"), (x, y) => x % 4 == 0 || y % 4 == 0),
		new (Translations.GetString ("Dotted Grid"), (x, y) => (x % 4 == 0 && y % 2 == 0) || (y % 4 == 0 && x % 2 == 0)),
		new (Translations.GetString ("Small Checker Board"), (x, y) => ((x / 2 + y / 2) & 1) == 0),
		new (Translations.GetString ("Large Checker Board"), (x, y) => ((x / 4 + y / 4) & 1) == 0),
		new (Translations.GetString ("Solid Diamond"), (x, y) => Math.Abs (x - 3.5) + Math.Abs (y - 3.5) <= 3),
	];
}
