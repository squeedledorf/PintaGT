using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using PaintDotNet;
using PaintDotNet.Hosting;

// A small subset of System.Drawing (GDI+) for plugins: images are straight-alpha BGRA buffers.
// Rectangle, Point, Size and Color come from .NET's own System.Drawing.Primitives.
namespace System.Drawing
{
	public abstract class Image : IDisposable, ICloneable
	{
		private protected Image () { }

		/// <summary>The original encoded file bytes, when the image was loaded from a file or resource.</summary>
		internal byte[] EncodedData { get; set; }

		public abstract int Width { get; }
		public abstract int Height { get; }
		public Size Size => new (Width, Height);
		public PixelFormat PixelFormat => PixelFormat.Format32bppArgb;
		public float HorizontalResolution => 96;
		public float VerticalResolution => 96;
		public RectangleF GetBounds (ref GraphicsUnit pageUnit) => new (0, 0, Width, Height);
		public object Tag { get; set; }

		/// <summary>Bits per pixel, encoded in bits 8..15 of the GDI+ pixel format value.</summary>
		public static int GetPixelFormatSize (PixelFormat pixfmt) => ((int) pixfmt >> 8) & 0xff;
		public static bool IsAlphaPixelFormat (PixelFormat pixfmt) => (pixfmt & PixelFormat.Alpha) != 0;

		public static Image FromStream (Stream stream) => new Bitmap (stream);
		public static Image FromStream (Stream stream, bool useEmbeddedColorManagement) => new Bitmap (stream);
		public static Image FromStream (Stream stream, bool useEmbeddedColorManagement, bool validateImageData) => new Bitmap (stream);
		public static Image FromFile (string filename) => new Bitmap (filename);
		public static Image FromFile (string filename, bool useEmbeddedColorManagement) => new Bitmap (filename);

		public Image GetThumbnailImage (int thumbWidth, int thumbHeight, GetThumbnailImageAbort callback, IntPtr callbackData)
			=> new Bitmap (this, new Size (thumbWidth, thumbHeight));

		public delegate bool GetThumbnailImageAbort ();

		public void Save (string filename) => File.WriteAllBytes (filename, ((Bitmap) this).EncodePng ());
		public void Save (string filename, ImageFormat format) => Save (filename);
		public void Save (Stream stream, ImageFormat format) => stream.Write (((Bitmap) this).EncodePng ());

