using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using PaintDotNet.ComponentModel;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;

// The Paint.NET 5 CPU effect model: OnInitializeRenderInfo once, OnSetToken per settings change,
// then OnRender(output) per tile, with pixels reached through bitmap locks and RegionPtr.
// Only 32-bit BGRA (straight alpha) is supported, which is what CPU plugins ask for in practice.
namespace PaintDotNet.Imaging
{
	public interface INaturalPixelInfo { }

	public readonly struct PixelFormat : IEquatable<PixelFormat>
	{
		public PixelFormat (in Guid guid) { Guid = guid; }
		public Guid Guid { get; }
		public int GetBitsPerPixel () => 32;
		public bool TryGetBitsPerPixel (out int bpp) { bpp = 32; return true; }
		public string GetName () => this == PixelFormats.Pbgra32 ? "Pbgra32" : "Bgra32";
		public bool Equals (PixelFormat other) => Guid == other.Guid;
		public override bool Equals (object obj) => obj is PixelFormat p && Equals (p);
		public override int GetHashCode () => Guid.GetHashCode ();
		public static bool operator == (PixelFormat a, PixelFormat b) => a.Equals (b);
		public static bool operator != (PixelFormat a, PixelFormat b) => !a.Equals (b);
		public override string ToString () => GetName ();
	}

	/// <summary>Pixel format IDs (the WIC GUIDs).</summary>
	public static class PixelFormats
	{
		public static PixelFormat Bgra32 { get; } = new (new Guid ("6fddc324-4e03-4bfe-b185-3d77768dc90f"));
		public static PixelFormat Pbgra32 { get; } = new (new Guid ("6fddc324-4e03-4bfe-b185-3d77768dc910"));
	}

	public enum BitmapLockOptions { Read = 1, Write = 2 }

	public enum BitmapExtendMode { Clamp = 0, Zero = 3 }

	public interface IColorContext { }

	/// <summary>An sRGB color.</summary>
	public abstract class ManagedColor : IEquatable<ManagedColor>
	{
		public abstract IColorContext ColorContext { get; }
		internal abstract ColorBgra32 Bgra { get; }
		public bool Equals (ManagedColor other) => other is not null && Bgra == other.Bgra;
		public override bool Equals (object obj) => obj is ManagedColor m && Equals (m);
		public override int GetHashCode () => Bgra.GetHashCode ();
		internal static ManagedColor FromBgra (ColorBgra32 c) => new Srgb (c);

		private sealed class Srgb (ColorBgra32 c) : ManagedColor
		{
			public override IColorContext ColorContext => null;
			internal override ColorBgra32 Bgra => c;
		}
	}

	public static class ManagedColorExtensions
	{
		public static ColorBgra32 GetBgra32 (this ManagedColor color, IColorContext dstColorContext) => color.Bgra;
	}

	public interface IBitmapSource : IDisposable
	{
		SizeInt32 Size { get; }
		PixelFormat PixelFormat { get; }
	}

	public interface IBitmapSource<TPixel> : IBitmapSource where TPixel : unmanaged { }

	public interface IBitmap : IBitmapSource
	{
		IBitmapLock Lock (RectInt32 rect, BitmapLockOptions lockOptions);
	}

	public interface IBitmap<TPixel> : IBitmap, IBitmapSource<TPixel> where TPixel : unmanaged
	{
		new IBitmapLock<TPixel> Lock (RectInt32 rect, BitmapLockOptions lockOptions);
	}

	public unsafe interface IBitmapLock : IDisposable
	{
		void* Buffer { get; }
		int BufferStride { get; }
		uint BufferSize { get; }
		SizeInt32 Size { get; }
		PixelFormat PixelFormat { get; }
	}

	public interface IBitmapLock<TPixel> : IBitmapLock where TPixel : unmanaged { }

