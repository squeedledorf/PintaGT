using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using PaintDotNet.ComponentModel;
using PaintDotNet.Rendering;

namespace PaintDotNet;

public enum SurfaceCreationFlags
{
	Default = 0,
	DoNotZeroFillHint = 1,
}

public enum ResamplingAlgorithm
{
	NearestNeighbor = 0,
	LinearLowQuality = 1,
	Cubic = 2,
	SuperSampling = 3,
	Fant = 4,
	Linear = 5,
	AdaptiveFast = 6,
	Lanczos3 = 7,
	CubicSmooth = 8,
	AdaptiveHighQuality = 9,
}

/// <summary>
/// A block of native memory. Plugins read <see cref="Pointer"/> directly, so it never moves.
/// </summary>
public sealed unsafe class MemoryBlock : IDisposable
{
	private readonly bool owned;

	public MemoryBlock (long bytes)
	{
		Pointer = (nint) NativeMemory.AlignedAlloc ((nuint) Math.Max (bytes, 1), 64);
		NativeMemory.Clear ((void*) Pointer, (nuint) bytes);
		Length = bytes;
		owned = true;
	}

	public MemoryBlock (MemoryBlock parentBlock, long offset, long length)
	{
		Parent = parentBlock;
		Pointer = parentBlock.Pointer + (nint) offset;
		Length = length;
	}

	internal MemoryBlock (nint pointer, long length)
	{
		Pointer = pointer;
		Length = length;
	}

	public nint Pointer { get; private set; }
	public void* VoidStar => (void*) Pointer;
	public long Length { get; }
	public MemoryBlock Parent { get; }
	public bool IsDisposed { get; private set; }

	public byte this[long index] {
		get {
			if ((ulong) index >= (ulong) Length) throw new ArgumentOutOfRangeException (nameof (index));
			return ((byte*) Pointer)[index];
		}
		set {
			if ((ulong) index >= (ulong) Length) throw new ArgumentOutOfRangeException (nameof (index));
			((byte*) Pointer)[index] = value;
		}
	}

	public byte[] ToByteArray () => ToByteArray (0, Length);
	public byte[] ToByteArray (long startOffset, long length) => new Span<byte> ((byte*) Pointer + startOffset, (int) length).ToArray ();

	public static void CopyBlock (MemoryBlock dst, long dstOffset, MemoryBlock src, long srcOffset, long length)
		=> Buffer.MemoryCopy ((byte*) src.Pointer + srcOffset, (byte*) dst.Pointer + dstOffset, dst.Length - dstOffset, length);

	public void Dispose ()
	{
		if (IsDisposed) return;
		IsDisposed = true;
		if (owned) NativeMemory.AlignedFree ((void*) Pointer);
		Pointer = 0;
		GC.SuppressFinalize (this);
	}

	~MemoryBlock () => Dispose ();
}

/// <summary>
/// A BGRA bitmap with straight alpha, as used by classic effects.
/// </summary>
public sealed unsafe class Surface : RefTrackedObject, ICloneable
{
	private MemoryBlock scan0;

	public Surface (int width, int height) : this (width, height, SurfaceCreationFlags.Default) { }
	public Surface (Size size) : this (size.Width, size.Height) { }
	public Surface (SizeInt32 size) : this (size.Width, size.Height) { }
	public Surface (Size size, SurfaceCreationFlags surfaceCreationFlags) : this (size.Width, size.Height, surfaceCreationFlags) { }
	public Surface (SizeInt32 size, SurfaceCreationFlags surfaceCreationFlags) : this (size.Width, size.Height, surfaceCreationFlags) { }

	public Surface (int width, int height, SurfaceCreationFlags surfaceCreationFlags)
	{
		if (width < 0 || height < 0) throw new ArgumentOutOfRangeException ();
		Width = width;
		Height = height;
		Stride = width * ColorBgra.SizeOf;
		scan0 = new MemoryBlock ((long) Stride * height);
	}

