using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PaintDotNet;

/// <summary>
/// A 32-bit BGRA color with straight (not premultiplied) alpha, laid out in memory as B, G, R, A.
/// </summary>
[Serializable]
[StructLayout (LayoutKind.Explicit)]
public struct ColorBgra : IEquatable<ColorBgra>
{
	[FieldOffset (0)] public byte B;
	[FieldOffset (1)] public byte G;
	[FieldOffset (2)] public byte R;
	[FieldOffset (3)] public byte A;
	[FieldOffset (0)] public uint Bgra;

	public const int BlueChannel = 0;
	public const int GreenChannel = 1;
	public const int RedChannel = 2;
	public const int AlphaChannel = 3;
	public const int SizeOf = 4;
	public const int BitsPerPixel = 32;

	public byte this[int channel] {
		readonly get => channel switch {
			0 => B,
			1 => G,
			2 => R,
			3 => A,
			_ => throw new ArgumentOutOfRangeException (nameof (channel)),
		};
		set {
			switch (channel) {
				case 0: B = value; break;
				case 1: G = value; break;
				case 2: R = value; break;
				case 3: A = value; break;
				default: throw new ArgumentOutOfRangeException (nameof (channel));
			}
		}
	}

	public static ColorBgra FromBgra (byte b, byte g, byte r, byte a) => new () { B = b, G = g, R = r, A = a };
	public static ColorBgra FromBgr (byte b, byte g, byte r) => FromBgra (b, g, r, 255);
	public static ColorBgra FromUInt32 (uint bgra) => new () { Bgra = bgra };
	public static ColorBgra FromOpaqueInt32 (int bgr) => FromUInt32 ((uint) bgr | 0xff000000);
	public static int ToOpaqueInt32 (ColorBgra color) => (int) (color.Bgra & 0x00ffffff);
	public static uint BgraToUInt32 (byte b, byte g, byte r, byte a) => b | ((uint) g << 8) | ((uint) r << 16) | ((uint) a << 24);
	public static uint BgraToUInt32 (int b, int g, int r, int a) => BgraToUInt32 ((byte) b, (byte) g, (byte) r, (byte) a);

	public static ColorBgra FromBgraClamped (int b, int g, int r, int a)
		=> FromBgra (Int32Util.ClampToByte (b), Int32Util.ClampToByte (g), Int32Util.ClampToByte (r), Int32Util.ClampToByte (a));

	public static ColorBgra FromBgraClamped (float b, float g, float r, float a)
		=> FromBgraClamped ((int) MathF.Round (b), (int) MathF.Round (g), (int) MathF.Round (r), (int) MathF.Round (a));

	public static ColorBgra FromColor (Color c) => FromBgra (c.B, c.G, c.R, c.A);
	public readonly Color ToColor () => Color.FromArgb (A, R, G, B);

	public readonly ColorBgra NewAlpha (byte newA) => FromBgra (B, G, R, newA);

	/// <summary>Luma (Rec. 601 weights) in the range 0..1.</summary>
	public readonly double GetIntensity () => ((0.114 * B) + (0.587 * G) + (0.299 * R)) / 255.0;

	public readonly byte GetIntensityByte () => (byte) ((7471 * B + 38470 * G + 19595 * R) >> 16);

	public readonly ColorBgra ConvertToPremultipliedAlpha ()
		=> FromBgra ((byte) ((B * A + 127) / 255), (byte) ((G * A + 127) / 255), (byte) ((R * A + 127) / 255), A);

	public readonly ColorBgra ConvertFromPremultipliedAlpha ()
	{
		if (A == 0) return default;
		if (A == 255) return this;
		int h = A / 2;
		return FromBgra (
			(byte) Math.Min (255, (B * 255 + h) / A),
			(byte) Math.Min (255, (G * 255 + h) / A),
			(byte) Math.Min (255, (R * 255 + h) / A),
			A);
	}

	public static ColorBgra Lerp (ColorBgra from, ColorBgra to, float frac) => Lerp (from, to, (double) frac);
	public static ColorBgra Lerp (ColorBgra from, ColorBgra to, byte frac) => Lerp (from, to, frac / 255.0);