	/// <summary>A lock on memory owned by someone else (a surface, or the output buffer).</summary>
	internal sealed unsafe class BitmapLock<TPixel> (void* buffer, int stride, SizeInt32 size) : IBitmapLock<TPixel> where TPixel : unmanaged
	{
		public void* Buffer => buffer;
		public int BufferStride => stride;
		public uint BufferSize => (uint) (stride * size.Height);
		public SizeInt32 Size => size;
		public PixelFormat PixelFormat => PixelFormats.Bgra32;
		public void Dispose () { }
	}

	/// <summary>A 32-bit view of a <see cref="Surface"/>, optionally showing only part of it.</summary>
	internal sealed unsafe class SurfaceBitmap<TPixel> : IBitmap<TPixel>, Effects.IEffectInputBitmap<TPixel> where TPixel : unmanaged
	{
		private readonly Surface surface;
		private readonly RectInt32 window;
		private readonly bool zeroOutside;

		public SurfaceBitmap (Surface surface) : this (surface, surface.Bounds, false) { }

		public SurfaceBitmap (Surface surface, RectInt32 window, bool zeroOutside)
		{
			if (sizeof (TPixel) != 4)
				throw new NotSupportedException ($"Pixel type {typeof (TPixel).Name} is not supported; only 32-bit BGRA is");
			this.surface = surface;
			this.window = window;
			this.zeroOutside = zeroOutside;
		}

		public SizeInt32 Size => window.Size;
		public PixelFormat PixelFormat => PixelFormats.Bgra32;
		public IColorContext ColorContext => null;

		public IBitmapLock<TPixel> Lock (RectInt32 rect) => Lock (rect, BitmapLockOptions.Read);
		IBitmapLock Effects.IEffectInputBitmap.Lock (RectInt32 rect) => Lock (rect);
		IBitmapLock IBitmap.Lock (RectInt32 rect, BitmapLockOptions lockOptions) => Lock (rect, lockOptions);

		public IBitmapLock<TPixel> Lock (RectInt32 rect, BitmapLockOptions lockOptions)
		{
			RectInt32 abs = rect.Offset (window.X, window.Y);
			RectInt32 inside = RectInt32.Intersect (abs, surface.Bounds);
			if (inside == abs)
				return new BitmapLock<TPixel> (surface.GetPointAddressUnchecked (abs.X, abs.Y), surface.Stride, rect.Size);
			// Partly outside the surface: copy into a buffer, clamping or zeroing the edges.
			ColorBgra[] copy = new ColorBgra[rect.Width * rect.Height];
			for (int y = 0; y < rect.Height; y++)
				for (int x = 0; x < rect.Width; x++) {
					int sx = abs.X + x, sy = abs.Y + y;
					copy[y * rect.Width + x] = surface.IsVisible (sx, sy) ? surface[sx, sy]
						: zeroOutside ? default : surface[Math.Clamp (sx, 0, surface.Width - 1), Math.Clamp (sy, 0, surface.Height - 1)];
				}
			return new PinnedLock (GCHandle.Alloc (copy, GCHandleType.Pinned), rect.Width * 4, rect.Size);
		}

		public SurfaceBitmap<TPixel> Clip (RectInt32 rect, bool zero) => new (surface, rect.Offset (window.X, window.Y), zero);

		private sealed class PinnedLock (GCHandle handle, int stride, SizeInt32 size) : IBitmapLock<TPixel>
		{
			public void* Buffer => (void*) handle.AddrOfPinnedObject ();
			public int BufferStride => stride;
			public uint BufferSize => (uint) (stride * size.Height);
			public SizeInt32 Size => size;
			public PixelFormat PixelFormat => PixelFormats.Bgra32;
			public void Dispose () { if (handle.IsAllocated) handle.Free (); }
		}

		public void Dispose () { }
	}

	public static class BitmapLockExtensions
	{
		public static unsafe RegionPtr<TPixel> AsRegionPtr<TPixel> (this IBitmapLock<TPixel> bitmapLock) where TPixel : unmanaged
			=> new (bitmapLock, (TPixel*) bitmapLock.Buffer, bitmapLock.Size, bitmapLock.BufferStride);
	}

