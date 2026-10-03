using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace PaintDotNet.ComponentModel
{
	public class RefTrackedObject : IDisposable
	{
		public RefTrackedObject () { }

		public bool IsDisposed { get; private set; }

		public void Dispose ()
		{
			if (IsDisposed) return;
			IsDisposed = true;
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing) { }

		~RefTrackedObject ()
		{
			// Plugin cleanup code runs here; an exception on the finalizer thread would end the process.
			try {
				if (!IsDisposed) Dispose (false);
			} catch (Exception) {
			}
		}
	}
}

namespace PaintDotNet
{
	public abstract class Disposable : IDisposable
	{
		protected Disposable () { }

		public bool IsDisposed { get; private set; }

		public void Dispose ()
		{
			if (IsDisposed) return;
			IsDisposed = true;
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing) { }

		~Disposable ()
		{
			try {
				if (!IsDisposed) Dispose (false);
			} catch (Exception) {
			}
		}
	}

	public interface IPluginSupportInfo
	{
		string Author { get; }
		string Copyright { get; }
		string DisplayName { get; }
		Version Version { get; }
		Uri WebsiteUri { get; }
	}

	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
	public class PluginSupportInfoAttribute : Attribute, IPluginSupportInfo
	{
		public PluginSupportInfoAttribute () { }

		public PluginSupportInfoAttribute (Type pluginSupportInfoOrProviderType)
		{
			PluginSupportInfoType = pluginSupportInfoOrProviderType;
		}

		public PluginSupportInfoAttribute (string displayName, string author, string copyright, Version version, Uri websiteUri)
		{
			DisplayName = displayName;
			Author = author;
			Copyright = copyright;
			Version = version;
			WebsiteUri = websiteUri;
		}

		/// <summary>The type given to the constructor, which implements <see cref="IPluginSupportInfo"/>.</summary>
		public Type PluginSupportInfoType { get; }

		public string Author { get; set; }
		public string Copyright { get; set; }
		public string DisplayName { get; set; }
		public Version Version { get; set; }
		public Uri WebsiteUri { get; set; }
	}

	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
	public sealed class PluginSupportInfoAttribute<T> : PluginSupportInfoAttribute where T : IPluginSupportInfo, new()
	{
		public PluginSupportInfoAttribute () : base (typeof (T)) { }
	}

	public readonly struct Pair<T1, T2> : IEquatable<Pair<T1, T2>>
	{
		public Pair (T1 first, T2 second)
		{
			First = first;
			Second = second;
		}

		public T1 First { get; }
		public T2 Second { get; }

		public bool Equals (Pair<T1, T2> other) => EqualityComparer<T1>.Default.Equals (First, other.First) && EqualityComparer<T2>.Default.Equals (Second, other.Second);
		public override bool Equals (object obj) => obj is Pair<T1, T2> p && Equals (p);
		public override int GetHashCode () => HashCode.Combine (First, Second);
		public static bool operator == (Pair<T1, T2> lhs, Pair<T1, T2> rhs) => lhs.Equals (rhs);
		public static bool operator != (Pair<T1, T2> lhs, Pair<T1, T2> rhs) => !lhs.Equals (rhs);
		public static implicit operator Tuple<T1, T2> (Pair<T1, T2> from) => Tuple.Create (from.First, from.Second);
		public static implicit operator (T1, T2) (Pair<T1, T2> from) => (from.First, from.Second);
		public static implicit operator Pair<T1, T2> (Tuple<T1, T2> from) => new (from.Item1, from.Item2);
		public static implicit operator Pair<T1, T2> ((T1, T2) from) => new (from.Item1, from.Item2);
		public override string ToString () => $"({First}, {Second})";
	}

	public static class Pair
	{
		public static Pair<T, U> Create<T, U> (T first, U second) => new (first, second);
	}

	public abstract class ImageResource
	{
		private Image image;
		private static long next_id;

		protected ImageResource () { ID = System.Threading.Interlocked.Increment (ref next_id); }

		public long ID { get; }
		public Image Reference => image ??= Load ();
		public Image GetCopy () => new Bitmap (Reference);
		protected abstract Image Load ();

		public static ImageResource FromImage (Image image) => new FixedImageResource (image);