	public static ColorBgra Lerp (ColorBgra from, ColorBgra to, double frac)
	{
		static byte L (byte a, byte b, double t) => (byte) Math.Clamp ((int) Math.Round (a + (b - a) * t), 0, 255);
		return FromBgra (L (from.B, to.B, frac), L (from.G, to.G, frac), L (from.R, to.R, frac), L (from.A, to.A, frac));
	}

	/// <summary>Blends <paramref name="cb"/> over <paramref name="ca"/> by the weight <paramref name="cbAlpha"/>, weighting color by alpha.</summary>
	public static ColorBgra Blend (ColorBgra ca, ColorBgra cb, byte cbAlpha)
	{
		int wb = cbAlpha, wa = 255 - cbAlpha;
		int a = (ca.A * wa + cb.A * wb + 127) / 255;
		if (a == 0) return default;
		int aa = ca.A * wa, ab = cb.A * wb, sum = aa + ab;
		return FromBgra (
			(byte) ((ca.B * aa + cb.B * ab + sum / 2) / sum),
			(byte) ((ca.G * aa + cb.G * ab + sum / 2) / sum),
			(byte) ((ca.R * aa + cb.R * ab + sum / 2) / sum),
			(byte) a);
	}

	/// <summary>Alpha-weighted average of the colors.</summary>
	public static ColorBgra Blend (ColorBgra[] colors) => Blend (colors, 0, colors.Length);

	public static ColorBgra Blend (ColorBgra[] colors, int startIndex, int length)
	{
		long a = 0, b = 0, g = 0, r = 0;
		for (int i = startIndex; i < startIndex + length; i++) {
			ColorBgra c = colors[i];
			a += c.A; b += c.B * c.A; g += c.G * c.A; r += c.R * c.A;
		}
		if (a == 0 || length == 0) return default;
		return FromBgra ((byte) (b / a), (byte) (g / a), (byte) (r / a), (byte) (a / length));
	}

	public static unsafe ColorBgra Blend (ColorBgra* colors, int count)
	{
		ColorBgra[] copy = new ColorBgra[count];
		for (int i = 0; i < count; i++) copy[i] = colors[i];
		return Blend (copy);
	}

	public static ColorBgra BlendColorsWFP (ColorBgra[] c, double[] w)
	{
		double a = 0, b = 0, g = 0, r = 0;
		for (int i = 0; i < c.Length; i++) {
			double wa = w[i] * c[i].A;
			a += wa; b += c[i].B * wa; g += c[i].G * wa; r += c[i].R * wa;
		}
		if (a <= 0) return default;
		return FromBgraClamped ((int) (b / a + 0.5), (int) (g / a + 0.5), (int) (r / a + 0.5), (int) (a + 0.5));
	}

	public static ColorBgra BlendColorsWAIP (ColorBgra[] c, uint[] w)
	{
		double[] wd = new double[w.Length];
		long sum = 0;
		foreach (uint x in w) sum += x;
		for (int i = 0; i < w.Length; i++) wd[i] = sum == 0 ? 0 : (double) w[i] / sum;
		return BlendColorsWFP (c, wd);
	}

	public static ColorBgra BlendColors4W16IP (ColorBgra c1, uint w1, ColorBgra c2, uint w2, ColorBgra c3, uint w3, ColorBgra c4, uint w4)
		=> BlendColorsWAIP ([c1, c2, c3, c4], [w1, w2, w3, w4]);

	public static ColorBgra BlendColors4Fast (ColorBgra c1, ColorBgra c2, ColorBgra c3, ColorBgra c4)
		=> Blend ([c1, c2, c3, c4]);

	/// <summary>Draws y over x with y's alpha replaced by <paramref name="ya"/> (normal source-over).</summary>
	public static ColorBgra Overwrite (ColorBgra x, ColorBgra y, byte ya)
	{
		int sa = ya, da = x.A;
		int outA = sa + (da * (255 - sa) + 127) / 255;
		if (outA == 0) return default;
		int k = da * (255 - sa) / 255;
		return FromBgra (
			(byte) ((y.B * sa + x.B * k) / outA),
			(byte) ((y.G * sa + x.G * k) / outA),
			(byte) ((y.R * sa + x.R * k) / outA),
			(byte) outA);
	}