	public static class BitmapExtensions
	{
		public static IBitmapLock<TPixel> Lock<TPixel> (this IBitmap<TPixel> bitmap, BitmapLockOptions lockOptions) where TPixel : unmanaged
			=> bitmap.Lock (new RectInt32 (0, 0, bitmap.Size), lockOptions);

		public static IBitmapLock Lock (this IBitmap bitmap, BitmapLockOptions lockOptions)
			=> bitmap.Lock (new RectInt32 (0, 0, bitmap.Size), lockOptions);
	}

	public static class BitmapSourceExtensions
	{
		/// <summary>A copy of the source as an in-memory bitmap.</summary>
		public static unsafe IBitmap<TPixel> ToBitmap<TPixel> (this IBitmapSource<TPixel> source) where TPixel : unmanaged
		{
			SizeInt32 size = source.Size;
			Surface copy = new (size.Width, size.Height);
			if (source is SurfaceBitmap<TPixel> sb) {
				using IBitmapLock<TPixel> l = sb.Lock (new RectInt32 (0, 0, size), BitmapLockOptions.Read);
				for (int y = 0; y < size.Height; y++)
					Buffer.MemoryCopy ((byte*) l.Buffer + (long) y * l.BufferStride, copy.GetRowAddressUnchecked (y), size.Width * 4L, size.Width * 4L);
			}
			return new SurfaceBitmap<TPixel> (copy);
		}

		public static IBitmap<TPixel> ToBitmap<TPixel> (this IBitmapSource<TPixel> source, RectInt32 rect) where TPixel : unmanaged
			=> CreateClipper (source, rect).ToBitmap ();

		/// <summary>The part of the source inside <paramref name="sourceRect"/>; pixels outside are clamped or zero.</summary>
		public static IBitmapSource<TPixel> CreateClipper<TPixel> (this IBitmapSource<TPixel> source, RectInt32 sourceRect, BitmapExtendMode extendMode) where TPixel : unmanaged
			=> source is SurfaceBitmap<TPixel> sb ? sb.Clip (sourceRect, extendMode == BitmapExtendMode.Zero) : throw new NotSupportedException ();

		public static IBitmapSource<TPixel> CreateClipper<TPixel> (this IBitmapSource<TPixel> source, RectInt32 rect) where TPixel : unmanaged
			=> CreateClipper (source, rect, BitmapExtendMode.Clamp);
	}
}