		public object Clone () => new Bitmap (this);

		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing) { }
	}

	/// <summary>A straight-alpha 32-bit BGRA bitmap in native memory (Format32bppArgb layout).</summary>
	public sealed unsafe class Bitmap : Image
	{
		private MemoryBlock memory; // null when aliasing someone else's memory
#pragma warning disable CS0414 // only held to keep an aliased surface alive
		private object alias_owner;
#pragma warning restore CS0414
		private nint scan0;
		private int width, height, stride;
		private bool decoded;

		public Bitmap (int width, int height) => Allocate (width, height);
		public Bitmap (int width, int height, PixelFormat format) => Allocate (width, height);
		public Bitmap (int width, int height, Graphics g) => Allocate (width, height);

		public Bitmap (int width, int height, int stride, PixelFormat format, IntPtr scan0)
		{
			if (format == PixelFormat.Format32bppArgb || format == PixelFormat.Format32bppRgb) {
				this.width = width;
				this.height = height;
				this.stride = stride;
				this.scan0 = scan0;
				decoded = true;
			} else {
				Allocate (width, height);
			}
		}

		public Bitmap (Image original) : this (original, original.Size) { }
		public Bitmap (Image original, int width, int height) : this (original, new Size (width, height)) { }

		public Bitmap (Image original, Size newSize)
		{
			Bitmap src = (Bitmap) original;
			Allocate (newSize.Width, newSize.Height);
			if (newSize == src.Size) {
				for (int y = 0; y < height; y++)
					Buffer.MemoryCopy ((byte*) src.Scan0 + (long) y * src.stride, (byte*) scan0 + (long) y * stride, stride, width * 4L);
				EncodedData = src.EncodedData;
			} else {
				using Graphics g = Graphics.FromImage (this);
				g.DrawImage (src, new Rectangle (0, 0, newSize.Width, newSize.Height));
			}
		}

		public Bitmap (Stream stream) : this (ReadAll (stream)) { }
		public Bitmap (Stream stream, bool useIcm) : this (stream) { }
		public Bitmap (string filename) : this (File.ReadAllBytes (filename)) { }
		public Bitmap (string filename, bool useIcm) : this (filename) { }

		/// <summary>Loads a resource named <c>{type.Namespace}.{resource}</c> from the type's assembly.</summary>
		public Bitmap (Type type, string resource) : this (ReadResource (type, resource)) { }

		private Bitmap (byte[] encoded)
		{
			EncodedData = encoded;
		}

		internal static Bitmap CreateAlias (nint scan0, int width, int height, int stride, object owner)
			=> new (width, height, stride, PixelFormat.Format32bppArgb, scan0) { alias_owner = owner };

		private static byte[] ReadAll (Stream stream)
		{
			using MemoryStream ms = new ();
			stream.CopyTo (ms);
			return ms.ToArray ();
		}

		private static byte[] ReadResource (Type type, string resource)
		{
			Assembly asm = type.Assembly;
			Stream s = asm.GetManifestResourceStream (type, resource)
				?? asm.GetManifestResourceStream (resource);
			if (s is null) {
				string match = asm.GetManifestResourceNames ().FirstOrDefault (n => n.EndsWith ("." + resource, StringComparison.OrdinalIgnoreCase) || n.Equals (resource, StringComparison.OrdinalIgnoreCase));
				s = match is null ? null : asm.GetManifestResourceStream (match);
			}
			if (s is null)
				throw new ArgumentException ($"Resource '{resource}' not found in {asm.GetName ().Name}");
			using (s)
				return ReadAll (s);
		}

		private void Allocate (int w, int h)
		{
			if (w <= 0 || h <= 0) throw new ArgumentException ("Invalid bitmap size");
			width = w;
			height = h;
			stride = w * 4;
			memory = new MemoryBlock ((long) stride * h);
			scan0 = memory.Pointer;
			decoded = true;
		}

		private void EnsureDecoded ()
		{
			if (decoded) return;
			DecodedImage img = ShimHost.DecodeImage?.Invoke (EncodedData)
				?? throw new ArgumentException ("Unsupported or invalid image data");
			Allocate (img.Width, img.Height);
			Marshal.Copy (img.Bgra, 0, scan0, img.Bgra.Length);
		}

		public override int Width { get { EnsureDecoded (); return width; } }
		public override int Height { get { EnsureDecoded (); return height; } }

		internal nint Scan0 { get { EnsureDecoded (); return scan0; } }
		internal int Stride { get { EnsureDecoded (); return stride; } }

		internal ColorBgra* Row (int y) => (ColorBgra*) ((byte*) Scan0 + (long) y * Stride);

		public Color GetPixel (int x, int y)
		{
			Check (x, y);
			return Row (y)[x].ToColor ();
		}

		public void SetPixel (int x, int y, Color color)
		{
			Check (x, y);
			Row (y)[x] = ColorBgra.FromColor (color);
			EncodedData = null;
		}

		private void Check (int x, int y)
		{
			if ((uint) x >= (uint) Width || (uint) y >= (uint) Height)
				throw new ArgumentOutOfRangeException ($"({x},{y}) is outside the bitmap");
		}

		public void MakeTransparent () => MakeTransparent (GetPixel (0, Height - 1));

		public void MakeTransparent (Color transparentColor)
		{
			uint key = ColorBgra.FromColor (transparentColor).Bgra | 0xff000000;
			for (int y = 0; y < Height; y++)
				for (int x = 0; x < Width; x++)
					if ((Row (y)[x].Bgra | 0xff000000) == key) Row (y)[x] = default;
			EncodedData = null;
		}

		public BitmapData LockBits (Rectangle rect, ImageLockMode flags, PixelFormat format)
		{
			rect.Intersect (new Rectangle (0, 0, Width, Height));
			BitmapData data = new () { Width = rect.Width, Height = rect.Height, PixelFormat = format, Mode = flags, Rect = rect };
			if (format is PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb) {
				data.Stride = Stride;
				data.Scan0 = (nint) (Row (rect.Y) + rect.X);
			} else {
				int bpp = Image.GetPixelFormatSize (format) / 8;
				data.Stride = (rect.Width * bpp + 3) & ~3;
				data.Buffer = new MemoryBlock ((long) data.Stride * rect.Height);
				data.Scan0 = data.Buffer.Pointer;
				for (int y = 0; y < rect.Height; y++) {
					byte* d = (byte*) data.Scan0 + (long) y * data.Stride;
					ColorBgra* s = Row (rect.Y + y) + rect.X;
					for (int x = 0; x < rect.Width; x++) Convert (s[x], d + x * bpp, format);
				}
			}
			EncodedData = null;
			return data;
		}

		public void UnlockBits (BitmapData bitmapdata)
		{
			if (bitmapdata.Buffer is null) return;
			if ((bitmapdata.Mode & ImageLockMode.WriteOnly) != 0) {
				int bpp = Image.GetPixelFormatSize (bitmapdata.PixelFormat) / 8;
				for (int y = 0; y < bitmapdata.Height; y++) {
					byte* s = (byte*) bitmapdata.Scan0 + (long) y * bitmapdata.Stride;
					ColorBgra* d = Row (bitmapdata.Rect.Y + y) + bitmapdata.Rect.X;
					for (int x = 0; x < bitmapdata.Width; x++) d[x] = Convert (s + x * bpp, bitmapdata.PixelFormat);
				}
			}
			bitmapdata.Buffer.Dispose ();
			bitmapdata.Buffer = null;
		}

		private static void Convert (ColorBgra c, byte* d, PixelFormat format)
		{
			switch (format) {
				case PixelFormat.Format24bppRgb:
					d[0] = c.B; d[1] = c.G; d[2] = c.R;
					break;
				case PixelFormat.Format32bppPArgb:
					*(ColorBgra*) d = c.ConvertToPremultipliedAlpha ();
					break;
				default:
					*(ColorBgra*) d = c;
					break;
			}
		}

		private static ColorBgra Convert (byte* s, PixelFormat format) => format switch {
			PixelFormat.Format24bppRgb => ColorBgra.FromBgr (s[0], s[1], s[2]),
			PixelFormat.Format32bppPArgb => (*(ColorBgra*) s).ConvertFromPremultipliedAlpha (),
			_ => *(ColorBgra*) s,
		};

		/// <summary>Copies the pixels into a surface of at least the same size.</summary>
		internal void CopyTo (Surface dst)
		{
			int w = Math.Min (Width, dst.Width), h = Math.Min (Height, dst.Height);
			for (int y = 0; y < h; y++)
				Buffer.MemoryCopy (Row (y), dst.GetRowAddressUnchecked (y), w * 4L, w * 4L);
		}

		/// <summary>The image as packed straight-alpha BGRA.</summary>
		internal DecodedImage ToDecoded ()
		{
			byte[] bytes = new byte[Width * Height * 4];
			fixed (byte* p = bytes)
				for (int y = 0; y < Height; y++)
					Buffer.MemoryCopy (Row (y), p + y * Width * 4, Width * 4L, Width * 4L);
			return new DecodedImage (Width, Height, bytes);
		}

		internal byte[] EncodePng ()
			=> EncodedData ?? ShimHost.EncodePng?.Invoke (ToDecoded ()) ?? throw new NotSupportedException ("No PNG encoder");

		public IntPtr GetHicon () => Icon.Register (new Icon (this));
		public IntPtr GetHbitmap () => Icon.Register (this);

		public Bitmap Clone (Rectangle rect, PixelFormat format)
		{
			Bitmap b = new (rect.Width, rect.Height);
			for (int y = 0; y < rect.Height; y++)
				Buffer.MemoryCopy (Row (rect.Y + y) + rect.X, b.Row (y), rect.Width * 4L, rect.Width * 4L);
			return b;
		}

		public void SetResolution (float xDpi, float yDpi) { }

		protected override void Dispose (bool disposing)
		{
			memory?.Dispose ();
			memory = null;
			alias_owner = null;
		}
	}

	public sealed class Icon : IDisposable, ICloneable
	{
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, object> handles = new ();
		private static long next_handle = 0x1000;

		internal Icon (Bitmap bitmap) { Bitmap = bitmap; }
		public Icon (Stream stream) : this (new Bitmap (stream)) { }
		public Icon (string fileName) : this (new Bitmap (fileName)) { }
		public Icon (Type type, string resource) : this (new Bitmap (type, resource)) { }
		public Icon (Icon original, Size size) : this (original.Bitmap) { }
		public Icon (Icon original, int width, int height) : this (original.Bitmap) { }

		internal Bitmap Bitmap { get; }
		public int Width => Bitmap.Width;
		public int Height => Bitmap.Height;
		public Size Size => Bitmap.Size;
		public IntPtr Handle { get; private set; }

		internal static nint Register (object o)
		{
			nint h = (nint) Threading.Interlocked.Increment (ref next_handle);
			handles[h] = o;
			if (o is Icon i) i.Handle = h;
			return h;
		}

		public static Icon FromHandle (IntPtr handle) => handles.TryGetValue (handle, out object o) && o is Icon i ? i : new Icon (new Bitmap (16, 16));
		public static Icon ExtractAssociatedIcon (string filePath) => new (new Bitmap (16, 16));
		public Bitmap ToBitmap () => new (Bitmap);
		public object Clone () => new Icon (Bitmap);
		public void Dispose () { }
	}

	public static class SystemIcons
	{
		public static Icon Application { get; } = new (new Bitmap (32, 32));
		public static Icon Information => Application;
		public static Icon Warning => Application;
		public static Icon Error => Application;
		public static Icon Question => Application;
	}

}