	public static ColorBgra ParseHexString (string hexString)
	{
		uint v = Convert.ToUInt32 (hexString.TrimStart ('#'), 16);
		return hexString.TrimStart ('#').Length <= 6 ? FromUInt32 (v | 0xff000000) : FromUInt32 (v);
	}

	public readonly string ToHexString () => Bgra.ToString ("X8");

	public static implicit operator Color (ColorBgra c) => c.ToColor ();
	public static implicit operator ColorBgra (Color c) => FromColor (c);
	public static explicit operator uint (ColorBgra color) => color.Bgra;
	public static explicit operator ColorBgra (uint uint32) => FromUInt32 (uint32);
	public static implicit operator Imaging.ColorBgra32 (ColorBgra c) => new () { Bgra = c.Bgra };
	public static implicit operator ColorBgra (Imaging.ColorBgra32 c) => FromUInt32 (c.Bgra);

	public static bool operator == (ColorBgra lhs, ColorBgra rhs) => lhs.Bgra == rhs.Bgra;
	public static bool operator != (ColorBgra lhs, ColorBgra rhs) => lhs.Bgra != rhs.Bgra;
	public readonly bool Equals (ColorBgra other) => Bgra == other.Bgra;
	public override readonly bool Equals (object obj) => obj is ColorBgra c && c.Bgra == Bgra;
	public override readonly int GetHashCode () => (int) Bgra;
	public override readonly string ToString () => $"B: {B}, G: {G}, R: {R}, A: {A}";