	private Surface (int width, int height, int stride, MemoryBlock memory)
	{
		Width = width;
		Height = height;
		Stride = stride;
		scan0 = memory;
	}

	public int Width { get; }
	public int Height { get; }
	public int Stride { get; }
	public Size Size => new (Width, Height);
	public Rectangle Bounds => new (0, 0, Width, Height);
	public MemoryBlock Scan0 => scan0;

	public ColorBgra this[int x, int y] {
		get {
			CheckPoint (x, y);
			return *GetPointAddressUnchecked (x, y);
		}
		set {
			CheckPoint (x, y);
			*GetPointAddressUnchecked (x, y) = value;
		}
	}

	public ColorBgra this[Point pt] {
		get => this[pt.X, pt.Y];
		set => this[pt.X, pt.Y] = value;
	}

	private void CheckPoint (int x, int y)
	{
		if ((uint) x >= (uint) Width || (uint) y >= (uint) Height)
			throw new ArgumentOutOfRangeException ($"Coordinates out of range, (x,y) = ({x},{y}), max = ({Width},{Height})");
	}

	public bool IsVisible (int x, int y) => (uint) x < (uint) Width && (uint) y < (uint) Height;
	public bool IsVisible (Point pt) => IsVisible (pt.X, pt.Y);
	public bool IsColumnVisible (int x) => (uint) x < (uint) Width;
	public bool IsRowVisible (int y) => (uint) y < (uint) Height;

	public long GetRowByteOffset (int y) { if (!IsRowVisible (y)) throw new ArgumentOutOfRangeException (nameof (y)); return (long) y * Stride; }
	public long GetRowByteOffsetUnchecked (int y) => (long) y * Stride;
	public long GetColumnByteOffset (int x) { if (!IsColumnVisible (x)) throw new ArgumentOutOfRangeException (nameof (x)); return x * 4L; }
	public long GetColumnByteOffsetUnchecked (int x) => x * 4L;
	public long GetPointByteOffset (int x, int y) { CheckPoint (x, y); return GetPointByteOffsetUnchecked (x, y); }
	public long GetPointByteOffsetUnchecked (int x, int y) => (long) y * Stride + x * 4L;

	public ColorBgra* GetRowAddress (int y) { if (!IsRowVisible (y)) throw new ArgumentOutOfRangeException (nameof (y)); return GetRowAddressUnchecked (y); }
	public ColorBgra* GetRowAddressUnchecked (int y) => (ColorBgra*) ((byte*) scan0.Pointer + (long) y * Stride);
	public ColorBgra* GetRowPointer (int y) => GetRowAddress (y);
	public ColorBgra* GetRowPointerUnchecked (int y) => GetRowAddressUnchecked (y);
	public ColorBgra* GetPointAddress (int x, int y) { CheckPoint (x, y); return GetPointAddressUnchecked (x, y); }
	public ColorBgra* GetPointAddressUnchecked (int x, int y) => GetRowAddressUnchecked (y) + x;
	public ColorBgra* GetPointAddress (Point pt) => GetPointAddress (pt.X, pt.Y);
	public ColorBgra* GetPointAddressUnchecked (Point pt) => GetPointAddressUnchecked (pt.X, pt.Y);
	public ColorBgra* GetPointPointer (int x, int y) => GetPointAddress (x, y);
	public ColorBgra* GetPointPointerUnchecked (int x, int y) => GetPointAddressUnchecked (x, y);
	public ref ColorBgra GetPointReference (int x, int y) => ref *GetPointAddress (x, y);
	public ref ColorBgra GetPointReferenceUnchecked (int x, int y) => ref *GetPointAddressUnchecked (x, y);
	public ref ColorBgra GetRowReference (int y) => ref *GetRowAddress (y);
	public ref ColorBgra GetRowReferenceUnchecked (int y) => ref *GetRowAddressUnchecked (y);
	public ColorBgra GetPoint (int x, int y) => this[x, y];
	public ColorBgra GetPointUnchecked (int x, int y) => *GetPointAddressUnchecked (x, y);