namespace PaintDotNet
{
	/// <summary>A pointer to a 2D block of pixels with a stride.</summary>
	public readonly unsafe struct RegionPtr<T> : IEquatable<RegionPtr<T>> where T : unmanaged
	{
		private readonly object owner;

		public RegionPtr (T* ptr, SizeInt32 size, int stride) : this (null, ptr, size.Width, size.Height, stride) { }
		public RegionPtr (T* ptr, int width, int height, int stride) : this (null, ptr, width, height, stride) { }
		public RegionPtr (object owner, T* ptr, SizeInt32 size, int stride) : this (owner, ptr, size.Width, size.Height, stride) { }

		public RegionPtr (object owner, T* ptr, int width, int height, int stride)
		{
			this.owner = owner;
			Ptr = ptr;
			Width = width;
			Height = height;
			Stride = stride;
		}

		public T* Ptr { get; }
		public int Width { get; }
		public int Height { get; }
		public int Stride { get; }
		public SizeInt32 Size => new (Width, Height);
		public bool IsEmpty => Width == 0 || Height == 0;
		public bool IsContiguous => Stride == Width * sizeof (T);
		public nint Count => (nint) Width * Height;
		public static RegionPtr<T> Empty => default;

		public ref T this[int x, int y] {
			get {
				if ((uint) x >= (uint) Width || (uint) y >= (uint) Height)
					throw new ArgumentOutOfRangeException ($"({x},{y}) is outside {Width}x{Height}");
				return ref *(T*) ((byte*) Ptr + (long) y * Stride + (long) x * sizeof (T));
			}
		}

		public ref T this[Point2Int32 pt] => ref this[pt.X, pt.Y];

		public RegionPtr<T> Slice (RectInt32 bounds)
			=> new (owner, (T*) ((byte*) Ptr + (long) bounds.Y * Stride + (long) bounds.X * sizeof (T)), bounds.Width, bounds.Height, Stride);

		public RegionPtr<T> SliceRows (int y, int height) => Slice (new RectInt32 (0, y, Width, height));
		public RegionPtr<T> SliceColumns (int x, int width) => Slice (new RectInt32 (x, 0, width, Height));

		public void Fill (T value)
		{
			for (int y = 0; y < Height; y++)
				new Span<T> ((byte*) Ptr + (long) y * Stride, Width).Fill (value);
		}

		public void Clear () => Fill (default);

		public void CopyTo (RegionPtr<T> dst)
		{
			if (dst.Width < Width || dst.Height < Height) throw new ArgumentException ("Destination is too small");
			for (int y = 0; y < Height; y++)
				new Span<T> ((byte*) Ptr + (long) y * Stride, Width).CopyTo (new Span<T> ((byte*) dst.Ptr + (long) y * dst.Stride, Width));
		}

		public bool TryCopyTo (RegionPtr<T> dst)
		{
			if (dst.Width < Width || dst.Height < Height) return false;
			CopyTo (dst);
			return true;
		}

		public bool Equals (RegionPtr<T> other) => Ptr == other.Ptr && Width == other.Width && Height == other.Height && Stride == other.Stride;
		public override bool Equals (object obj) => obj is RegionPtr<T> r && Equals (r);
		public override int GetHashCode () => HashCode.Combine ((nint) Ptr, Width, Height, Stride);
		public static bool operator == (RegionPtr<T> a, RegionPtr<T> b) => a.Equals (b);
		public static bool operator != (RegionPtr<T> a, RegionPtr<T> b) => !a.Equals (b);
	}

	/// <summary>A region addressed in another coordinate space (e.g. image coordinates for a tile).</summary>
	public readonly ref struct RegionPtrOffsetView<T> where T : unmanaged
	{
		public RegionPtrOffsetView (RegionPtr<T> region, Point2Int32 location)
		{
			Region = region;
			Location = location;
		}

		public RegionPtr<T> Region { get; }
		public Point2Int32 Location { get; }
		public SizeInt32 Size => Region.Size;
		public int Width => Region.Width;
		public int Height => Region.Height;
		public RectInt32 Bounds => new (Location, Size);
		public ref T this[int x, int y] => ref Region[x - Location.X, y - Location.Y];
		public ref T this[Point2Int32 pt] => ref this[pt.X, pt.Y];
	}

	public static class RegionPtrExtensions
	{
		/// <summary>A view where (offset) is the region's top-left; pass the negated tile location to index by image coordinates.</summary>
		public static RegionPtrOffsetView<T> OffsetView<T> (this RegionPtr<T> region, Point2Int32 offset) where T : unmanaged
			=> new (region, -offset);

		public static RegionPtrOffsetView<T> OffsetView<T> (this RegionPtr<T> region, int offsetX, int offsetY) where T : unmanaged
			=> OffsetView (region, new Point2Int32 (offsetX, offsetY));

		public static RegionPtr<T> Slice<T> (this RegionPtr<T> region, int x, int y, int width, int height) where T : unmanaged
			=> region.Slice (new RectInt32 (x, y, width, height));

		public static RegionPtr<T> Slice<T> (this RegionPtr<T> region, Point2Int32 offset, SizeInt32 size) where T : unmanaged
			=> region.Slice (new RectInt32 (offset, size));

		public static bool CheckOffset<T> (this RegionPtr<T> region, int x, int y) where T : unmanaged
			=> (uint) x < (uint) region.Width && (uint) y < (uint) region.Height;