		private sealed class FixedImageResource (Image fixedImage) : ImageResource
		{
			protected override Image Load () => fixedImage;
		}
	}

	public static class ServiceProviderExtensions
	{
		public static TService GetService<TService> (this IServiceProvider serviceProvider) where TService : class
			=> serviceProvider?.GetService (typeof (TService)) as TService;

		public static void AssertService<TService> (this IServiceProvider serviceProvider) where TService : class
		{
			if (GetService<TService> (serviceProvider) is null)
				throw new InvalidOperationException ($"Service {typeof (TService)} is not available");
		}
	}

	public static class Int32Util
	{
		public static byte ClampToByte (int x) => x > 255 ? (byte) 255 : x < 0 ? (byte) 0 : (byte) x;
		public static int ClampSafe (int value, int bound1, int bound2) => Math.Clamp (value, Math.Min (bound1, bound2), Math.Max (bound1, bound2));
		public static bool IsClamped (int value, int min, int max) => value >= min && value <= max;
		public static bool SafeIsClamped (int value, int extreme1, int extreme2) => IsClamped (value, Math.Min (extreme1, extreme2), Math.Max (extreme1, extreme2));
		public static int Max (int val0, int val1, int val2) => Math.Max (val0, Math.Max (val1, val2));
		public static int Max (int val0, int val1, int val2, int val3) => Math.Max (Math.Max (val0, val1), Math.Max (val2, val3));
		public static int Max (int val0, params int[] vals) => Math.Max (val0, vals.Length == 0 ? val0 : vals.Max ());
		public static int Min (int val0, int val1, int val2) => Math.Min (val0, Math.Min (val1, val2));
		public static int Min (int val0, int val1, int val2, int val3) => Math.Min (Math.Min (val0, val1), Math.Min (val2, val3));
		public static int Min (int val0, params int[] vals) => Math.Min (val0, vals.Length == 0 ? val0 : vals.Min ());
		public static int GreatestCommonDivisor (int a, int b) => b == 0 ? Math.Abs (a) : GreatestCommonDivisor (b, a % b);
		public static int RoundUpN (int x, int n) => (x + n - 1) / n * n;
		public static int RoundDownN (int x, int n) => x / n * n;
		public static int Log2 (int x) => System.Numerics.BitOperations.Log2 ((uint) x);
		public static int Log2RoundUp (int x) => x <= 1 ? 0 : Log2 (x - 1) + 1;
		public static int Pow2RoundUp (int x) => (int) System.Numerics.BitOperations.RoundUpToPowerOf2 ((uint) x);
		public static int Pow2RoundDown (int x) => x <= 0 ? 0 : 1 << Log2 (x);
		public static int RotateLeft (int x, int count) => (int) System.Numerics.BitOperations.RotateLeft ((uint) x, count);
		public static int RotateRight (int x, int count) => (int) System.Numerics.BitOperations.RotateRight ((uint) x, count);
		public static int IncrementRoundUp (int x, int i) => RoundUpN (x + 1, i);
		public static int DivLog2RoundDown (int x, int log2N) => x >> log2N;
		public static int DivLog2RoundUp (int x, int log2N) => (x + (1 << log2N) - 1) >> log2N;
		public static int RoundDownLog2N (int x, int log2N) => x >> log2N << log2N;
		public static int RoundUpLog2N (int x, int log2N) => DivLog2RoundUp (x, log2N) << log2N;
	}

	public static class DoubleUtil
	{
		public static double Clamp (double x, double min, double max) => x < min ? min : x > max ? max : x;
		public static double Lerp (double from, double to, double frac) => from + (to - from) * frac;
		public static bool IsFinite (double x) => double.IsFinite (x);
		public static byte ClampToByte (double x) => (byte) Clamp (x, 0, 255);
	}

	public static class FloatUtil
	{
		public static float Clamp (float x, float min, float max) => x < min ? min : x > max ? max : x;
		public static float Lerp (float from, float to, float frac) => from + (to - from) * frac;
		public static byte ClampToByte (float x) => (byte) Clamp (x, 0, 255);
	}