	/// <summary>The row as a span, for host code.</summary>
	public Span<ColorBgra> GetRowSpan (int y) => new (GetRowAddress (y), Width);

	public Surface Clone ()
	{
		Surface s = new (Width, Height);
		s.CopySurface (this);
		return s;
	}

	object ICloneable.Clone () => Clone ();

	/// <summary>A surface that shares this surface's memory for the given rectangle.</summary>
	public Surface CreateWindow (Rectangle bounds)
	{
		Rectangle clipped = Rectangle.Intersect (bounds, Bounds);
		if (clipped != bounds) throw new ArgumentOutOfRangeException (nameof (bounds));
		long offset = GetPointByteOffsetUnchecked (bounds.X, bounds.Y);
		long length = bounds.Height == 0 ? 0 : (long) (bounds.Height - 1) * Stride + bounds.Width * 4L;
		return new Surface (bounds.Width, bounds.Height, Stride, new MemoryBlock (scan0, offset, length));
	}

	public Surface CreateWindow (RectInt32 bounds) => CreateWindow ((Rectangle) bounds);
	public Surface CreateWindow (int x, int y, int windowWidth, int windowHeight) => CreateWindow (new Rectangle (x, y, windowWidth, windowHeight));

	// --- Clear / Fill ---

	public void Clear () => Clear (default (ColorBgra));
	public void Clear (ColorBgra color) => Clear (Bounds, color);

	public void Clear (Rectangle rect, ColorBgra color)
	{
		rect.Intersect (Bounds);
		for (int y = rect.Top; y < rect.Bottom; y++)
			new Span<ColorBgra> (GetPointAddressUnchecked (rect.X, y), rect.Width).Fill (color);
	}

	public void Clear (PdnRegion region, ColorBgra color)
	{
		foreach (Rectangle r in region.GetRegionScansReadOnlyInt ()) Clear (r, color);
	}

	public void Fill (ColorBgra color) => Clear (color);
	public void Fill (Rectangle rect, ColorBgra color) => Clear (rect, color);
	public void Fill (RectInt32 rect, ColorBgra color) => Clear ((Rectangle) rect, color);
	public void Fill (PdnRegion region, ColorBgra color) => Clear (region, color);
	public void Fill (RectInt32[] scans, ColorBgra color) => Fill (scans, 0, scans.Length, color);
	public void Fill (RectInt32[] scans, int startIndex, int length, ColorBgra color)
	{
		for (int i = startIndex; i < startIndex + length; i++) Clear ((Rectangle) scans[i], color);
	}

	public void ClearWithCheckerboardPattern () => ClearWithCheckerboardPattern (0, 0);