namespace System.Drawing.Imaging
{
	public enum PixelFormat
	{
		Indexed = 0x10000,
		Gdi = 0x20000,
		Alpha = 0x40000,
		PAlpha = 0x80000,
		Extended = 0x100000,
		Canonical = 0x200000,
		Undefined = 0,
		DontCare = 0,
		Format1bppIndexed = 0x30101,
		Format4bppIndexed = 0x30402,
		Format8bppIndexed = 0x30803,
		Format16bppGrayScale = 0x101004,
		Format16bppRgb555 = 0x21005,
		Format16bppRgb565 = 0x21006,
		Format16bppArgb1555 = 0x61007,
		Format24bppRgb = 0x21808,
		Format32bppRgb = 0x22009,
		Format32bppArgb = 0x26200a,
		Format32bppPArgb = 0xe200b,
		Format48bppRgb = 0x10300c,
		Format64bppArgb = 0x34400d,
		Format64bppPArgb = 0x1a400e,
		Max = 0xf,
	}

	[Flags]
	public enum ImageLockMode
	{
		ReadOnly = 1,
		WriteOnly = 2,
		ReadWrite = 3,
		UserInputBuffer = 4,
	}

	public sealed class BitmapData
	{
		public int Width { get; set; }
		public int Height { get; set; }
		public int Stride { get; set; }
		public PixelFormat PixelFormat { get; set; }
		public IntPtr Scan0 { get; set; }
		public int Reserved { get; set; }
		internal ImageLockMode Mode { get; set; }
		internal Rectangle Rect { get; set; }
		internal MemoryBlock Buffer { get; set; }
	}

	public sealed class ImageFormat
	{
		private ImageFormat (string name) { Name = name; }
		private string Name { get; }
		public static ImageFormat Png { get; } = new ("Png");
		public static ImageFormat Bmp { get; } = new ("Bmp");
		public static ImageFormat Jpeg { get; } = new ("Jpeg");
		public static ImageFormat Gif { get; } = new ("Gif");
		public static ImageFormat Tiff { get; } = new ("Tiff");
		public static ImageFormat Icon { get; } = new ("Icon");
		public override string ToString () => Name;
	}
}