	public static class MathUtil
	{
		public static double DegreesToRadians (double degrees) => degrees * Math.PI / 180.0;
		public static float DegreesToRadians (float degrees) => degrees * MathF.PI / 180.0f;
		public static double RadiansToDegrees (double radians) => radians * 180.0 / Math.PI;
		public static float RadiansToDegrees (float radians) => radians * 180.0f / MathF.PI;
		public static double Lerp (double from, double to, double frac) => from + (to - from) * frac;
		public static float Lerp (float from, float to, float frac) => from + (to - from) * frac;
	}

	public static unsafe class BufferUtil
	{
		public static void Copy (void* dst, void* src, int length) => Buffer.MemoryCopy (src, dst, length, length);
		public static void Copy (void* dst, void* src, long length) => Buffer.MemoryCopy (src, dst, length, length);
		public static void Clear (void* dst, int length) => NativeMemory.Clear (dst, (nuint) length);
	}

	/// <summary>The UI scale (DPI / 96). Pinta reports 1.0: plugin UI is drawn by GTK, not by the plugin.</summary>
	public readonly struct UIScaleFactor : IEquatable<UIScaleFactor>, IComparable<UIScaleFactor>
	{
		private UIScaleFactor (int dpi) { Dpi = dpi; }
		public static UIScaleFactor Current => new (96);
		public static UIScaleFactor Legacy => new (96);
		public static UIScaleFactor Minimum => new (96);
		public static UIScaleFactor Maximum => new (384);
		public static UIScaleFactor FromDpi (int dpi) => new (dpi);
		public static UIScaleFactor FromScale (double scale) => new ((int) Math.Round (scale * 96));
		public int Dpi { get; }
		public double Scale => Dpi / 96.0;
		public int ConvertDipsToPixelsInt (int dips) => (int) Math.Round (dips * Scale);
		public double ConvertDipsToPixels (double dips) => dips * Scale;
		public float ConvertDipsToPixels (float dips) => (float) (dips * Scale);
		public int ConvertPixelsToDipsInt (int px) => (int) Math.Round (px / Scale);
		public double ConvertPixelsToDips (double px) => px / Scale;
		public float ConvertPixelsToDips (float px) => (float) (px / Scale);
		public static double ConvertFontPointsToDips (double points) => points * 96 / 72;
		public static float ConvertFontPointsToDips (float points) => points * 96 / 72;
		public double ConvertFontPointsToPixels (double points) => ConvertDipsToPixels (ConvertFontPointsToDips (points));
		public float ConvertFontPointsToPixels (float points) => ConvertDipsToPixels (ConvertFontPointsToDips (points));
		public int ScaleScalar (int x) => ConvertDipsToPixelsInt (x);
		public double ScaleScalar (double x) => x * Scale;
		public Size ScaleSize (Size size) => new (ScaleScalar (size.Width), ScaleScalar (size.Height));
		public int ScaleWidth (int width) => ScaleScalar (width);
		public int ScaleHeight (int height) => ScaleScalar (height);
		public int CompareTo (UIScaleFactor other) => Dpi.CompareTo (other.Dpi);
		public bool Equals (UIScaleFactor other) => Dpi == other.Dpi;
		public override bool Equals (object obj) => obj is UIScaleFactor u && Equals (u);
		public override int GetHashCode () => Dpi;
	}

	public struct RgbColor
	{
		public int Red;
		public int Green;
		public int Blue;

		public RgbColor (int r, int g, int b)
		{
			Red = r;
			Green = g;
			Blue = b;
		}

		public Color ToColor () => Color.FromArgb (Red, Green, Blue);
		public HsvColor ToHsv () => HsvColor.FromColor (ToColor ());
	}

	/// <summary>HSV with hue 0..360 and saturation and value 0..100.</summary>
	public struct HsvColor : IEquatable<HsvColor>
	{
		public int Hue;
		public int Saturation;
		public int Value;

		public HsvColor (int hue, int saturation, int value)
		{
			Hue = hue;
			Saturation = saturation;
			Value = value;
		}

		public static HsvColor FromColor (Color color)
		{
			HsvColorF f = HsvColorF.FromRgb (color.R, color.G, color.B);
			return new HsvColor ((int) Math.Round (f.Hue) % 360, (int) Math.Round (f.Saturation), (int) Math.Round (f.Value));
		}

		public Color ToColor ()
		{
			(byte r, byte g, byte b) = new HsvColorF (Hue, Saturation, Value).ToRgb ();
			return Color.FromArgb (r, g, b);
		}