	public void ClearWithCheckerboardPattern (int xOffset, int yOffset)
	{
		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++)
				*GetPointAddressUnchecked (x, y) = (((x + xOffset) ^ (y + yOffset)) & 8) == 0 ? ColorBgra.FromBgr (255, 255, 255) : ColorBgra.FromBgr (191, 191, 191);
	}

	// --- Copy ---

	public void CopySurface (Surface source) => CopySurface (source, Point.Empty, source.Bounds);
	public void CopySurface (Surface source, Point dstOffset) => CopySurface (source, dstOffset, source.Bounds);
	public void CopySurface (Surface source, Point2Int32 dstOffset) => CopySurface (source, new Point (dstOffset.X, dstOffset.Y));
	public void CopySurface (Surface source, Rectangle sourceRoi) => CopySurface (source, sourceRoi.Location, sourceRoi);
	public void CopySurface (Surface source, RectInt32 sourceRoi) => CopySurface (source, (Rectangle) sourceRoi);
	public void CopySurface (Surface source, Point2Int32 dstOffset, RectInt32 srcRect) => CopySurface (source, new Point (dstOffset.X, dstOffset.Y), (Rectangle) srcRect);

	/// <summary>Copies <paramref name="srcRect"/> of the source to <paramref name="dstOffset"/> in this surface, clipped to both.</summary>
	public void CopySurface (Surface source, Point dstOffset, Rectangle srcRect)
	{
		srcRect.Intersect (source.Bounds);
		Rectangle dstRect = new (dstOffset, srcRect.Size);
		dstRect.Intersect (Bounds);
		int sx = srcRect.X + (dstRect.X - dstOffset.X);
		int sy = srcRect.Y + (dstRect.Y - dstOffset.Y);
		for (int y = 0; y < dstRect.Height; y++)
			Buffer.MemoryCopy (
				source.GetPointAddressUnchecked (sx, sy + y),
				GetPointAddressUnchecked (dstRect.X, dstRect.Y + y),
				dstRect.Width * 4L,
				dstRect.Width * 4L);
	}

	public void CopySurface (Surface source, Rectangle[] region) => CopySurface (source, region, 0, region.Length);

	public void CopySurface (Surface source, Rectangle[] region, int startIndex, int length)
	{
		for (int i = startIndex; i < startIndex + length; i++) CopySurface (source, region[i]);
	}

	public void CopySurface (Surface source, RectInt32[] region) => CopySurface (source, region, 0, region.Length);

	public void CopySurface (Surface source, RectInt32[] region, int startIndex, int length)
	{
		for (int i = startIndex; i < startIndex + length; i++) CopySurface (source, (Rectangle) region[i]);
	}

	public void CopySurface (Surface source, PdnRegion region) => CopySurface (source, region.GetRegionScansReadOnlyInt ());

	// --- Sampling ---

	/// <summary>Bilinear sample; samples outside the surface are transparent.</summary>
	public ColorBgra GetBilinearSample (float x, float y) => Sample (x, y, 0);

	public ColorBgra GetBilinearSampleClamped (float x, float y) => Sample (x, y, 1);

	public ColorBgra GetBilinearSampleWrapped (float x, float y) => Sample (x, y, 2);

	private ColorBgra Sample (float x, float y, int mode)
	{
		if (Width == 0 || Height == 0 || !float.IsFinite (x) || !float.IsFinite (y))
			return default;
		float fx = MathF.Floor (x), fy = MathF.Floor (y);
		int x0 = (int) fx, y0 = (int) fy;
		uint wx = (uint) ((x - fx) * 256), wy = (uint) ((y - fy) * 256);
		ColorBgra c00 = Fetch (x0, y0, mode), c10 = Fetch (x0 + 1, y0, mode);
		ColorBgra c01 = Fetch (x0, y0 + 1, mode), c11 = Fetch (x0 + 1, y0 + 1, mode);
		return ColorBgra.BlendColors4W16IP (
			c00, (256 - wx) * (256 - wy),
			c10, wx * (256 - wy),
			c01, (256 - wx) * wy,
			c11, wx * wy);
	}

	private ColorBgra Fetch (int x, int y, int mode)
	{
		switch (mode) {
			case 1:
				x = Math.Clamp (x, 0, Width - 1);
				y = Math.Clamp (y, 0, Height - 1);
				break;
			case 2:
				x = ((x % Width) + Width) % Width;
				y = ((y % Height) + Height) % Height;
				break;
			default:
				if (!IsVisible (x, y)) return default;
				break;
		}
		return *GetPointAddressUnchecked (x, y);
	}

	/// <summary>Resizes <paramref name="source"/> to fill this surface (box filter when shrinking, bilinear when enlarging).</summary>
	public void FitSurface (ResamplingAlgorithm algorithm, Surface source) => FitSurface (algorithm, source, Bounds);

	public void FitSurface (ResamplingAlgorithm algorithm, Surface source, Rectangle dstRoi)
	{
		dstRoi.Intersect (Bounds);
		double sx = (double) source.Width / Width, sy = (double) source.Height / Height;
		for (int y = dstRoi.Top; y < dstRoi.Bottom; y++) {
			for (int x = dstRoi.Left; x < dstRoi.Right; x++) {
				ColorBgra c;
				if (algorithm == ResamplingAlgorithm.NearestNeighbor) {
					c = source[Math.Min ((int) (x * sx), source.Width - 1), Math.Min ((int) (y * sy), source.Height - 1)];
				} else if (sx > 1 || sy > 1) {
					int x0 = (int) (x * sx), x1 = Math.Max (x0 + 1, Math.Min (source.Width, (int) Math.Ceiling ((x + 1) * sx)));
					int y0 = (int) (y * sy), y1 = Math.Max (y0 + 1, Math.Min (source.Height, (int) Math.Ceiling ((y + 1) * sy)));
					long a = 0, b = 0, g = 0, r = 0, n = 0;
					for (int v = y0; v < y1 && v < source.Height; v++)
						for (int u = x0; u < x1 && u < source.Width; u++) {
							ColorBgra s = *source.GetPointAddressUnchecked (u, v);
							a += s.A; b += s.B * s.A; g += s.G * s.A; r += s.R * s.A; n++;
						}
					c = a == 0 ? default : ColorBgra.FromBgra ((byte) (b / a), (byte) (g / a), (byte) (r / a), (byte) (a / n));
				} else {
					c = source.GetBilinearSampleClamped ((float) ((x + 0.5) * sx - 0.5), (float) ((y + 0.5) * sy - 0.5));
				}
				*GetPointAddressUnchecked (x, y) = c;
			}
		}
	}

	// --- System.Drawing interop ---

	/// <summary>A GDI-style bitmap that aliases this surface's memory.</summary>
	public Bitmap CreateAliasedBitmap () => CreateAliasedBitmap (Bounds, true);
	public Bitmap CreateAliasedBitmap (bool alpha) => CreateAliasedBitmap (Bounds, alpha);
	public Bitmap CreateAliasedBitmap (Rectangle bounds) => CreateAliasedBitmap (bounds, true);
	public Bitmap CreateAliasedBitmap (RectInt32 bounds) => CreateAliasedBitmap ((Rectangle) bounds, true);

	public Bitmap CreateAliasedBitmap (Rectangle bounds, bool alpha)
	{
		bounds.Intersect (Bounds);
		return Bitmap.CreateAlias ((nint) GetPointAddressUnchecked (bounds.X, bounds.Y), bounds.Width, bounds.Height, Stride, this);
	}

	public static Surface CopyFromBitmap (Bitmap bitmap) => CopyFromBitmap (bitmap, false);

	public static Surface CopyFromBitmap (Bitmap bitmap, bool detectDishonestAlpha)
	{
		Surface s = new (bitmap.Width, bitmap.Height);
		bitmap.CopyTo (s);
		return s;
	}

	public void CopyFromGdipBitmap (Bitmap bitmap, bool detectDishonestAlpha = true) => bitmap.CopyTo (this);
	public static Surface CopyFromGdipImage (Image image) => CopyFromBitmap (new Bitmap (image));
	public static Surface CopyFromGdipImage (Image image, bool detectDishonestAlpha) => CopyFromGdipImage (image);

	public void DetectAndFixDishonestAlpha () { }

	protected override void Dispose (bool disposing)
	{
		if (disposing) scan0?.Dispose ();
		base.Dispose (disposing);
	}
}