	public static ColorBgra Zero => default;
	public static ColorBgra TransparentBlack => default;
	public static ColorBgra Transparent => FromBgra (255, 255, 255, 0);
	public static ColorBgra AliceBlue => FromColor (Color.AliceBlue);
	public static ColorBgra AntiqueWhite => FromColor (Color.AntiqueWhite);
	public static ColorBgra Aquamarine => FromColor (Color.Aquamarine);
	public static ColorBgra Aqua => FromColor (Color.Aqua);
	public static ColorBgra Azure => FromColor (Color.Azure);
	public static ColorBgra Beige => FromColor (Color.Beige);
	public static ColorBgra Bisque => FromColor (Color.Bisque);
	public static ColorBgra Black => FromColor (Color.Black);
	public static ColorBgra BlanchedAlmond => FromColor (Color.BlanchedAlmond);
	public static ColorBgra Blue => FromColor (Color.Blue);
	public static ColorBgra BlueViolet => FromColor (Color.BlueViolet);
	public static ColorBgra Brown => FromColor (Color.Brown);
	public static ColorBgra BurlyWood => FromColor (Color.BurlyWood);
	public static ColorBgra CadetBlue => FromColor (Color.CadetBlue);
	public static ColorBgra Chartreuse => FromColor (Color.Chartreuse);
	public static ColorBgra Chocolate => FromColor (Color.Chocolate);
	public static ColorBgra Coral => FromColor (Color.Coral);
	public static ColorBgra CornflowerBlue => FromColor (Color.CornflowerBlue);
	public static ColorBgra Cornsilk => FromColor (Color.Cornsilk);
	public static ColorBgra Crimson => FromColor (Color.Crimson);
	public static ColorBgra Cyan => FromColor (Color.Cyan);
	public static ColorBgra DarkBlue => FromColor (Color.DarkBlue);
	public static ColorBgra DarkCyan => FromColor (Color.DarkCyan);
	public static ColorBgra DarkGoldenrod => FromColor (Color.DarkGoldenrod);
	public static ColorBgra DarkGray => FromColor (Color.DarkGray);
	public static ColorBgra DarkGreen => FromColor (Color.DarkGreen);
	public static ColorBgra DarkKhaki => FromColor (Color.DarkKhaki);
	public static ColorBgra DarkMagenta => FromColor (Color.DarkMagenta);
	public static ColorBgra DarkOliveGreen => FromColor (Color.DarkOliveGreen);
	public static ColorBgra DarkOrange => FromColor (Color.DarkOrange);
	public static ColorBgra DarkOrchid => FromColor (Color.DarkOrchid);
	public static ColorBgra DarkRed => FromColor (Color.DarkRed);
	public static ColorBgra DarkSalmon => FromColor (Color.DarkSalmon);
	public static ColorBgra DarkSeaGreen => FromColor (Color.DarkSeaGreen);
	public static ColorBgra DarkSlateBlue => FromColor (Color.DarkSlateBlue);
	public static ColorBgra DarkSlateGray => FromColor (Color.DarkSlateGray);
	public static ColorBgra DarkTurquoise => FromColor (Color.DarkTurquoise);
	public static ColorBgra DarkViolet => FromColor (Color.DarkViolet);
	public static ColorBgra DeepPink => FromColor (Color.DeepPink);
	public static ColorBgra DeepSkyBlue => FromColor (Color.DeepSkyBlue);
	public static ColorBgra DimGray => FromColor (Color.DimGray);
	public static ColorBgra DodgerBlue => FromColor (Color.DodgerBlue);
	public static ColorBgra Firebrick => FromColor (Color.Firebrick);
	public static ColorBgra FloralWhite => FromColor (Color.FloralWhite);
	public static ColorBgra ForestGreen => FromColor (Color.ForestGreen);
	public static ColorBgra Fuchsia => FromColor (Color.Fuchsia);
	public static ColorBgra Gainsboro => FromColor (Color.Gainsboro);
	public static ColorBgra GhostWhite => FromColor (Color.GhostWhite);
	public static ColorBgra Goldenrod => FromColor (Color.Goldenrod);
	public static ColorBgra Gold => FromColor (Color.Gold);
	public static ColorBgra Gray => FromColor (Color.Gray);
	public static ColorBgra Green => FromColor (Color.Green);
	public static ColorBgra GreenYellow => FromColor (Color.GreenYellow);
	public static ColorBgra Honeydew => FromColor (Color.Honeydew);
	public static ColorBgra HotPink => FromColor (Color.HotPink);
	public static ColorBgra IndianRed => FromColor (Color.IndianRed);
	public static ColorBgra Indigo => FromColor (Color.Indigo);
	public static ColorBgra Ivory => FromColor (Color.Ivory);
	public static ColorBgra Khaki => FromColor (Color.Khaki);
	public static ColorBgra LavenderBlush => FromColor (Color.LavenderBlush);
	public static ColorBgra Lavender => FromColor (Color.Lavender);
	public static ColorBgra LawnGreen => FromColor (Color.LawnGreen);
	public static ColorBgra LemonChiffon => FromColor (Color.LemonChiffon);
	public static ColorBgra LightBlue => FromColor (Color.LightBlue);
	public static ColorBgra LightCoral => FromColor (Color.LightCoral);
	public static ColorBgra LightCyan => FromColor (Color.LightCyan);
	public static ColorBgra LightGoldenrodYellow => FromColor (Color.LightGoldenrodYellow);
	public static ColorBgra LightGray => FromColor (Color.LightGray);
	public static ColorBgra LightGreen => FromColor (Color.LightGreen);
	public static ColorBgra LightPink => FromColor (Color.LightPink);
	public static ColorBgra LightSalmon => FromColor (Color.LightSalmon);
	public static ColorBgra LightSeaGreen => FromColor (Color.LightSeaGreen);
	public static ColorBgra LightSkyBlue => FromColor (Color.LightSkyBlue);
	public static ColorBgra LightSlateGray => FromColor (Color.LightSlateGray);
	public static ColorBgra LightSteelBlue => FromColor (Color.LightSteelBlue);
	public static ColorBgra LightYellow => FromColor (Color.LightYellow);
	public static ColorBgra LimeGreen => FromColor (Color.LimeGreen);
	public static ColorBgra Lime => FromColor (Color.Lime);
	public static ColorBgra Linen => FromColor (Color.Linen);
	public static ColorBgra Magenta => FromColor (Color.Magenta);
	public static ColorBgra Maroon => FromColor (Color.Maroon);
	public static ColorBgra MediumAquamarine => FromColor (Color.MediumAquamarine);
	public static ColorBgra MediumBlue => FromColor (Color.MediumBlue);
	public static ColorBgra MediumOrchid => FromColor (Color.MediumOrchid);
	public static ColorBgra MediumPurple => FromColor (Color.MediumPurple);
	public static ColorBgra MediumSeaGreen => FromColor (Color.MediumSeaGreen);
	public static ColorBgra MediumSlateBlue => FromColor (Color.MediumSlateBlue);
	public static ColorBgra MediumSpringGreen => FromColor (Color.MediumSpringGreen);
	public static ColorBgra MediumTurquoise => FromColor (Color.MediumTurquoise);
	public static ColorBgra MediumVioletRed => FromColor (Color.MediumVioletRed);
	public static ColorBgra MidnightBlue => FromColor (Color.MidnightBlue);
	public static ColorBgra MintCream => FromColor (Color.MintCream);
	public static ColorBgra MistyRose => FromColor (Color.MistyRose);
	public static ColorBgra Moccasin => FromColor (Color.Moccasin);
	public static ColorBgra NavajoWhite => FromColor (Color.NavajoWhite);
	public static ColorBgra Navy => FromColor (Color.Navy);
	public static ColorBgra OldLace => FromColor (Color.OldLace);
	public static ColorBgra OliveDrab => FromColor (Color.OliveDrab);
	public static ColorBgra Olive => FromColor (Color.Olive);
	public static ColorBgra OrangeRed => FromColor (Color.OrangeRed);
	public static ColorBgra Orange => FromColor (Color.Orange);
	public static ColorBgra Orchid => FromColor (Color.Orchid);
	public static ColorBgra PaleGoldenrod => FromColor (Color.PaleGoldenrod);
	public static ColorBgra PaleGreen => FromColor (Color.PaleGreen);
	public static ColorBgra PaleTurquoise => FromColor (Color.PaleTurquoise);
	public static ColorBgra PaleVioletRed => FromColor (Color.PaleVioletRed);
	public static ColorBgra PapayaWhip => FromColor (Color.PapayaWhip);
	public static ColorBgra PeachPuff => FromColor (Color.PeachPuff);
	public static ColorBgra Peru => FromColor (Color.Peru);
	public static ColorBgra Pink => FromColor (Color.Pink);
	public static ColorBgra Plum => FromColor (Color.Plum);
	public static ColorBgra PowderBlue => FromColor (Color.PowderBlue);
	public static ColorBgra Purple => FromColor (Color.Purple);
	public static ColorBgra Red => FromColor (Color.Red);
	public static ColorBgra RosyBrown => FromColor (Color.RosyBrown);
	public static ColorBgra RoyalBlue => FromColor (Color.RoyalBlue);
	public static ColorBgra SaddleBrown => FromColor (Color.SaddleBrown);
	public static ColorBgra Salmon => FromColor (Color.Salmon);
	public static ColorBgra SandyBrown => FromColor (Color.SandyBrown);
	public static ColorBgra SeaGreen => FromColor (Color.SeaGreen);
	public static ColorBgra SeaShell => FromColor (Color.SeaShell);
	public static ColorBgra Sienna => FromColor (Color.Sienna);
	public static ColorBgra Silver => FromColor (Color.Silver);
	public static ColorBgra SkyBlue => FromColor (Color.SkyBlue);
	public static ColorBgra SlateBlue => FromColor (Color.SlateBlue);
	public static ColorBgra SlateGray => FromColor (Color.SlateGray);
	public static ColorBgra Snow => FromColor (Color.Snow);
	public static ColorBgra SpringGreen => FromColor (Color.SpringGreen);
	public static ColorBgra SteelBlue => FromColor (Color.SteelBlue);
	public static ColorBgra Tan => FromColor (Color.Tan);
	public static ColorBgra Teal => FromColor (Color.Teal);
	public static ColorBgra Thistle => FromColor (Color.Thistle);
	public static ColorBgra Tomato => FromColor (Color.Tomato);
	public static ColorBgra Turquoise => FromColor (Color.Turquoise);
	public static ColorBgra Violet => FromColor (Color.Violet);
	public static ColorBgra Wheat => FromColor (Color.Wheat);
	public static ColorBgra WhiteSmoke => FromColor (Color.WhiteSmoke);
	public static ColorBgra White => FromColor (Color.White);
	public static ColorBgra YellowGreen => FromColor (Color.YellowGreen);
	public static ColorBgra Yellow => FromColor (Color.Yellow);
}