		public RgbColor ToRgb ()
		{
			Color c = ToColor ();
			return new RgbColor (c.R, c.G, c.B);
		}

		public bool Equals (HsvColor other) => Hue == other.Hue && Saturation == other.Saturation && Value == other.Value;
		public override bool Equals (object obj) => obj is HsvColor h && Equals (h);
		public override int GetHashCode () => HashCode.Combine (Hue, Saturation, Value);
		public static bool operator == (HsvColor a, HsvColor b) => a.Equals (b);
		public static bool operator != (HsvColor a, HsvColor b) => !a.Equals (b);
	}

	/// <summary>HSV with hue 0..360 and saturation and value 0..100, as doubles.</summary>
	public readonly struct HsvColorF
	{
		public HsvColorF (double hue, double saturation, double value)
		{
			Hue = hue;
			Saturation = saturation;
			Value = value;
		}

		public double Hue { get; }
		public double Saturation { get; }
		public double Value { get; }

		public static HsvColorF FromRgb (byte r, byte g, byte b)
		{
			double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
			double max = Math.Max (rf, Math.Max (gf, bf)), min = Math.Min (rf, Math.Min (gf, bf)), d = max - min;
			double h = 0;
			if (d > 0) {
				if (max == rf) h = 60 * (((gf - bf) / d) % 6);
				else if (max == gf) h = 60 * (((bf - rf) / d) + 2);
				else h = 60 * (((rf - gf) / d) + 4);
			}
			if (h < 0) h += 360;
			return new HsvColorF (h, max == 0 ? 0 : d / max * 100, max * 100);
		}

		public static HsvColorF FromColor (Color c) => FromRgb (c.R, c.G, c.B);

		public (byte r, byte g, byte b) ToRgb ()
		{
			double s = Saturation / 100.0, v = Value / 100.0, h = ((Hue % 360) + 360) % 360 / 60.0;
			double c = v * s, x = c * (1 - Math.Abs (h % 2 - 1)), m = v - c;
			(double r, double g, double b) = (int) h switch {
				0 => (c, x, 0.0),
				1 => (x, c, 0.0),
				2 => (0.0, c, x),
				3 => (0.0, x, c),
				4 => (x, 0.0, c),
				_ => (c, 0.0, x),
			};
			static byte B (double d) => (byte) Math.Clamp ((int) Math.Round (d * 255), 0, 255);
			return (B (r + m), B (g + m), B (b + m));
		}

		public Color ToColor ()
		{
			(byte r, byte g, byte b) = ToRgb ();
			return Color.FromArgb (r, g, b);
		}

		public RgbColorF ToRgbColorF ()
		{
			(byte r, byte g, byte b) = ToRgb ();
			return new RgbColorF (r / 255.0, g / 255.0, b / 255.0);
		}
	}

	/// <summary>RGB with channels 0..1, as doubles.</summary>
	public readonly struct RgbColorF
	{
		public RgbColorF (double red, double green, double blue)
		{
			Red = red;
			Green = green;
			Blue = blue;
		}

		public double Red { get; }
		public double Green { get; }
		public double Blue { get; }

		public HsvColorF ToHsvColorF ()
		{
			static byte B (double d) => (byte) Math.Clamp ((int) Math.Round (d * 255), 0, 255);
			return HsvColorF.FromRgb (B (Red), B (Green), B (Blue));
		}
	}
}

namespace PaintDotNet.Collections
{
	public static class EnumerableExtensions
	{
		public static int IndexOf<T> (this IEnumerable<T> source, T value)
		{
			int i = 0;
			foreach (T item in source) {
				if (EqualityComparer<T>.Default.Equals (item, value)) return i;
				i++;
			}
			return -1;
		}

		public static T[] ToArrayEx<T> (this IEnumerable<T> source) => source.ToArray ();

		public static int FirstIndexWhere<T> (this IEnumerable<T> source, Func<T, bool> predicate)
		{
			int i = 0;
			foreach (T item in source) {
				if (predicate (item)) return i;
				i++;
			}
			return -1;
		}
	}

	public static class ListExtensions
	{
		public static IList<TResult> Select<T, TResult> (this IList<T> source, Func<T, TResult> selector) => Enumerable.Select (source, selector).ToList ();
	}
}