/// <summary>
/// A surface together with its lazily created GDI-style bitmap and graphics.
/// </summary>
public sealed class RenderArgs : Disposable
{
	private Bitmap bitmap;
	private Graphics graphics;

	public RenderArgs (Surface surface)
	{
		Surface = surface;
	}

	public Surface Surface { get; }
	public Rectangle Bounds => Surface.Bounds;
	public Size Size => Surface.Size;
	public int Width => Surface.Width;
	public int Height => Surface.Height;
	public Bitmap Bitmap => bitmap ??= Surface.CreateAliasedBitmap ();
	public Graphics Graphics => graphics ??= Graphics.FromImage (Bitmap);

	protected override void Dispose (bool disposing)
	{
		if (disposing) {
			graphics?.Dispose ();
			bitmap?.Dispose ();
		}
		base.Dispose (disposing);
	}
}

/// <summary>
/// A region stored as a list of non-overlapping integer rectangles (scans).
/// </summary>
public sealed class PdnRegion : IDisposable
{
	private readonly List<Rectangle> scans = [];

	public PdnRegion () { }
	public PdnRegion (Rectangle rect) { if (!rect.IsEmpty) scans.Add (rect); }
	public PdnRegion (RectInt32 rect) : this ((Rectangle) rect) { }
	public PdnRegion (RectangleF rectF) : this (Rectangle.Round (rectF)) { }
	public PdnRegion (Region region) : this (region.Bounds) { }
	public PdnRegion (Region region, bool takeOwnership) : this (region.Bounds) { }