		public static void CopyToClipped<T> (this RegionPtr<T> src, RegionPtr<T> dst) where T : unmanaged
			=> src.Slice (new RectInt32 (0, 0, Math.Min (src.Width, dst.Width), Math.Min (src.Height, dst.Height))).CopyTo (dst);
	}
}

namespace PaintDotNet.Effects
{
	[Flags]
	public enum BitmapEffectRenderingFlags
	{
		None = 0,
		SingleThreaded = 1,
		DisableSelectionClipping = 2,
		ForceAliasedSelectionQuality = 4,
		FirstTileIsRenderedWithBarrier = 8,
		UninitializedOutputBuffer = 16,
	}

	public enum BitmapEffectRenderingSchedule : long
	{
		None = 0,
		HorizontalStrips = 1,
		SquareTiles = 2,
	}

	public abstract record BitmapEffectOptionsFactory : EffectOptionsBase
	{
		public static BitmapEffectOptions Create () => new ();
	}

	public record BitmapEffectOptions : BitmapEffectOptionsFactory
	{
		public BitmapEffectOptions () { }
	}

	public interface IEffectConfigForm : IDisposable { }

	public interface IEffectSelectionInfo : IDisposable
	{
		RectInt32 RenderBounds { get; }
		IReadOnlyList<RectInt32> RenderScans { get; }
	}

	public interface IBitmapEffectSelectionInfo : IEffectSelectionInfo { }

	public interface IEffectEnvironment : IDisposable
	{
		float BrushSize { get; }
		IEffectSelectionInfo Selection { get; }
		int SourceLayerIndex { get; }
	}

	public interface IEffectEnvironment2 : IEffectEnvironment
	{
		ManagedColor PrimaryColor { get; }
		ManagedColor SecondaryColor { get; }
	}

	public interface IBitmapEffectEnvironment : IEffectEnvironment2
	{
		new IBitmapEffectSelectionInfo Selection { get; }
	}

	public interface IEffectInputBitmap : IBitmapSource
	{
		IColorContext ColorContext { get; }
		IBitmapLock Lock (RectInt32 rect);
	}

	public interface IEffectInputBitmap<TPixel> : IEffectInputBitmap, IBitmapSource<TPixel> where TPixel : unmanaged
	{
		new IBitmapLock<TPixel> Lock (RectInt32 rect);
	}

	public interface IBitmapEffectRenderInfo
	{
		BitmapEffectRenderingFlags Flags { get; set; }
		PixelFormat OutputPixelFormat { get; set; }
		BitmapEffectRenderingSchedule Schedule { get; set; }
	}

	public interface IBitmapEffectOutput
	{
		RectInt32 Bounds { get; }
		PixelFormat PixelFormat { get; }
		IBitmapLock Lock (in PixelFormat pixelFormat);
	}

	public static class BitmapEffectOutputExtensions
	{
		public static IBitmapLock<ColorBgra32> LockBgra32 (this IBitmapEffectOutput output) => Lock<ColorBgra32> (output);

		public static IBitmapLock<TPixel> Lock<TPixel> (this IBitmapEffectOutput output) where TPixel : unmanaged
			=> ((BitmapEffectOutput) output).Lock<TPixel> ();
	}

	public static class EffectEnvironmentExtensions
	{
		public static IEffectInputBitmap<ColorBgra32> GetSourceBitmapBgra32 (this IEffectEnvironment environment) => GetSourceBitmap<ColorBgra32> (environment);

		public static IEffectInputBitmap<TPixel> GetSourceBitmap<TPixel> (this IEffectEnvironment environment) where TPixel : unmanaged
			=> new SurfaceBitmap<TPixel> (((BitmapEffectEnvironment) environment).SourceSurface);
	}

	/// <summary>What the host passes to a BitmapEffect: the source layer, selection and colors.</summary>
	internal sealed class BitmapEffectEnvironment : IBitmapEffectEnvironment, IBitmapEffectSelectionInfo
	{
		private readonly ColorBgra primary, secondary;
		private readonly PdnRegion selection;

