using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PaintDotNet.Rendering
{
	public readonly struct Point2Int32 : IEquatable<Point2Int32>
	{
		public Point2Int32 (int x, int y) { X = x; Y = y; }
		public int X { get; }
		public int Y { get; }
		public static Point2Int32 Zero => default;
		public static Point2Int32 operator - (Point2Int32 p) => new (-p.X, -p.Y);
		public static Point2Int32 operator + (Point2Int32 a, Point2Int32 b) => new (a.X + b.X, a.Y + b.Y);
		public static Point2Int32 operator - (Point2Int32 a, Point2Int32 b) => new (a.X - b.X, a.Y - b.Y);
		public static bool operator == (Point2Int32 a, Point2Int32 b) => a.Equals (b);
		public static bool operator != (Point2Int32 a, Point2Int32 b) => !a.Equals (b);
		public static implicit operator Point2Int32 (Point p) => new (p.X, p.Y);
		public static implicit operator Point (Point2Int32 p) => new (p.X, p.Y);
		public bool Equals (Point2Int32 other) => X == other.X && Y == other.Y;
		public override bool Equals (object obj) => obj is Point2Int32 p && Equals (p);
		public override int GetHashCode () => HashCode.Combine (X, Y);
		public override string ToString () => $"({X}, {Y})";
	}

	public readonly struct SizeInt32 : IEquatable<SizeInt32>
	{
		public SizeInt32 (int width, int height) { Width = width; Height = height; }
		public int Width { get; }
		public int Height { get; }
		public long Area => (long) Width * Height;
		public bool IsEmpty => Width == 0 || Height == 0;
		public static implicit operator SizeInt32 (Size s) => new (s.Width, s.Height);
		public static implicit operator Size (SizeInt32 s) => new (s.Width, s.Height);
		public static bool operator == (SizeInt32 a, SizeInt32 b) => a.Equals (b);
		public static bool operator != (SizeInt32 a, SizeInt32 b) => !a.Equals (b);
		public bool Equals (SizeInt32 other) => Width == other.Width && Height == other.Height;
		public override bool Equals (object obj) => obj is SizeInt32 s && Equals (s);
		public override int GetHashCode () => HashCode.Combine (Width, Height);
	}

	public readonly struct RectInt32 : IEquatable<RectInt32>
	{
		public RectInt32 (int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
		public RectInt32 (Point2Int32 location, SizeInt32 size) : this (location.X, location.Y, size.Width, size.Height) { }
		public RectInt32 (int x, int y, SizeInt32 size) : this (x, y, size.Width, size.Height) { }

		public int X { get; }
		public int Y { get; }
		public int Width { get; }
		public int Height { get; }
		public int Left => X;
		public int Top => Y;
		public int Right => X + Width;
		public int Bottom => Y + Height;
		public Point2Int32 Location => new (X, Y);
		public SizeInt32 Size => new (Width, Height);
		public bool HasZeroArea => Width == 0 || Height == 0;
		public long Area => (long) Width * Height;

		public bool Contains (int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
		public bool Contains (Point2Int32 p) => Contains (p.X, p.Y);
		public bool Contains (RectInt32 r) => r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;
		public bool IntersectsWith (RectInt32 r) => r.X < Right && X < r.Right && r.Y < Bottom && Y < r.Bottom;
		public static RectInt32 FromEdges (int left, int top, int right, int bottom) => new (left, top, right - left, bottom - top);
		public static RectInt32 Intersect (RectInt32 a, RectInt32 b) => Rectangle.Intersect (a, b);
		public static RectInt32 Union (RectInt32 a, RectInt32 b) => Rectangle.Union (a, b);
		public RectInt32 Intersect (RectInt32 r) => Intersect (this, r);
		public RectInt32 Offset (int dx, int dy) => new (X + dx, Y + dy, Width, Height);
		public RectInt32 Inflate (int dx, int dy) => new (X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

		public static implicit operator RectInt32 (Rectangle r) => new (r.X, r.Y, r.Width, r.Height);
		public static implicit operator Rectangle (RectInt32 r) => new (r.X, r.Y, r.Width, r.Height);
		public static bool operator == (RectInt32 a, RectInt32 b) => a.Equals (b);
		public static bool operator != (RectInt32 a, RectInt32 b) => !a.Equals (b);
		public bool Equals (RectInt32 other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
		public override bool Equals (object obj) => obj is RectInt32 r && Equals (r);
		public override int GetHashCode () => HashCode.Combine (X, Y, Width, Height);
		public override string ToString () => $"({X}, {Y}, {Width}, {Height})";
	}

	public readonly struct Vector2Double : IEquatable<Vector2Double>
	{
		public Vector2Double (double x, double y) { X = x; Y = y; }
		public double X { get; }
		public double Y { get; }
		public static Vector2Double Zero => default;
		public double Length => Math.Sqrt (X * X + Y * Y);
		public static Vector2Double operator + (Vector2Double a, Vector2Double b) => new (a.X + b.X, a.Y + b.Y);
		public static Vector2Double operator - (Vector2Double a, Vector2Double b) => new (a.X - b.X, a.Y - b.Y);
		public static Vector2Double operator * (Vector2Double a, double s) => new (a.X * s, a.Y * s);
		public static bool operator == (Vector2Double a, Vector2Double b) => a.Equals (b);
		public static bool operator != (Vector2Double a, Vector2Double b) => !a.Equals (b);
		public static implicit operator Pair<double, double> (Vector2Double v) => new (v.X, v.Y);
		public static implicit operator Vector2Double (Pair<double, double> p) => new (p.First, p.Second);
		public bool Equals (Vector2Double other) => X == other.X && Y == other.Y;
		public override bool Equals (object obj) => obj is Vector2Double v && Equals (v);
		public override int GetHashCode () => HashCode.Combine (X, Y);
		public override string ToString () => $"({X}, {Y})";
	}

	public readonly struct Vector3Double : IEquatable<Vector3Double>
	{
		public Vector3Double (double x, double y, double z) { X = x; Y = y; Z = z; }
		public double X { get; }
		public double Y { get; }
		public double Z { get; }
		public static implicit operator Tuple<double, double, double> (Vector3Double v) => Tuple.Create (v.X, v.Y, v.Z);
		public static implicit operator Vector3Double (Tuple<double, double, double> t) => new (t.Item1, t.Item2, t.Item3);
		public bool Equals (Vector3Double other) => X == other.X && Y == other.Y && Z == other.Z;
		public override bool Equals (object obj) => obj is Vector3Double v && Equals (v);
		public override int GetHashCode () => HashCode.Combine (X, Y, Z);
	}
}

namespace PaintDotNet.Imaging
{
	/// <summary>The Paint.NET 5 name for a straight-alpha BGRA32 pixel.</summary>
	[StructLayout (LayoutKind.Explicit)]
	public struct ColorBgra32 : IEquatable<ColorBgra32>
	{
		[FieldOffset (0)] private byte b;
		[FieldOffset (1)] private byte g;
		[FieldOffset (2)] private byte r;
		[FieldOffset (3)] private byte a;
		[FieldOffset (0)] private uint bgra;

		public byte B { readonly get => b; set => b = value; }
		public byte G { readonly get => g; set => g = value; }
		public byte R { readonly get => r; set => r = value; }
		public byte A { readonly get => a; set => a = value; }
		public uint Bgra { readonly get => bgra; set => bgra = value; }

		public ColorBgra32 (byte b, byte g, byte r, byte a) : this () { this.b = b; this.g = g; this.r = r; this.a = a; }
		public static ColorBgra32 FromBgra (byte b, byte g, byte r, byte a) => new (b, g, r, a);
		public static ColorBgra32 FromBgr (byte b, byte g, byte r) => new (b, g, r, 255);
		public static ColorBgra32 FromUInt32 (uint bgra) => new () { Bgra = bgra };
		public bool Equals (ColorBgra32 other) => bgra == other.bgra;
		public override bool Equals (object obj) => obj is ColorBgra32 c && Equals (c);
		public override int GetHashCode () => (int) bgra;
		public static bool operator == (ColorBgra32 x, ColorBgra32 y) => x.bgra == y.bgra;
		public static bool operator != (ColorBgra32 x, ColorBgra32 y) => x.bgra != y.bgra;
	}
}