	public static PdnRegion CreateEmpty () => new ();

	public static PdnRegion FromRectangles (Rectangle[] rects) => FromRectangles (rects, 0, rects.Length);

	public static PdnRegion FromRectangles (Rectangle[] rects, int startIndex, int length)
	{
		PdnRegion r = new ();
		for (int i = startIndex; i < startIndex + length; i++) r.Union (rects[i]);
		return r;
	}

	public static PdnRegion FromRectangles (RectangleF[] rectsF) => FromRectangles (Array.ConvertAll (rectsF, Rectangle.Round));
	public static PdnRegion FromRectangles (RectInt32[] rects) => FromRectangles (Array.ConvertAll (rects, r => (Rectangle) r));
	public static PdnRegion FromRectangles (IEnumerable<RectInt32> rects)
	{
		PdnRegion r = new ();
		foreach (RectInt32 rect in rects) r.Union ((Rectangle) rect);
		return r;
	}

	public object SyncRoot => scans;
	public bool IsDisposed { get; private set; }

	public PdnRegion Clone ()
	{
		PdnRegion r = new ();
		r.scans.AddRange (scans);
		return r;
	}

	public Rectangle GetBoundsInt ()
	{
		if (scans.Count == 0) return Rectangle.Empty;
		Rectangle b = scans[0];
		foreach (Rectangle r in scans) b = Rectangle.Union (b, r);
		return b;
	}

	public RectInt32 GetBoundsRectInt32 () => GetBoundsInt ();
	public RectangleF GetBounds () => GetBoundsInt ();
	public RectangleF GetBounds (Graphics g) => GetBoundsInt ();
	public long GetArea64 () { long a = 0; foreach (Rectangle r in scans) a += (long) r.Width * r.Height; return a; }

	public Rectangle[] GetRegionScansInt () => [.. scans];
	public Rectangle[] GetRegionScansReadOnlyInt () => [.. scans];
	public RectangleF[] GetRegionScans () => scans.ConvertAll (r => (RectangleF) r).ToArray ();
	public RectangleF[] GetRegionScansReadOnly () => GetRegionScans ();
	public Region GetRegionReadOnly () => new (GetBoundsInt ());

	public bool IsEmpty () => scans.Count == 0;
	public bool IsEmpty (Graphics g) => IsEmpty ();
	public bool IsInfinite (Graphics g) => false;

	public bool IsVisible (int x, int y)
	{
		foreach (Rectangle r in scans) if (r.Contains (x, y)) return true;
		return false;
	}

