using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Cairo;

namespace Pinta.Core;

/// <summary>
/// 8-bit (palette) PNG writing for Paint.NET's PNG Bit Depth = 8-bit: an Octree palette,
/// Floyd–Steinberg dithering scaled by the dithering level, and a transparency threshold.
/// gdk-pixbuf only writes 24- and 32-bit PNGs.
/// </summary>
public static class IndexedPng
{
	/// <summary>
	/// The image as an 8-bit PNG that keeps every pixel exactly, or null when it has more than 256 colours.
	/// </summary>
	public static byte[]? EncodeExact (ImageSurface surface)
	{
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();
		Dictionary<ColorBgra, int> lookup = [];
		byte[] indices = new byte[pixels.Length];

		for (int i = 0; i < pixels.Length; i++) {
			ColorBgra c = Straight (pixels[i]);
			if (!lookup.TryGetValue (c, out int index)) {
				if (lookup.Count == 256)
					return null;
				index = lookup.Count;
				lookup.Add (c, index);
			}
			indices[i] = (byte) index;
		}

		ColorBgra[] palette = new ColorBgra[lookup.Count];
		foreach (var (color, index) in lookup)
			palette[index] = color;

		return Write (surface.Width, surface.Height, palette, indices);
	}

	/// <summary>
	/// The image reduced to at most 256 colours.
	/// Pixels with alpha below <paramref name="transparencyThreshold"/> become fully transparent and the rest opaque.
	/// </summary>
	/// <param name="ditheringLevel">0 (none) to <see cref="SaveConfiguration.MaxDitheringLevel"/> (full Floyd–Steinberg).</param>
	public static byte[] EncodeQuantized (ImageSurface surface, int ditheringLevel, int transparencyThreshold)
	{
		int width = surface.Width;
		int height = surface.Height;
		ReadOnlySpan<ColorBgra> pixels = surface.GetReadOnlyPixelData ();

		bool[] clear = new bool[pixels.Length];
		bool anyClear = false;
		Octree octree = new ();
		for (int i = 0; i < pixels.Length; i++) {
			ColorBgra c = pixels[i];
			if (c.A < transparencyThreshold) {
				clear[i] = anyClear = true;
				continue;
			}
			ColorBgra s = Straight (c);
			octree.Add (s.R, s.G, s.B);
		}

		// One entry is kept for transparency when needed.
		int maxColors = anyClear ? 255 : 256;
		octree.ReduceTo (maxColors);
		List<ColorBgra> palette = octree.Palette ();
		int clearIndex = -1;
		if (anyClear) {
			clearIndex = palette.Count;
			palette.Add (ColorBgra.Transparent);
		}
		if (palette.Count == 0)
			palette.Add (ColorBgra.Black);

		NearestColor nearest = new (palette, clearIndex);
		float strength = Math.Clamp (ditheringLevel, 0, SaveConfiguration.MaxDitheringLevel) / (float) SaveConfiguration.MaxDitheringLevel;

		byte[] indices = new byte[pixels.Length];
		float[] errorThis = new float[(width + 2) * 3];
		float[] errorNext = new float[(width + 2) * 3];

		for (int y = 0; y < height; y++) {
			Array.Clear (errorNext);
			for (int x = 0; x < width; x++) {
				int i = y * width + x;
				if (clear[i]) {
					indices[i] = (byte) clearIndex;
					continue;
				}

				ColorBgra s = Straight (pixels[i]);
				int e = (x + 1) * 3;
				float r = Math.Clamp (s.R + errorThis[e], 0, 255);
				float g = Math.Clamp (s.G + errorThis[e + 1], 0, 255);
				float b = Math.Clamp (s.B + errorThis[e + 2], 0, 255);

				int index = nearest.Find ((int) (r + 0.5f), (int) (g + 0.5f), (int) (b + 0.5f));
				indices[i] = (byte) index;

				if (strength == 0)
					continue;

				ColorBgra chosen = palette[index];
				float er = (r - chosen.R) * strength;
				float eg = (g - chosen.G) * strength;
				float eb = (b - chosen.B) * strength;
				Spread (errorThis, e + 3, er, eg, eb, 7 / 16f);
				Spread (errorNext, e - 3, er, eg, eb, 3 / 16f);
				Spread (errorNext, e, er, eg, eb, 5 / 16f);
				Spread (errorNext, e + 3, er, eg, eb, 1 / 16f);
			}
			(errorThis, errorNext) = (errorNext, errorThis);
		}

		return Write (width, height, palette.ToArray (), indices);
	}

	private static void Spread (float[] errors, int at, float r, float g, float b, float weight)
	{
		errors[at] += r * weight;
		errors[at + 1] += g * weight;
		errors[at + 2] += b * weight;
	}

	/// <summary>Straight (non-premultiplied) alpha, rounded as gdk-pixbuf rounds it for 32-bit PNGs.</summary>
	private static ColorBgra Straight (ColorBgra c)
	{
		if (c.A == 0)
			return ColorBgra.Transparent;
		if (c.A == 255)
			return c;
		return ColorBgra.FromBgra (
			(byte) Math.Min (255, (c.B * 255 + c.A / 2) / c.A),
			(byte) Math.Min (255, (c.G * 255 + c.A / 2) / c.A),
			(byte) Math.Min (255, (c.R * 255 + c.A / 2) / c.A),
			c.A);
	}

	/// <summary>Palette lookups for dithered colours, cached at 5 bits per channel.</summary>
	private sealed class NearestColor (List<ColorBgra> palette, int skip)
	{
		private readonly short[] cache = Enumerable.Repeat ((short) -1, 32 * 32 * 32).ToArray ();

