using System;
using System.Collections.Generic;
using System.Drawing;

namespace Pinta.PdnPlugins;

/// <summary>Turns a selection mask into rectangles and clips render tiles to them.</summary>
public static class RoiUtil
{
	/// <summary>
	/// Scans of a coverage mask (any non-zero byte is selected), as rectangles in image coordinates.
	/// Each row's runs are merged with identical runs on the rows below, so a rectangular selection is one rectangle.
	/// Results are sorted by top edge.
	/// </summary>
	public static List<Rectangle> ScansFromMask (ReadOnlySpan<byte> mask, int width, int height, int stride, int offsetX, int offsetY)
	{
		List<Rectangle> result = [];
		// Open rectangles (runs that continue from the previous row), keyed by (x, width).
		Dictionary<(int X, int W), int> open = [];
		Dictionary<(int X, int W), int> next = [];

		for (int y = 0; y <= height; y++) {
			next.Clear ();
			if (y < height) {
				ReadOnlySpan<byte> row = mask.Slice (y * stride, width);
				int x = 0;
				while (x < width) {
					while (x < width && row[x] == 0) x++;
					if (x == width) break;
					int start = x;
					while (x < width && row[x] != 0) x++;
					var run = (start, x - start);
					next[run] = open.TryGetValue (run, out int top) ? top : y;
				}
			}
			foreach (var (run, top) in open)
				if (!next.ContainsKey (run))
					result.Add (new Rectangle (offsetX + run.X, offsetY + top, run.W, y - top));
			(open, next) = (next, open);
		}

		result.Sort ((a, b) => a.Y != b.Y ? a.Y.CompareTo (b.Y) : a.X.CompareTo (b.X));
		return result;
	}

	/// <summary>The parts of <paramref name="tile"/> inside the scans (sorted by top edge), or the tile itself when there is no selection.</summary>
	public static Rectangle[] Clip (Rectangle tile, IReadOnlyList<Rectangle>? scans)
	{
		if (scans is null)
			return tile.IsEmpty ? [] : [tile];

		List<Rectangle> rois = [];
		foreach (Rectangle s in scans) {
			if (s.Y >= tile.Bottom)
				break;
			Rectangle r = Rectangle.Intersect (s, tile);
			if (r.Width > 0 && r.Height > 0)
				rois.Add (r);
		}
		return [.. rois];
	}
}