		public BitmapEffectEnvironment (Surface source, ColorBgra primary, ColorBgra secondary, float brushSize, PdnRegion selection)
		{
			SourceSurface = source;
			this.primary = primary;
			this.secondary = secondary;
			BrushSize = brushSize;
			this.selection = selection;
		}

		public Surface SourceSurface { get; }
		public float BrushSize { get; }
		public int SourceLayerIndex => 0;
		public ManagedColor PrimaryColor => ManagedColor.FromBgra (primary);
		public ManagedColor SecondaryColor => ManagedColor.FromBgra (secondary);
		public IBitmapEffectSelectionInfo Selection => this;
		IEffectSelectionInfo IEffectEnvironment.Selection => this;
		public RectInt32 RenderBounds => selection?.GetBoundsInt () ?? SourceSurface.Bounds;
		public IReadOnlyList<RectInt32> RenderScans => Array.ConvertAll (selection?.GetRegionScansReadOnlyInt () ?? [SourceSurface.Bounds], r => (RectInt32) r);
		public void Dispose () { }
	}

	/// <summary>One tile of output: a lock over the destination surface at <see cref="Bounds"/>.</summary>
	internal sealed unsafe class BitmapEffectOutput (Surface destination, RectInt32 bounds) : IBitmapEffectOutput
	{
		public RectInt32 Bounds => bounds;
		public PixelFormat PixelFormat => PixelFormats.Bgra32;
		public IBitmapLock Lock (in PixelFormat pixelFormat) => Lock<ColorBgra32> ();

		public IBitmapLock<TPixel> Lock<TPixel> () where TPixel : unmanaged
		{
			if (sizeof (TPixel) != 4)
				throw new NotSupportedException ($"Output pixel type {typeof (TPixel).Name} is not supported; only 32-bit BGRA is");
			return new BitmapLock<TPixel> (destination.GetPointAddressUnchecked (bounds.X, bounds.Y), destination.Stride, bounds.Size);
		}
	}

	internal sealed class BitmapEffectRenderInfo : IBitmapEffectRenderInfo
	{
		public BitmapEffectRenderingFlags Flags { get; set; }
		public PixelFormat OutputPixelFormat { get; set; } = PixelFormats.Bgra32;
		public BitmapEffectRenderingSchedule Schedule { get; set; } = BitmapEffectRenderingSchedule.SquareTiles;
	}

	/// <summary>Base of Paint.NET 5 effects.</summary>
	public abstract class EffectBase : RefTrackedObject
	{
		private protected EffectBase (string name, Image image, string subMenuName, EffectOptionsBase options)
		{
			Name = name;
			Image = image;
			SubMenuName = subMenuName;
			OptionsBase = options ?? new BitmapEffectOptions ();
			Category = GetType ().GetCustomAttribute<EffectCategoryAttribute> (true)?.Category ?? EffectCategory.Effect;
		}

		internal string Name { get; }
		internal Image Image { get; }
		internal string SubMenuName { get; }
		internal EffectCategory Category { get; }
		internal EffectOptionsBase OptionsBase { get; }

		private readonly CancellationTokenSource cancel = new ();
		protected CancellationToken CancellationToken => cancel.Token;
		protected bool IsCancelRequested => cancel.IsCancellationRequested;
		internal void SignalCancel () => cancel.Cancel ();

		protected IServiceProvider Services { get; private set; }
		internal void SetServices (IServiceProvider services) => Services = services;

		protected EffectConfigToken Token { get; private set; }
		internal void SetTokenCore (EffectConfigToken token) => Token = token;

		protected virtual IEffectConfigForm OnCreateConfigForm () => null;

		protected sealed override void Dispose (bool disposing)
		{
			OnDispose (disposing);
			base.Dispose (disposing);
		}

		protected virtual void OnDispose (bool disposing) { }
	}