		public int Find (int r, int g, int b)
		{
			int key = (r >> 3) << 10 | (g >> 3) << 5 | b >> 3;
			if (cache[key] >= 0)
				return cache[key];

			int best = 0;
			int bestDistance = int.MaxValue;
			for (int i = 0; i < palette.Count; i++) {
				if (i == skip)
					continue;
				ColorBgra p = palette[i];
				int dr = p.R - r, dg = p.G - g, db = p.B - b;
				int d = dr * dr + dg * dg + db * db;
				if (d < bestDistance) {
					bestDistance = d;
					best = i;
				}
			}
			cache[key] = (short) best;
			return best;
		}
	}

	/// <summary>The classic octree colour quantizer: 8 levels, merging the deepest nodes first.</summary>
	private sealed class Octree
	{
		private sealed class Node
		{
			public long R, G, B;
			public int Count;
			public Node?[]? Children = new Node?[8];
			public bool IsLeaf => Children is null;
		}

		private const int Depth = 8;
		private readonly Node root = new ();
		private readonly List<Node>[] reducible = Enumerable.Range (0, Depth).Select (_ => new List<Node> ()).ToArray ();
		private int leaves;

		public void Add (byte r, byte g, byte b)
		{
			Node node = root;
			for (int level = 0; !node.IsLeaf; level++) {
				int shift = 7 - level;
				int child = ((r >> shift) & 1) << 2 | ((g >> shift) & 1) << 1 | ((b >> shift) & 1);
				Node? next = node.Children![child];
				if (next is null) {
					next = new Node ();
					if (level + 1 == Depth) {
						next.Children = null;
						leaves++;
					} else
						reducible[level + 1].Add (next);
					node.Children[child] = next;
				}
				node = next;
			}
			node.R += r;
			node.G += g;
			node.B += b;
			node.Count++;

			// ponytail: reduce as we go so memory stays bounded; one pass over the image, no second refinement pass.
			if (leaves > 1024)
				ReduceTo (256);
		}

		public void ReduceTo (int maxColors)
		{
			while (leaves > maxColors) {
				int level = Depth - 1;
				while (level > 0 && reducible[level].Count == 0)
					level--;
				List<Node> list = reducible[level];
				if (list.Count == 0) {
					if (root.IsLeaf)
						return;
					list.Add (root);
				}
				Node node = list[^1];
				list.RemoveAt (list.Count - 1);

				int merged = 0;
				foreach (Node? child in node.Children!) {
					if (child is null)
						continue;
					node.R += child.R;
					node.G += child.G;
					node.B += child.B;
					node.Count += child.Count;
					merged++;
				}
				node.Children = null;
				leaves -= merged - 1;
			}
		}

		public List<ColorBgra> Palette ()
		{
			List<ColorBgra> result = [];
			Collect (root, result);
			return result;
		}

		private static void Collect (Node node, List<ColorBgra> result)
		{
			if (node.IsLeaf) {
				if (node.Count > 0)
					result.Add (ColorBgra.FromBgra (
						(byte) (node.B / node.Count),
						(byte) (node.G / node.Count),
						(byte) (node.R / node.Count),
						255));
				return;
			}
			foreach (Node? child in node.Children!)
				if (child is not null)
					Collect (child, result);
		}
	}

	// --- PNG container

	private static byte[] Write (int width, int height, ColorBgra[] palette, byte[] indices)
	{
		using MemoryStream png = new ();
		png.Write ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian (header, width);
		BinaryPrimitives.WriteInt32BigEndian (header.AsSpan (4), height);
		header[8] = 8; // bit depth
		header[9] = 3; // colour type: palette
		WriteChunk (png, "IHDR", header);

		byte[] plte = new byte[palette.Length * 3];
		for (int i = 0; i < palette.Length; i++) {
			plte[i * 3] = palette[i].R;
			plte[i * 3 + 1] = palette[i].G;
			plte[i * 3 + 2] = palette[i].B;
		}
		WriteChunk (png, "PLTE", plte);

		int lastTranslucent = Array.FindLastIndex (palette, c => c.A < 255);
		if (lastTranslucent >= 0)
			WriteChunk (png, "tRNS", palette.Take (lastTranslucent + 1).Select (c => c.A).ToArray ());

		using MemoryStream idat = new ();
		using (ZLibStream z = new (idat, CompressionLevel.SmallestSize, leaveOpen: true)) {
			for (int y = 0; y < height; y++) {
				z.WriteByte (0); // filter: none
				z.Write (indices, y * width, width);
			}
		}
		WriteChunk (png, "IDAT", idat.ToArray ());
		WriteChunk (png, "IEND", []);

		return png.ToArray ();
	}

	internal static void WriteChunk (Stream stream, string type, byte[] data)
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian (word, data.Length);
		stream.Write (word);

		byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes (type);
		stream.Write (typeBytes);
		stream.Write (data);

		uint crc = Crc32 (Crc32 (0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
		BinaryPrimitives.WriteUInt32BigEndian (word, crc);
		stream.Write (word);
	}

	private static readonly uint[] crc_table = Enumerable.Range (0, 256).Select (n => {
		uint c = (uint) n;
		for (int k = 0; k < 8; k++)
			c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
		return c;
	}).ToArray ();

	private static uint Crc32 (uint crc, byte[] data)
	{
		foreach (byte b in data)
			crc = crc_table[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc;
	}
}