	public bool IsVisible (Point point) => IsVisible (point.X, point.Y);
	public bool IsVisible (PointF pointF) => IsVisible ((int) MathF.Floor (pointF.X), (int) MathF.Floor (pointF.Y));
	public bool IsVisible (float x, float y) => IsVisible (new PointF (x, y));
	public bool IsVisible (Rectangle rect) { foreach (Rectangle r in scans) if (r.IntersectsWith (rect)) return true; return false; }
	public bool IsVisible (RectangleF rectF) => IsVisible (Rectangle.Round (rectF));
	public bool IsVisible (int x, int y, int width, int height) => IsVisible (new Rectangle (x, y, width, height));
	public bool IsVisible (Point point, Graphics g) => IsVisible (point);
	public bool IsVisible (Rectangle rect, Graphics g) => IsVisible (rect);

	public void Intersect (Rectangle rect)
	{
		for (int i = scans.Count - 1; i >= 0; i--) {
			Rectangle r = Rectangle.Intersect (scans[i], rect);
			if (r.IsEmpty) scans.RemoveAt (i); else scans[i] = r;
		}
	}

	public void Intersect (RectangleF rectF) => Intersect (Rectangle.Round (rectF));

	public void Intersect (PdnRegion region2)
	{
		List<Rectangle> result = [];
		foreach (Rectangle a in scans)
			foreach (Rectangle b in region2.scans) {
				Rectangle r = Rectangle.Intersect (a, b);
				if (!r.IsEmpty) result.Add (r);
			}
		scans.Clear ();
		scans.AddRange (result);
	}

	public void Exclude (Rectangle rect)
	{
		List<Rectangle> result = [];
		foreach (Rectangle a in scans) Subtract (a, rect, result);
		scans.Clear ();
		scans.AddRange (result);
	}

	public void Exclude (RectangleF rectF) => Exclude (Rectangle.Round (rectF));
	public void Exclude (PdnRegion region2) { foreach (Rectangle r in region2.scans) Exclude (r); }

	public void Union (Rectangle rect)
	{
		if (rect.IsEmpty) return;
		List<Rectangle> pieces = [rect];
		foreach (Rectangle existing in scans) {
			List<Rectangle> next = [];
			foreach (Rectangle p in pieces) Subtract (p, existing, next);
			pieces = next;
		}
		scans.AddRange (pieces);
	}

	public void Union (RectangleF rectF) => Union (Rectangle.Round (rectF));
	public void Union (RectangleF[] rectsF) { foreach (RectangleF r in rectsF) Union (r); }
	public void Union (PdnRegion region2) { foreach (Rectangle r in region2.scans) Union (r); }

	public void Xor (Rectangle rect)
	{
		PdnRegion inter = Clone ();
		inter.Intersect (rect);
		PdnRegion add = new (rect);
		add.Exclude (this);
		Exclude (inter);
		Union (add);
	}

	public void Xor (PdnRegion region2) { foreach (Rectangle r in region2.scans) Xor (r); }

	public void Complement (Rectangle rect)
	{
		PdnRegion r = new (rect);
		r.Exclude (this);
		scans.Clear ();
		scans.AddRange (r.scans);
	}

	public void MakeEmpty () => scans.Clear ();
	public void MakeInfinite () { scans.Clear (); scans.Add (new Rectangle (-0x400000, -0x400000, 0x800000, 0x800000)); }

	public void Dispose () => IsDisposed = true;

	/// <summary>Adds the parts of <paramref name="a"/> that are outside <paramref name="b"/> to <paramref name="output"/>.</summary>
	private static void Subtract (Rectangle a, Rectangle b, List<Rectangle> output)
	{
		Rectangle i = Rectangle.Intersect (a, b);
		if (i.IsEmpty) { output.Add (a); return; }
		if (i.Top > a.Top) output.Add (Rectangle.FromLTRB (a.Left, a.Top, a.Right, i.Top));
		if (i.Left > a.Left) output.Add (Rectangle.FromLTRB (a.Left, i.Top, i.Left, i.Bottom));
		if (i.Right < a.Right) output.Add (Rectangle.FromLTRB (i.Right, i.Top, a.Right, i.Bottom));
		if (i.Bottom < a.Bottom) output.Add (Rectangle.FromLTRB (a.Left, i.Bottom, a.Right, a.Bottom));
	}
}