	public abstract class BitmapEffect : EffectBase
	{
		protected BitmapEffect (string name, IBitmapSource image, string submenuName, BitmapEffectOptions options) : base (name, null, submenuName, options) { }
		protected BitmapEffect (string name, Image image, string submenuName, BitmapEffectOptions options) : base (name, image, submenuName, options) { }
		protected BitmapEffect (string name, string submenuName, BitmapEffectOptions options) : base (name, null, submenuName, options) { }

		protected IBitmapEffectEnvironment Environment { get; private set; }

		internal BitmapEffectRenderInfo RenderInfo { get; } = new ();

		/// <summary>Host entry before the settings UI is built: what the plugin can look at.</summary>
		internal void SetEnvironment (IBitmapEffectEnvironment environment, IServiceProvider services)
		{
			Environment = environment;
			SetServices (services);
		}

		/// <summary>Host entry before rendering: render info, then the token.</summary>
		internal void Initialize (EffectConfigToken token)
		{
			OnInitializeRenderInfo (RenderInfo);
			SetTokenCore (token);
			OnSetToken (token);
		}

		internal void Render (Surface destination, RectInt32 bounds) => OnRender (new BitmapEffectOutput (destination, bounds));

		protected override void OnDispose (bool disposing) { }
		protected virtual void OnInitializeRenderInfo (IBitmapEffectRenderInfo renderInfo) { }
		protected abstract void OnRender (IBitmapEffectOutput output);
		protected virtual void OnSetToken (EffectConfigToken newToken) { }
	}

	public abstract class BitmapEffect<TToken> : BitmapEffect where TToken : EffectConfigToken
	{
		protected BitmapEffect (string name, IBitmapSource image, string submenuName, BitmapEffectOptions options) : base (name, image, submenuName, options) { }
		protected BitmapEffect (string name, Image image, string submenuName, BitmapEffectOptions options) : base (name, image, submenuName, options) { }
		protected BitmapEffect (string name, string submenuName, BitmapEffectOptions options) : base (name, submenuName, options) { }

		protected new TToken Token => (TToken) base.Token;
		protected sealed override void OnSetToken (EffectConfigToken newToken) => OnSetToken ((TToken) newToken);
		protected virtual void OnSetToken (TToken newToken) { }
	}

	public abstract class PropertyBasedBitmapEffect : BitmapEffect<PropertyBasedEffectConfigToken>, IPropertyBasedEffect
	{
		protected PropertyBasedBitmapEffect (string name, IBitmapSource image, string submenuName, BitmapEffectOptions options) : base (name, image, submenuName, options) { }
		protected PropertyBasedBitmapEffect (string name, Image image, string submenuName, BitmapEffectOptions options) : base (name, image, submenuName, options) { }
		protected PropertyBasedBitmapEffect (string name, string submenuName, BitmapEffectOptions options) : base (name, submenuName, options) { }

		public PropertyCollection CreatePropertyCollection () => OnCreatePropertyCollection ();
		public ControlInfo CreateConfigUI (PropertyCollection props) => OnCreateConfigUI (props);
		public static ControlInfo CreateDefaultConfigUI (IEnumerable<Property> props) => ControlInfo.CreateDefaultConfigUI (props);

		protected sealed override IEffectConfigForm OnCreateConfigForm () => null;
		protected abstract PropertyCollection OnCreatePropertyCollection ();
		protected virtual ControlInfo OnCreateConfigUI (PropertyCollection props) => CreateDefaultConfigUI (props);
		protected virtual void OnCustomizeConfigUIWindowProperties (PropertyCollection props) { }

		PropertyCollection IPropertyBasedEffect.CreateWindowProperties ()
		{
			PropertyCollection props = PropertyBasedEffect.DefaultWindowProperties (Name);
			OnCustomizeConfigUIWindowProperties (props);
			return props;
		}
	}

	/// <summary>What the host's settings dialog needs from both kinds of property-based effects.</summary>
	internal interface IPropertyBasedEffect
	{
		PropertyCollection CreatePropertyCollection ();
		ControlInfo CreateConfigUI (PropertyCollection props);
		PropertyCollection CreateWindowProperties ();
	}
}
