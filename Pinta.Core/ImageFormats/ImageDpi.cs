using System;
using System.Buffers.Binary;
using System.IO;

namespace Pinta.Core;

/// <summary>
/// Reads and writes the resolution stored in PNG (pHYs) and JPEG (JFIF density) files.
/// gdk-pixbuf's glycin loaders and savers ignore it.
/// </summary>
public static class ImageDpi
{
	private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>The resolution in pixels per inch, or null when the file does not state one.</summary>
	public static double? Read (ReadOnlySpan<byte> data)
	{
		if (data.StartsWith (PngSignature)) {
			int at = PngSignature.Length;
			while (at + 8 <= data.Length) {
				int length = BinaryPrimitives.ReadInt32BigEndian (data[at..]);
				ReadOnlySpan<byte> type = data.Slice (at + 4, 4);
				if (type.SequenceEqual ("IDAT"u8) || length < 0)
					return null;
				if (type.SequenceEqual ("pHYs"u8) && length >= 9 && at + 17 <= data.Length) {
					uint pixelsPerUnit = BinaryPrimitives.ReadUInt32BigEndian (data[(at + 8)..]);
					bool metres = data[at + 16] == 1;
					return metres && pixelsPerUnit > 0 ? SnapToWhole (pixelsPerUnit * 0.0254) : null;
				}
				at += 12 + length;
			}
			return null;
		}

		if (FindJfif (data) is int jfif) {
			byte units = data[jfif + 11];
			int density = BinaryPrimitives.ReadUInt16BigEndian (data[(jfif + 12)..]);
			if (density == 0)
				return null;
			return units switch {
				1 => density,
				2 => density * PrintSize.CentimetersPerInch,
				_ => null,
			};
		}

		return null;
	}

	/// <summary>
	/// PNG stores whole pixels per metre, so 96 DPI reads back as 96.012: show it as 96.
	/// </summary>
	private static double SnapToWhole (double dpi)
		=> Math.Abs (dpi - Math.Round (dpi)) < 0.02 ? Math.Round (dpi) : dpi;

	/// <summary>The file with its resolution set; other formats come back unchanged.</summary>
	public static byte[] Write (byte[] data, double dpi)
	{
		if (data.AsSpan ().StartsWith (PngSignature))
			return WritePng (data, dpi);
		if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8)
			return WriteJpeg (data, dpi);
		return data;
	}

	private static byte[] WritePng (byte[] data, double dpi)
	{
		using MemoryStream result = new ();
		result.Write (PngSignature);

		byte[] phys = new byte[9];
		uint pixelsPerMetre = (uint) Math.Round (dpi / 0.0254);
		BinaryPrimitives.WriteUInt32BigEndian (phys, pixelsPerMetre);
		BinaryPrimitives.WriteUInt32BigEndian (phys.AsSpan (4), pixelsPerMetre);
		phys[8] = 1; // metres

		int at = PngSignature.Length;
		while (at + 12 <= data.Length) {
			int length = BinaryPrimitives.ReadInt32BigEndian (data.AsSpan (at));
			string type = System.Text.Encoding.ASCII.GetString (data, at + 4, 4);
			if (type != "pHYs")
				result.Write (data, at, 12 + length);
			if (type == "IHDR")
				IndexedPng.WriteChunk (result, "pHYs", phys);
			at += 12 + length;
		}

		return result.ToArray ();
	}

	private static byte[] WriteJpeg (byte[] data, double dpi)
	{
		ushort density = (ushort) Math.Clamp (Math.Round (dpi), 1, ushort.MaxValue);

		if (FindJfif (data) is int jfif) {
			byte[] copy = (byte[]) data.Clone ();
			copy[jfif + 11] = 1; // dots per inch
			BinaryPrimitives.WriteUInt16BigEndian (copy.AsSpan (jfif + 12), density);
			BinaryPrimitives.WriteUInt16BigEndian (copy.AsSpan (jfif + 14), density);
			return copy;
		}

		// No JFIF header: add one straight after the start-of-image marker.
		byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, (byte) 'J', (byte) 'F', (byte) 'I', (byte) 'F', 0, 1, 2, 1, 0, 0, 0, 0, 0, 0];
		BinaryPrimitives.WriteUInt16BigEndian (app0.AsSpan (12), density);
		BinaryPrimitives.WriteUInt16BigEndian (app0.AsSpan (14), density);
		return [.. data.AsSpan (0, 2), .. app0, .. data.AsSpan (2)];
	}

	/// <summary>The offset of the JFIF APP0 marker when it follows the start-of-image marker.</summary>
	private static int? FindJfif (ReadOnlySpan<byte> data)
	{
		const int at = 2;
		if (data.Length < at + 16 || data[0] != 0xFF || data[1] != 0xD8)
			return null;
		if (data[at] != 0xFF || data[at + 1] != 0xE0 || !data.Slice (at + 4, 5).SequenceEqual ("JFIF\0"u8))
			return null;
		return at;
	}
}
