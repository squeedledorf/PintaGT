using System;

namespace Pinta.PdnPlugins;

/// <summary>
/// Converts between Cairo's ARGB32 (premultiplied alpha) and Paint.NET's ColorBgra (straight alpha).
/// Both are BGRA in memory on little-endian machines, as 0xAARRGGBB words.
/// </summary>
public static class PixelConvert
{
	/// <summary>Premultiplied to straight: c * 255 / a, rounded and clamped. Fully transparent pixels become 0.</summary>
	public static void ToStraight (ReadOnlySpan<uint> premultiplied, Span<uint> straight)
	{
		for (int i = 0; i < premultiplied.Length; i++) {
			uint p = premultiplied[i];
			uint a = p >> 24;
			if (a == 255) {
				straight[i] = p;
			} else if (a == 0) {
				straight[i] = 0;
			} else {
				uint half = a / 2;
				uint b = Math.Min (255, ((p & 0xff) * 255 + half) / a);
				uint g = Math.Min (255, (((p >> 8) & 0xff) * 255 + half) / a);
				uint r = Math.Min (255, (((p >> 16) & 0xff) * 255 + half) / a);
				straight[i] = (a << 24) | (r << 16) | (g << 8) | b;
			}
		}
	}

	/// <summary>Straight to premultiplied: (c * a + 127) / 255.</summary>
	public static void ToPremultiplied (ReadOnlySpan<uint> straight, Span<uint> premultiplied)
	{
		for (int i = 0; i < straight.Length; i++) {
			uint p = straight[i];
			uint a = p >> 24;
			if (a == 255) {
				premultiplied[i] = p;
			} else if (a == 0) {
				premultiplied[i] = 0;
			} else {
				uint b = ((p & 0xff) * a + 127) / 255;
				uint g = (((p >> 8) & 0xff) * a + 127) / 255;
				uint r = (((p >> 16) & 0xff) * a + 127) / 255;
				premultiplied[i] = (a << 24) | (r << 16) | (g << 8) | b;
			}
		}
	}
}
