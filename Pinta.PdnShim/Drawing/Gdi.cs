using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using PaintDotNet.Hosting;

namespace System.Drawing
{
	public enum GraphicsUnit
	{
		World = 0,
		Display = 1,
		Pixel = 2,
		Point = 3,
		Inch = 4,
		Document = 5,
		Millimeter = 6,
	}

	[Flags]
	public enum FontStyle
	{
		Regular = 0,
		Bold = 1,
		Italic = 2,
		Underline = 4,
		Strikeout = 8,
	}

	public enum StringAlignment
	{
		Near = 0,
		Center = 1,
		Far = 2,
	}

	[Flags]
	public enum StringFormatFlags
	{
		DirectionRightToLeft = 0x1,
		DirectionVertical = 0x2,
		FitBlackBox = 0x4,
		DisplayFormatControl = 0x20,
		NoFontFallback = 0x400,
		MeasureTrailingSpaces = 0x800,
		NoWrap = 0x1000,
		LineLimit = 0x2000,
		NoClip = 0x4000,
	}

	public enum StringTrimming
	{
		None = 0,
		Character = 1,
		Word = 2,
		EllipsisCharacter = 3,
		EllipsisWord = 4,
		EllipsisPath = 5,
	}

	public sealed class StringFormat : IDisposable, ICloneable
	{
		public StringFormat () { }
		public StringFormat (StringFormatFlags options) { FormatFlags = options; }
		public StringFormat (StringFormat format) { Alignment = format.Alignment; LineAlignment = format.LineAlignment; FormatFlags = format.FormatFlags; Trimming = format.Trimming; }
		public StringAlignment Alignment { get; set; }
		public StringAlignment LineAlignment { get; set; }
		public StringFormatFlags FormatFlags { get; set; }
		public StringTrimming Trimming { get; set; }
		public static StringFormat GenericDefault => new ();
		public static StringFormat GenericTypographic => new () { FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.LineLimit };
		public object Clone () => new StringFormat (this);
		public void Dispose () { }
	}

	public sealed class FontFamily : IDisposable
	{
		public FontFamily (string name) { Name = name; }
		public FontFamily (GenericFontFamilies genericFamily) : this (genericFamily switch { GenericFontFamilies.Serif => "Serif", GenericFontFamilies.Monospace => "Monospace", _ => "Sans" }) { }
		public string Name { get; }
		public static FontFamily GenericSansSerif => new ("Sans");
		public static FontFamily GenericSerif => new ("Serif");
		public static FontFamily GenericMonospace => new ("Monospace");
		public static FontFamily[] Families => new Text.InstalledFontCollection ().Families;
		public bool IsStyleAvailable (FontStyle style) => true;
		public int GetEmHeight (FontStyle style) => 2048;
		public int GetCellAscent (FontStyle style) => 1854;
		public int GetCellDescent (FontStyle style) => 434;
		public int GetLineSpacing (FontStyle style) => 2355;
		public string GetName (int language) => Name;
		public override bool Equals (object obj) => obj is FontFamily f && f.Name == Name;
		public override int GetHashCode () => Name.GetHashCode ();
		public override string ToString () => $"[FontFamily: Name={Name}]";
		public void Dispose () { }
	}

	public enum GenericFontFamilies
	{
		Serif = 0,
		SansSerif = 1,
		Monospace = 2,
	}

	public sealed class Font : IDisposable, ICloneable
	{
		public Font (string familyName, float emSize) : this (new FontFamily (familyName), emSize, FontStyle.Regular, GraphicsUnit.Point) { }
		public Font (string familyName, float emSize, FontStyle style) : this (new FontFamily (familyName), emSize, style, GraphicsUnit.Point) { }
		public Font (string familyName, float emSize, GraphicsUnit unit) : this (new FontFamily (familyName), emSize, FontStyle.Regular, unit) { }
		public Font (string familyName, float emSize, FontStyle style, GraphicsUnit unit) : this (new FontFamily (familyName), emSize, style, unit) { }
		public Font (string familyName, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet) : this (new FontFamily (familyName), emSize, style, unit) { }
		public Font (FontFamily family, float emSize) : this (family, emSize, FontStyle.Regular, GraphicsUnit.Point) { }
		public Font (FontFamily family, float emSize, FontStyle style) : this (family, emSize, style, GraphicsUnit.Point) { }
		public Font (FontFamily family, float emSize, GraphicsUnit unit) : this (family, emSize, FontStyle.Regular, unit) { }
		public Font (FontFamily family, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet) : this (family, emSize, style, unit) { }
		public Font (Font prototype, FontStyle newStyle) : this (prototype.FontFamily, prototype.Size, newStyle, prototype.Unit) { }

		public Font (FontFamily family, float emSize, FontStyle style, GraphicsUnit unit)
		{
			FontFamily = family;
			Size = emSize;
			Style = style;
			Unit = unit;
		}

		public FontFamily FontFamily { get; }
		public string Name => FontFamily.Name;
		public float Size { get; }
		public FontStyle Style { get; }
		public GraphicsUnit Unit { get; }
		public bool Bold => (Style & FontStyle.Bold) != 0;
		public bool Italic => (Style & FontStyle.Italic) != 0;
		public bool Underline => (Style & FontStyle.Underline) != 0;
		public bool Strikeout => (Style & FontStyle.Strikeout) != 0;
		public float SizeInPoints => Unit == GraphicsUnit.Point ? Size : SizeInPixels * 72f / 96f;
		public byte GdiCharSet => 1;
		public bool GdiVerticalFont => false;
		public string OriginalFontName => Name;
		public string SystemFontName => string.Empty;
		public bool IsSystemFont => false;

		/// <summary>The em size in device pixels at 96 DPI.</summary>
		internal float SizeInPixels => Unit switch {
			GraphicsUnit.Pixel or GraphicsUnit.World or GraphicsUnit.Display => Size,
			GraphicsUnit.Inch => Size * 96f,
			GraphicsUnit.Document => Size * 96f / 300f,
			GraphicsUnit.Millimeter => Size * 96f / 25.4f,
			_ => Size * 96f / 72f,
		};

		public int Height => (int) Math.Ceiling (GetHeight ());
		public float GetHeight () => SizeInPixels * 1.15f;
		public float GetHeight (float dpi) => GetHeight () * dpi / 96f;
		public float GetHeight (Graphics graphics) => GetHeight ();

		internal Pango.FontDescription ToPango ()
		{
			Pango.FontDescription d = Pango.FontDescription.New ();
			d.SetFamily (Name);
			d.SetWeight (Bold ? Pango.Weight.Bold : Pango.Weight.Normal);
			d.SetStyle (Italic ? Pango.Style.Italic : Pango.Style.Normal);
			d.SetAbsoluteSize (SizeInPixels * Pango.Constants.SCALE);
			return d;
		}

		public object Clone () => new Font (FontFamily, Size, Style, Unit);
		public void Dispose () { }
		public override string ToString () => $"[Font: Name={Name}, Size={Size}, Units={(int) Unit}]";
	}

	public abstract class Brush : IDisposable, ICloneable
	{
		private protected Brush () { }
		internal abstract void SetSource (Cairo.Context cr);
		public abstract object Clone ();
		public void Dispose () { }
	}

	public sealed class SolidBrush : Brush
	{
		public SolidBrush (Color color) { Color = color; }
		public Color Color { get; set; }
		internal override void SetSource (Cairo.Context cr) => cr.SetSourceRgba (Color.R / 255.0, Color.G / 255.0, Color.B / 255.0, Color.A / 255.0);
		public override object Clone () => new SolidBrush (Color);
	}

	public sealed class TextureBrush : Brush
	{
		public TextureBrush (Image image) { Image = image; }
		public TextureBrush (Image image, WrapMode wrapMode) { Image = image; }
		public Image Image { get; }
		public WrapMode WrapMode { get; set; } = WrapMode.Tile;
		internal override void SetSource (Cairo.Context cr)
		{
			Cairo.ImageSurface s = CairoInterop.ToCairo ((Bitmap) Image);
			Cairo.SurfacePattern p = new (s);
			p.Extend = Cairo.Extend.Repeat;
			cr.SetSource (p);
		}
		public override object Clone () => new TextureBrush (Image);
	}

	public sealed class Pen : IDisposable, ICloneable
	{
		public Pen (Color color) : this (color, 1) { }
		public Pen (Color color, float width) { Color = color; Width = width; }
		public Pen (Brush brush) : this (brush, 1) { }
		public Pen (Brush brush, float width) { Brush = brush; Width = width; }

		private Brush brush;

		public Color Color {
			get => (brush as SolidBrush)?.Color ?? Color.Black;
			set => brush = new SolidBrush (value);
		}

		public Brush Brush {
			get => brush;
			set => brush = value;
		}

		public float Width { get; set; }
		public DashStyle DashStyle { get; set; }
		public float[] DashPattern { get; set; }
		public float DashOffset { get; set; }
		public LineCap StartCap { get; set; }
		public LineCap EndCap { get; set; }
		public DashCap DashCap { get; set; }
		public LineJoin LineJoin { get; set; }
		public PenAlignment Alignment { get; set; }
		public float MiterLimit { get; set; } = 10;
		public void SetLineCap (LineCap startCap, LineCap endCap, DashCap dashCap) { StartCap = startCap; EndCap = endCap; DashCap = dashCap; }
		public object Clone () => new Pen (Brush, Width) { DashStyle = DashStyle, StartCap = StartCap, EndCap = EndCap, LineJoin = LineJoin, Alignment = Alignment };
		public void Dispose () { }

		internal void Apply (Cairo.Context cr)
		{
			brush.SetSource (cr);
			cr.LineWidth = Math.Max (Width, 1e-3);
			cr.LineCap = StartCap switch {
				LineCap.Round or LineCap.RoundAnchor => Cairo.LineCap.Round,
				LineCap.Square or LineCap.SquareAnchor => Cairo.LineCap.Square,
				_ => Cairo.LineCap.Butt,
			};
			cr.LineJoin = LineJoin switch {
				LineJoin.Round => Cairo.LineJoin.Round,
				LineJoin.Bevel => Cairo.LineJoin.Bevel,
				_ => Cairo.LineJoin.Miter,
			};
			cr.MiterLimit = MiterLimit;
			double w = Math.Max (Width, 1);
			double[] dashes = DashStyle switch {
				DashStyle.Dash => [3 * w, w],
				DashStyle.Dot => [w, w],
				DashStyle.DashDot => [3 * w, w, w, w],
				DashStyle.DashDotDot => [3 * w, w, w, w, w, w],
				DashStyle.Custom when DashPattern is { Length: > 0 } => DashPattern.Select (d => d * w).ToArray (),
				_ => [],
			};
			cr.SetDash (dashes, DashOffset * w);
		}
	}

	public static class Pens
	{
		public static Pen Black => new (Color.Black);
		public static Pen White => new (Color.White);
		public static Pen Red => new (Color.Red);
		public static Pen Green => new (Color.Green);
		public static Pen Blue => new (Color.Blue);
		public static Pen Gray => new (Color.Gray);
		public static Pen LightBlue => new (Color.LightBlue);
		public static Pen LightGray => new (Color.LightGray);
		public static Pen DarkGray => new (Color.DarkGray);
		public static Pen Yellow => new (Color.Yellow);
		public static Pen Transparent => new (Color.Transparent);
	}

	public static class Brushes
	{
		public static Brush Black => new SolidBrush (Color.Black);
		public static Brush White => new SolidBrush (Color.White);
		public static Brush Red => new SolidBrush (Color.Red);
		public static Brush Green => new SolidBrush (Color.Green);
		public static Brush Blue => new SolidBrush (Color.Blue);
		public static Brush Gray => new SolidBrush (Color.Gray);
		public static Brush LightGray => new SolidBrush (Color.LightGray);
		public static Brush DarkGray => new SolidBrush (Color.DarkGray);
		public static Brush Yellow => new SolidBrush (Color.Yellow);
		public static Brush Transparent => new SolidBrush (Color.Transparent);
	}

	/// <summary>A clip region kept as a list of rectangles.</summary>
	public sealed class Region : IDisposable
	{
		internal List<RectangleF> Rects { get; } = [];

		public Region () { MakeInfinite (); }
		public Region (Rectangle rect) { Rects.Add (rect); }
		public Region (RectangleF rect) { Rects.Add (rect); }
		public Region (GraphicsPath path) { Rects.Add (path.GetBounds ()); }

		internal Rectangle Bounds => Rects.Count == 0 ? Rectangle.Empty : Rectangle.Round (Rects.Aggregate (RectangleF.Union));

		public void MakeInfinite () { Rects.Clear (); Rects.Add (new RectangleF (-4194304, -4194304, 8388608, 8388608)); }
		public void MakeEmpty () => Rects.Clear ();
		public void Intersect (Rectangle rect) => Intersect ((RectangleF) rect);
		public void Intersect (RectangleF rect)
		{
			for (int i = Rects.Count - 1; i >= 0; i--) {
				RectangleF r = RectangleF.Intersect (Rects[i], rect);
				if (r.IsEmpty) Rects.RemoveAt (i); else Rects[i] = r;
			}
		}
		public void Intersect (Region region) { RectangleF b = region.Bounds; Intersect (b); }
		public void Union (Rectangle rect) => Rects.Add (rect);
		public void Union (RectangleF rect) => Rects.Add (rect);
		public void Union (Region region) => Rects.AddRange (region.Rects);
		public void Exclude (Rectangle rect) { }
		public RectangleF GetBounds (Graphics g) => Bounds;
		public bool IsVisible (Point point) => Rects.Any (r => r.Contains (point));
		public bool IsVisible (PointF point) => Rects.Any (r => r.Contains (point));
		public bool IsEmpty (Graphics g) => Rects.Count == 0;
		public Region Clone () { Region r = new (); r.Rects.Clear (); r.Rects.AddRange (Rects); return r; }
		public void Dispose () { }
	}

	/// <summary>Converts between straight-alpha bitmaps and premultiplied Cairo surfaces.</summary>
	internal static unsafe class CairoInterop
	{
		public static Cairo.Matrix ToCairo (Matrix3x2 m)
		{
			Cairo.Matrix c = new ();
			c.Init (m.M11, m.M12, m.M21, m.M22, m.M31, m.M32); // xx, yx, xy, yy, x0, y0
			return c;
		}

		public static Cairo.ImageSurface ToCairo (Bitmap b) => ToCairo (b, new Rectangle (0, 0, b.Width, b.Height));

		public static Cairo.ImageSurface ToCairo (Bitmap b, Rectangle r)
		{
			Cairo.ImageSurface s = new (Cairo.Format.Argb32, Math.Max (1, r.Width), Math.Max (1, r.Height));
			s.Flush ();
			Span<byte> data = s.GetData ();
			int stride = s.Stride;
			fixed (byte* d0 = data) {
				for (int y = 0; y < r.Height; y++) {
					PaintDotNet.ColorBgra* src = b.Row (r.Y + y) + r.X;
					PaintDotNet.ColorBgra* dst = (PaintDotNet.ColorBgra*) (d0 + y * stride);
					for (int x = 0; x < r.Width; x++) dst[x] = src[x].ConvertToPremultipliedAlpha ();
				}
			}
			s.MarkDirty ();
			return s;
		}

		public static void FromCairo (Cairo.ImageSurface s, Bitmap b, Rectangle r)
		{
			s.Flush ();
			Span<byte> data = s.GetData ();
			int stride = s.Stride;
			fixed (byte* d0 = data) {
				for (int y = 0; y < r.Height; y++) {
					PaintDotNet.ColorBgra* src = (PaintDotNet.ColorBgra*) (d0 + y * stride);
					PaintDotNet.ColorBgra* dst = b.Row (r.Y + y) + r.X;
					for (int x = 0; x < r.Width; x++) dst[x] = src[x].ConvertFromPremultipliedAlpha ();
				}
			}
		}
	}

	/// <summary>
	/// GDI+-style drawing onto a straight-alpha bitmap, implemented with Cairo and Pango.
	/// Each call converts only the affected rectangle to premultiplied alpha, draws, and converts back.
	/// </summary>
	public sealed class Graphics : IDisposable
	{
		private readonly Bitmap target;
		private Matrix3x2 transform = Matrix3x2.Identity;
		private Region clip;

		private Graphics (Bitmap target) { this.target = target; }

		public static Graphics FromImage (Image image) => new ((Bitmap) image);

		public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.None;
		public Text.TextRenderingHint TextRenderingHint { get; set; }
		public CompositingMode CompositingMode { get; set; }
		public CompositingQuality CompositingQuality { get; set; }
		public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.Bilinear;
		public PixelOffsetMode PixelOffsetMode { get; set; }
		public GraphicsUnit PageUnit { get; set; } = GraphicsUnit.Pixel;
		public float PageScale { get; set; } = 1;
		public int TextContrast { get; set; } = 4;
		public float DpiX => 96;
		public float DpiY => 96;

		public Region Clip {
			get => clip?.Clone () ?? new Region ();
			set => clip = value?.Clone ();
		}

		public RectangleF ClipBounds => clip?.Bounds ?? new RectangleF (0, 0, target.Width, target.Height);
		public RectangleF VisibleClipBounds => RectangleF.Intersect (ClipBounds, new RectangleF (0, 0, target.Width, target.Height));
		public bool IsClipEmpty => clip is not null && clip.Rects.Count == 0;

		public void SetClip (Rectangle rect) => clip = new Region (rect);
		public void SetClip (RectangleF rect) => clip = new Region (rect);
		public void SetClip (Region region, CombineMode combineMode) { if (combineMode == CombineMode.Intersect && clip is not null) clip.Intersect (region); else clip = region.Clone (); }
		public void SetClip (Rectangle rect, CombineMode combineMode) => SetClip (new Region (rect), combineMode);
		public void SetClip (GraphicsPath path) => clip = new Region (path);
		public void ResetClip () => clip = null;
		public void IntersectClip (Rectangle rect) { if (clip is null) clip = new Region (rect); else clip.Intersect (rect); }
		public void IntersectClip (RectangleF rect) { if (clip is null) clip = new Region (rect); else clip.Intersect (rect); }

		public Drawing2D.Matrix Transform {
			get => new (transform);
			set => transform = value.Value;
		}

		public void ResetTransform () => transform = Matrix3x2.Identity;
		public void TranslateTransform (float dx, float dy) => TranslateTransform (dx, dy, MatrixOrder.Prepend);
		public void TranslateTransform (float dx, float dy, MatrixOrder order) => Combine (Matrix3x2.CreateTranslation (dx, dy), order);
		public void RotateTransform (float angle) => RotateTransform (angle, MatrixOrder.Prepend);
		public void RotateTransform (float angle, MatrixOrder order) => Combine (Matrix3x2.CreateRotation (angle * MathF.PI / 180f), order);
		public void ScaleTransform (float sx, float sy) => ScaleTransform (sx, sy, MatrixOrder.Prepend);
		public void ScaleTransform (float sx, float sy, MatrixOrder order) => Combine (Matrix3x2.CreateScale (sx, sy), order);
		public void MultiplyTransform (Drawing2D.Matrix matrix) => Combine (matrix.Value, MatrixOrder.Prepend);
		public void MultiplyTransform (Drawing2D.Matrix matrix, MatrixOrder order) => Combine (matrix.Value, order);
		private void Combine (Matrix3x2 m, MatrixOrder order) => transform = order == MatrixOrder.Prepend ? m * transform : transform * m;

		public GraphicsState Save () => new (transform, clip?.Clone (), SmoothingMode);
		public void Restore (GraphicsState gstate) { transform = gstate.Transform; clip = gstate.Clip; SmoothingMode = gstate.Smoothing; }

		public void Flush () { }
		public void Flush (FlushIntention intention) { }
		public void Dispose () { }

		// --- Core: run a Cairo drawing on the affected rectangle of the bitmap ---

		/// <summary>Draws a path; <paramref name="antialias"/> overrides <see cref="SmoothingMode"/> (text follows <see cref="TextRenderingHint"/>).</summary>
		private void Draw (Action<Cairo.Context> path, Action<Cairo.Context> paint, float padding, bool? antialias = null)
		{
			// Measure the device-space extents of the path.
			RectangleF extents;
			using (Cairo.ImageSurface probe = new (Cairo.Format.A8, 1, 1))
			using (Cairo.Context pc = new (probe)) {
				SetMatrix (pc, 0, 0);
				path (pc);
				pc.IdentityMatrix ();
				// Path extents, not fill extents: a line has no fill area but still needs its rectangle drawn.
				pc.PathExtents (out double x1, out double y1, out double x2, out double y2);
				extents = RectangleF.FromLTRB ((float) x1, (float) y1, (float) x2, (float) y2);
			}
			float scale = MathF.Sqrt (MathF.Abs (transform.GetDeterminant ()));
			extents.Inflate (padding * Math.Max (1, scale) + 2, padding * Math.Max (1, scale) + 2);
			Run (extents, cr => {
				path (cr);
				paint (cr);
			}, antialias);
		}

		/// <summary>Runs <paramref name="draw"/> (user space = bitmap space with the current transform) clipped to <paramref name="deviceBounds"/>.</summary>
		private void Run (RectangleF deviceBounds, Action<Cairo.Context> draw, bool? antialias = null)
		{
			Rectangle r = Rectangle.Intersect (Rectangle.Round (RectangleF.Inflate (deviceBounds, 1, 1)), new Rectangle (0, 0, target.Width, target.Height));
			if (clip is not null) r = Rectangle.Intersect (r, Rectangle.Ceiling (clip.Bounds));
			if (r.Width <= 0 || r.Height <= 0) return;
			using Cairo.ImageSurface s = CairoInterop.ToCairo (target, r);
			using (Cairo.Context cr = new (s)) {
				cr.Translate (-r.X, -r.Y);
				if (clip is not null) {
					foreach (RectangleF c in clip.Rects) cr.Rectangle (c.X, c.Y, c.Width, c.Height);
					cr.Clip ();
				}
				bool aa = antialias ?? SmoothingMode is SmoothingMode.AntiAlias or SmoothingMode.HighQuality;
				cr.Antialias = aa ? Cairo.Antialias.Default : Cairo.Antialias.None;
				if (CompositingMode == CompositingMode.SourceCopy) cr.Operator = Cairo.Operator.Source;
				SetMatrix (cr, r.X, r.Y);
				draw (cr);
			}
			CairoInterop.FromCairo (s, target, r);
			target.EncodedData = null;
		}

		private void SetMatrix (Cairo.Context cr, int offsetX, int offsetY)
		{
			Matrix3x2 m = transform * Matrix3x2.CreateTranslation (-offsetX, -offsetY);
			cr.SetMatrix (CairoInterop.ToCairo (m));
		}

		private void FillPath (Brush brush, Action<Cairo.Context> path, Cairo.FillRule rule = Cairo.FillRule.Winding)
			=> Draw (path, cr => { cr.FillRule = rule; brush.SetSource (cr); cr.Fill (); }, 0);

		private void StrokePath (Pen pen, Action<Cairo.Context> path)
			=> Draw (path, cr => { pen.Apply (cr); cr.Stroke (); }, pen.Width + pen.MiterLimit);

		private static Cairo.FillRule Rule (FillMode mode) => mode == FillMode.Winding ? Cairo.FillRule.Winding : Cairo.FillRule.EvenOdd;

		// --- Clear ---

		public void Clear (Color color)
		{
			Run (new RectangleF (0, 0, target.Width, target.Height), cr => {
				cr.IdentityMatrix ();
				cr.Operator = Cairo.Operator.Source;
				cr.SetSourceRgba (color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);
				cr.Paint ();
			});
		}

		// --- Rectangles ---

		public void FillRectangle (Brush brush, Rectangle rect) => FillRectangle (brush, rect.X, rect.Y, rect.Width, rect.Height);
		public void FillRectangle (Brush brush, RectangleF rect) => FillRectangle (brush, rect.X, rect.Y, rect.Width, rect.Height);
		public void FillRectangle (Brush brush, int x, int y, int width, int height) => FillRectangle (brush, (float) x, y, width, height);
		public void FillRectangle (Brush brush, float x, float y, float width, float height) => FillPath (brush, cr => cr.Rectangle (x, y, width, height));
		public void FillRectangles (Brush brush, Rectangle[] rects) { foreach (Rectangle r in rects) FillRectangle (brush, r); }
		public void FillRectangles (Brush brush, RectangleF[] rects) { foreach (RectangleF r in rects) FillRectangle (brush, r); }
		public void DrawRectangle (Pen pen, Rectangle rect) => DrawRectangle (pen, rect.X, rect.Y, rect.Width, rect.Height);
		public void DrawRectangle (Pen pen, int x, int y, int width, int height) => DrawRectangle (pen, (float) x, y, width, height);
		public void DrawRectangle (Pen pen, float x, float y, float width, float height) => StrokePath (pen, cr => cr.Rectangle (x + 0.5, y + 0.5, width, height));
		public void DrawRectangles (Pen pen, Rectangle[] rects) { foreach (Rectangle r in rects) DrawRectangle (pen, r); }
		public void DrawRectangles (Pen pen, RectangleF[] rects) { foreach (RectangleF r in rects) DrawRectangle (pen, r.X, r.Y, r.Width, r.Height); }

		// --- Ellipses, arcs, pies ---

		private static void Ellipse (Cairo.Context cr, double x, double y, double w, double h, double start = 0, double sweep = 360, bool pie = false)
		{
			if (w <= 0 || h <= 0) return;
			cr.Save ();
			cr.Translate (x + w / 2, y + h / 2);
			cr.Scale (w / 2, h / 2);
			double a1 = start * Math.PI / 180, a2 = (start + sweep) * Math.PI / 180;
			if (pie) cr.MoveTo (0, 0);
			if (sweep >= 0) cr.Arc (0, 0, 1, a1, a2); else cr.ArcNegative (0, 0, 1, a1, a2);
			if (pie) cr.ClosePath ();
			cr.Restore ();
		}

		public void FillEllipse (Brush brush, Rectangle rect) => FillEllipse (brush, rect.X, rect.Y, rect.Width, rect.Height);
		public void FillEllipse (Brush brush, RectangleF rect) => FillEllipse (brush, rect.X, rect.Y, rect.Width, rect.Height);
		public void FillEllipse (Brush brush, int x, int y, int width, int height) => FillEllipse (brush, (float) x, y, width, height);
		public void FillEllipse (Brush brush, float x, float y, float width, float height) => FillPath (brush, cr => Ellipse (cr, x, y, width, height));
		public void DrawEllipse (Pen pen, Rectangle rect) => DrawEllipse (pen, rect.X, rect.Y, rect.Width, rect.Height);
		public void DrawEllipse (Pen pen, RectangleF rect) => DrawEllipse (pen, rect.X, rect.Y, rect.Width, rect.Height);
		public void DrawEllipse (Pen pen, int x, int y, int width, int height) => DrawEllipse (pen, (float) x, y, width, height);
		public void DrawEllipse (Pen pen, float x, float y, float width, float height) => StrokePath (pen, cr => Ellipse (cr, x, y, width, height));
		public void DrawArc (Pen pen, Rectangle rect, float startAngle, float sweepAngle) => DrawArc (pen, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
		public void DrawArc (Pen pen, RectangleF rect, float startAngle, float sweepAngle) => DrawArc (pen, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
		public void DrawArc (Pen pen, int x, int y, int width, int height, int startAngle, int sweepAngle) => DrawArc (pen, (float) x, y, width, height, startAngle, sweepAngle);
		public void DrawArc (Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle) => StrokePath (pen, cr => Ellipse (cr, x, y, width, height, startAngle, sweepAngle));
		public void FillPie (Brush brush, Rectangle rect, float startAngle, float sweepAngle) => FillPie (brush, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
		public void FillPie (Brush brush, int x, int y, int width, int height, int startAngle, int sweepAngle) => FillPie (brush, (float) x, y, width, height, startAngle, sweepAngle);
		public void FillPie (Brush brush, float x, float y, float width, float height, float startAngle, float sweepAngle) => FillPath (brush, cr => Ellipse (cr, x, y, width, height, startAngle, sweepAngle, true));
		public void DrawPie (Pen pen, Rectangle rect, float startAngle, float sweepAngle) => DrawPie (pen, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
		public void DrawPie (Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle) => StrokePath (pen, cr => Ellipse (cr, x, y, width, height, startAngle, sweepAngle, true));

		// --- Lines and polygons ---

		public void DrawLine (Pen pen, Point pt1, Point pt2) => DrawLine (pen, (float) pt1.X, pt1.Y, pt2.X, pt2.Y);
		public void DrawLine (Pen pen, PointF pt1, PointF pt2) => DrawLine (pen, pt1.X, pt1.Y, pt2.X, pt2.Y);
		public void DrawLine (Pen pen, int x1, int y1, int x2, int y2) => DrawLine (pen, (float) x1, y1, x2, y2);
		public void DrawLine (Pen pen, float x1, float y1, float x2, float y2) => StrokePath (pen, cr => { cr.MoveTo (x1, y1); cr.LineTo (x2, y2); });
		public void DrawLines (Pen pen, Point[] points) => DrawLines (pen, ToF (points));
		public void DrawLines (Pen pen, PointF[] points) => StrokePath (pen, cr => Poly (cr, points, false));
		public void DrawPolygon (Pen pen, Point[] points) => DrawPolygon (pen, ToF (points));
		public void DrawPolygon (Pen pen, PointF[] points) => StrokePath (pen, cr => Poly (cr, points, true));
		public void FillPolygon (Brush brush, Point[] points) => FillPolygon (brush, ToF (points), FillMode.Alternate);
		public void FillPolygon (Brush brush, Point[] points, FillMode fillMode) => FillPolygon (brush, ToF (points), fillMode);
		public void FillPolygon (Brush brush, PointF[] points) => FillPolygon (brush, points, FillMode.Alternate);
		public void FillPolygon (Brush brush, PointF[] points, FillMode fillMode) => FillPath (brush, cr => Poly (cr, points, true), Rule (fillMode));

		internal static PointF[] ToF (Point[] points) => Array.ConvertAll (points, p => (PointF) p);

		internal static void Poly (Cairo.Context cr, PointF[] points, bool close)
		{
			if (points.Length == 0) return;
			cr.MoveTo (points[0].X, points[0].Y);
			for (int i = 1; i < points.Length; i++) cr.LineTo (points[i].X, points[i].Y);
			if (close) cr.ClosePath ();
		}

		// --- Cardinal splines (curves through the points) ---

		public void DrawCurve (Pen pen, PointF[] points) => DrawCurve (pen, points, 0.5f);
		public void DrawCurve (Pen pen, Point[] points) => DrawCurve (pen, ToF (points), 0.5f);
		public void DrawCurve (Pen pen, Point[] points, float tension) => DrawCurve (pen, ToF (points), tension);
		public void DrawCurve (Pen pen, PointF[] points, float tension) => StrokePath (pen, cr => Spline (cr, points, tension, false));
		public void DrawClosedCurve (Pen pen, PointF[] points) => StrokePath (pen, cr => Spline (cr, points, 0.5f, true));
		public void DrawClosedCurve (Pen pen, Point[] points) => DrawClosedCurve (pen, ToF (points));
		public void FillClosedCurve (Brush brush, PointF[] points) => FillClosedCurve (brush, points, FillMode.Alternate, 0.5f);
		public void FillClosedCurve (Brush brush, Point[] points) => FillClosedCurve (brush, ToF (points), FillMode.Alternate, 0.5f);
		public void FillClosedCurve (Brush brush, PointF[] points, FillMode fillmode) => FillClosedCurve (brush, points, fillmode, 0.5f);
		public void FillClosedCurve (Brush brush, PointF[] points, FillMode fillmode, float tension) => FillPath (brush, cr => Spline (cr, points, tension, true), Rule (fillmode));

		internal static void Spline (Cairo.Context cr, PointF[] p, float tension, bool closed)
		{
			int n = p.Length;
			if (n < 2) return;
			PointF At (int i) => closed ? p[((i % n) + n) % n] : p[Math.Clamp (i, 0, n - 1)];
			float k = tension / 3f;
			cr.MoveTo (p[0].X, p[0].Y);
			int segments = closed ? n : n - 1;
			for (int i = 0; i < segments; i++) {
				PointF p0 = At (i - 1), p1 = At (i), p2 = At (i + 1), p3 = At (i + 2);
				cr.CurveTo (
					p1.X + k * (p2.X - p0.X), p1.Y + k * (p2.Y - p0.Y),
					p2.X - k * (p3.X - p1.X), p2.Y - k * (p3.Y - p1.Y),
					p2.X, p2.Y);
			}
			if (closed) cr.ClosePath ();
		}

		// --- Paths ---

		public void FillPath (Brush brush, GraphicsPath path) => FillPath (brush, path.AddTo, Rule (path.FillMode));
		public void DrawPath (Pen pen, GraphicsPath path) => StrokePath (pen, path.AddTo);
		public void FillRegion (Brush brush, Region region) { foreach (RectangleF r in region.Rects) FillRectangle (brush, r); }

		// --- Images ---

		public void DrawImage (Image image, Point point) => DrawImage (image, point.X, point.Y);
		public void DrawImage (Image image, PointF point) => DrawImage (image, point.X, point.Y);
		public void DrawImage (Image image, int x, int y) => DrawImage (image, (float) x, y);
		public void DrawImage (Image image, float x, float y) => DrawImage (image, new RectangleF (x, y, image.Width, image.Height));
		public void DrawImageUnscaled (Image image, int x, int y) => DrawImage (image, x, y);
		public void DrawImageUnscaled (Image image, Point point) => DrawImage (image, point);
		public void DrawImage (Image image, Rectangle rect) => DrawImage (image, (RectangleF) rect);
		public void DrawImage (Image image, int x, int y, int width, int height) => DrawImage (image, new RectangleF (x, y, width, height));
		public void DrawImage (Image image, float x, float y, float width, float height) => DrawImage (image, new RectangleF (x, y, width, height));
		public void DrawImage (Image image, RectangleF rect) => DrawImage (image, rect, new RectangleF (0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
		public void DrawImage (Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit) => DrawImage (image, (RectangleF) destRect, (RectangleF) srcRect, srcUnit);
		public void DrawImage (Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit) => DrawImage (image, destRect, new Rectangle (srcX, srcY, srcWidth, srcHeight), srcUnit);

		public void DrawImage (Image image, RectangleF destRect, RectangleF srcRect, GraphicsUnit srcUnit)
		{
			if (srcRect.Width <= 0 || srcRect.Height <= 0) return;
			using Cairo.ImageSurface src = CairoInterop.ToCairo ((Bitmap) image);
			Draw (cr => cr.Rectangle (destRect.X, destRect.Y, destRect.Width, destRect.Height), cr => {
				cr.Translate (destRect.X, destRect.Y);
				cr.Scale (destRect.Width / srcRect.Width, destRect.Height / srcRect.Height);
				cr.SetSourceSurface (src, -srcRect.X, -srcRect.Y);
				using Cairo.Pattern p = cr.GetSource ();
				if (InterpolationMode == InterpolationMode.NearestNeighbor) ((Cairo.SurfacePattern) p).Filter = Cairo.Filter.Nearest;
				cr.Fill ();
			}, 0);
		}

		// --- Text ---

		public SizeF MeasureString (string text, Font font) => MeasureString (text, font, 0, null);
		public SizeF MeasureString (string text, Font font, int width) => MeasureString (text, font, width, null);
		public SizeF MeasureString (string text, Font font, SizeF layoutArea) => MeasureString (text, font, (int) layoutArea.Width, null);
		public SizeF MeasureString (string text, Font font, SizeF layoutArea, StringFormat stringFormat) => MeasureString (text, font, (int) layoutArea.Width, stringFormat);
		public SizeF MeasureString (string text, Font font, PointF origin, StringFormat stringFormat) => MeasureString (text, font, 0, stringFormat);

		public SizeF MeasureString (string text, Font font, int width, StringFormat format)
		{
			using Cairo.ImageSurface probe = new (Cairo.Format.A8, 1, 1);
			using Cairo.Context cr = new (probe);
			Pango.Layout layout = TextLayout (cr, text, font, width, format);
			layout.GetPixelSize (out int w, out int h);
			return new SizeF (w + font.SizeInPixels / 3f, h);
		}

		public void DrawString (string s, Font font, Brush brush, float x, float y) => DrawString (s, font, brush, new RectangleF (x, y, 0, 0), null);
		public void DrawString (string s, Font font, Brush brush, float x, float y, StringFormat format) => DrawString (s, font, brush, new RectangleF (x, y, 0, 0), format);
		public void DrawString (string s, Font font, Brush brush, PointF point) => DrawString (s, font, brush, new RectangleF (point, SizeF.Empty), null);
		public void DrawString (string s, Font font, Brush brush, PointF point, StringFormat format) => DrawString (s, font, brush, new RectangleF (point, SizeF.Empty), format);
		public void DrawString (string s, Font font, Brush brush, RectangleF layoutRectangle) => DrawString (s, font, brush, layoutRectangle, null);

		public void DrawString (string s, Font font, Brush brush, RectangleF layoutRectangle, StringFormat format)
		{
			if (string.IsNullOrEmpty (s)) return;
			Draw (cr => TextPath (cr, s, font, layoutRectangle, format), cr => {
				brush.SetSource (cr);
				cr.Fill ();
			}, font.SizeInPixels, TextRenderingHint is not (Text.TextRenderingHint.SingleBitPerPixel or Text.TextRenderingHint.SingleBitPerPixelGridFit));
		}

		/// <summary>Adds the outline of the text, positioned like GDI+ (with its small left padding).</summary>
		internal static void TextPath (Cairo.Context cr, string s, Font font, RectangleF rect, StringFormat format)
		{
			int width = rect.Width > 0 && (format?.FormatFlags & StringFormatFlags.NoWrap) == 0 ? (int) rect.Width : 0;
			Pango.Layout layout = TextLayout (cr, s, font, width, format);
			layout.GetPixelSize (out int w, out int h);
			double pad = font.SizeInPixels / 6.0;
			double x = rect.X + pad, y = rect.Y;
			StringAlignment align = format?.Alignment ?? StringAlignment.Near;
			StringAlignment lineAlign = format?.LineAlignment ?? StringAlignment.Near;
			if (rect.Width > 0) {
				if (align == StringAlignment.Center) x = rect.X + (rect.Width - w) / 2.0;
				else if (align == StringAlignment.Far) x = rect.Right - w - pad;
			} else {
				if (align == StringAlignment.Center) x = rect.X - w / 2.0;
				else if (align == StringAlignment.Far) x = rect.X - w - pad;
			}
			if (rect.Height > 0) {
				if (lineAlign == StringAlignment.Center) y = rect.Y + (rect.Height - h) / 2.0;
				else if (lineAlign == StringAlignment.Far) y = rect.Bottom - h;
			} else {
				if (lineAlign == StringAlignment.Center) y = rect.Y - h / 2.0;
				else if (lineAlign == StringAlignment.Far) y = rect.Y - h;
			}
			cr.MoveTo (x, y);
			PangoCairo.Functions.LayoutPath (cr, layout);
			if (font.Underline || font.Strikeout) {
				double t = Math.Max (1, font.SizeInPixels / 14.0);
				double baseline = y + layout.GetBaseline () / (double) Pango.Constants.SCALE;
				if (font.Underline) cr.Rectangle (x, baseline + t, w, t);
				if (font.Strikeout) cr.Rectangle (x, baseline - font.SizeInPixels * 0.3, w, t);
			}
		}

		private static Pango.Layout TextLayout (Cairo.Context cr, string s, Font font, int width, StringFormat format)
		{
			Pango.Layout layout = PangoCairo.Functions.CreateLayout (cr);
			using Pango.FontDescription desc = font.ToPango ();
			layout.SetFontDescription (desc);
			layout.SetText (s, -1);
			if (width > 0) {
				layout.SetWidth (width * Pango.Constants.SCALE);
				layout.SetWrap (Pango.WrapMode.Word);
			}
			layout.SetAlignment ((format?.Alignment ?? StringAlignment.Near) switch {
				StringAlignment.Center => Pango.Alignment.Center,
				StringAlignment.Far => Pango.Alignment.Right,
				_ => Pango.Alignment.Left,
			});
			return layout;
		}
	}

	public sealed class GraphicsState
	{
		internal GraphicsState (Matrix3x2 transform, Region clip, SmoothingMode smoothing) { Transform = transform; Clip = clip; Smoothing = smoothing; }
		internal Matrix3x2 Transform { get; }
		internal Region Clip { get; }
		internal SmoothingMode Smoothing { get; }
	}
}

namespace System.Drawing.Text
{
	public enum TextRenderingHint
	{
		SystemDefault = 0,
		SingleBitPerPixelGridFit = 1,
		SingleBitPerPixel = 2,
		AntiAliasGridFit = 3,
		AntiAlias = 4,
		ClearTypeGridFit = 5,
	}

	public abstract class FontCollection : IDisposable
	{
		private protected FontCollection () { }
		public FontFamily[] Families => GetFamilies ();
		private protected abstract FontFamily[] GetFamilies ();
		public void Dispose () { }
	}

	public sealed class InstalledFontCollection : FontCollection
	{
		private static FontFamily[] cache;

		public InstalledFontCollection () { }

		private protected override FontFamily[] GetFamilies () => cache ??= Load ();

		private static FontFamily[] Load ()
		{
			try {
				Pango.FontMap map = PangoCairo.Functions.FontMapGetDefault ();
				List<string> names = [];
				for (uint i = 0; i < map.GetNItems (); i++)
					if (map.GetObject (i) is Pango.FontFamily f) names.Add (f.GetName ());
				return names.Distinct ().OrderBy (n => n, StringComparer.OrdinalIgnoreCase).Select (n => new FontFamily (n)).ToArray ();
			} catch (Exception) {
				return [new FontFamily ("Sans"), new FontFamily ("Serif"), new FontFamily ("Monospace")];
			}
		}
	}

	public sealed class PrivateFontCollection : FontCollection
	{
		private readonly List<FontFamily> families = [];
		private protected override FontFamily[] GetFamilies () => [.. families];
		public void AddFontFile (string filename) { }
		public void AddMemoryFont (IntPtr memory, int length) { }
	}
}

namespace System.Drawing.Drawing2D
{
	public enum SmoothingMode { Invalid = -1, Default = 0, HighSpeed = 1, HighQuality = 2, None = 3, AntiAlias = 4 }
	public enum CompositingMode { SourceOver = 0, SourceCopy = 1 }
	public enum CompositingQuality { Invalid = -1, Default = 0, HighSpeed = 1, HighQuality = 2, GammaCorrected = 3, AssumeLinear = 4 }
	public enum InterpolationMode { Invalid = -1, Default = 0, Low = 1, High = 2, Bilinear = 3, Bicubic = 4, NearestNeighbor = 5, HighQualityBilinear = 6, HighQualityBicubic = 7 }
	public enum PixelOffsetMode { Invalid = -1, Default = 0, HighSpeed = 1, HighQuality = 2, None = 3, Half = 4 }
	public enum FillMode { Alternate = 0, Winding = 1 }
	public enum MatrixOrder { Prepend = 0, Append = 1 }
	public enum CombineMode { Replace = 0, Intersect = 1, Union = 2, Xor = 3, Exclude = 4, Complement = 5 }
	public enum FlushIntention { Flush = 0, Sync = 1 }
	public enum DashStyle { Solid = 0, Dash = 1, Dot = 2, DashDot = 3, DashDotDot = 4, Custom = 5 }
	public enum DashCap { Flat = 0, Round = 2, Triangle = 3 }
	public enum LineJoin { Miter = 0, Bevel = 1, Round = 2, MiterClipped = 3 }
	public enum PenAlignment { Center = 0, Inset = 1, Outset = 2, Left = 3, Right = 4 }
	public enum WrapMode { Tile = 0, TileFlipX = 1, TileFlipY = 2, TileFlipXY = 3, Clamp = 4 }

	public enum LineCap
	{
		Flat = 0,
		Square = 1,
		Round = 2,
		Triangle = 3,
		NoAnchor = 0x10,
		SquareAnchor = 0x11,
		RoundAnchor = 0x12,
		DiamondAnchor = 0x13,
		ArrowAnchor = 0x14,
		Custom = 0xff,
		AnchorMask = 0xf0,
	}

	public enum HatchStyle
	{
		Horizontal = 0, Vertical = 1, ForwardDiagonal = 2, BackwardDiagonal = 3, Cross = 4, DiagonalCross = 5,
		Percent05 = 6, Percent10 = 7, Percent20 = 8, Percent25 = 9, Percent30 = 10, Percent40 = 11, Percent50 = 12,
		Percent60 = 13, Percent70 = 14, Percent75 = 15, Percent80 = 16, Percent90 = 17, LightDownwardDiagonal = 18,
		LightUpwardDiagonal = 19, DarkDownwardDiagonal = 20, DarkUpwardDiagonal = 21, WideDownwardDiagonal = 22,
		WideUpwardDiagonal = 23, LightVertical = 24, LightHorizontal = 25, NarrowVertical = 26, NarrowHorizontal = 27,
		DarkVertical = 28, DarkHorizontal = 29, DashedDownwardDiagonal = 30, DashedUpwardDiagonal = 31,
		DashedHorizontal = 32, DashedVertical = 33, SmallConfetti = 34, LargeConfetti = 35, ZigZag = 36, Wave = 37,
		DiagonalBrick = 38, HorizontalBrick = 39, Weave = 40, Plaid = 41, Divot = 42, DottedGrid = 43,
		DottedDiamond = 44, Shingle = 45, Trellis = 46, Sphere = 47, SmallGrid = 48, SmallCheckerBoard = 49,
		LargeCheckerBoard = 50, OutlinedDiamond = 51, SolidDiamond = 52,
		LargeGrid = Cross, Min = Horizontal, Max = LargeGrid,
	}

	/// <summary>A hatch pattern approximated by an 8x8 tile of foreground/background pixels.</summary>
	public sealed class HatchBrush : Brush
	{
		public HatchBrush (HatchStyle hatchstyle, Color foreColor) : this (hatchstyle, foreColor, Color.Black) { }
		public HatchBrush (HatchStyle hatchstyle, Color foreColor, Color backColor) { HatchStyle = hatchstyle; ForegroundColor = foreColor; BackgroundColor = backColor; }
		public HatchStyle HatchStyle { get; }
		public Color ForegroundColor { get; }
		public Color BackgroundColor { get; }

		private bool On (int x, int y) => HatchStyle switch {
			HatchStyle.Horizontal => y == 0,
			HatchStyle.Vertical => x == 0,
			HatchStyle.ForwardDiagonal => x == y,
			HatchStyle.BackwardDiagonal => x == 7 - y,
			HatchStyle.DiagonalCross => x == y || x == 7 - y,
			HatchStyle.Percent50 or HatchStyle.SmallCheckerBoard => ((x + y) & 1) == 0,
			HatchStyle.LargeCheckerBoard => ((x / 4 + y / 4) & 1) == 0,
			HatchStyle.Percent25 => (x & 1) == 0 && (y & 1) == 0,
			HatchStyle.Percent75 => !((x & 1) == 1 && (y & 1) == 1),
			_ => x == 0 || y == 0,
		};

		internal override void SetSource (Cairo.Context cr)
		{
			Bitmap tile = new (8, 8);
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++)
					tile.SetPixel (x, y, On (x, y) ? ForegroundColor : BackgroundColor);
			new TextureBrush (tile).SetSource (cr);
		}

		public override object Clone () => new HatchBrush (HatchStyle, ForegroundColor, BackgroundColor);
	}

	public sealed class LinearGradientBrush : Brush
	{
		private readonly PointF p1, p2;
		public LinearGradientBrush (PointF point1, PointF point2, Color color1, Color color2) { p1 = point1; p2 = point2; LinearColors = [color1, color2]; }
		public LinearGradientBrush (Point point1, Point point2, Color color1, Color color2) : this ((PointF) point1, (PointF) point2, color1, color2) { }
		public LinearGradientBrush (Rectangle rect, Color color1, Color color2, float angle) : this ((RectangleF) rect, color1, color2, angle) { }
		public LinearGradientBrush (RectangleF rect, Color color1, Color color2, float angle)
		{
			double a = angle * Math.PI / 180;
			PointF c = new (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
			float r = (float) (Math.Abs (rect.Width * Math.Cos (a)) + Math.Abs (rect.Height * Math.Sin (a))) / 2;
			p1 = new PointF (c.X - r * (float) Math.Cos (a), c.Y - r * (float) Math.Sin (a));
			p2 = new PointF (c.X + r * (float) Math.Cos (a), c.Y + r * (float) Math.Sin (a));
			LinearColors = [color1, color2];
		}
		public LinearGradientBrush (Rectangle rect, Color color1, Color color2, LinearGradientMode mode) : this (rect, color1, color2, mode switch { LinearGradientMode.Vertical => 90f, LinearGradientMode.ForwardDiagonal => 45f, LinearGradientMode.BackwardDiagonal => 135f, _ => 0f }) { }
		public Color[] LinearColors { get; set; }
		public WrapMode WrapMode { get; set; }
		internal override void SetSource (Cairo.Context cr)
		{
			Cairo.LinearGradient g = new (p1.X, p1.Y, p2.X, p2.Y);
			Color a = LinearColors[0], b = LinearColors[^1];
			g.AddColorStopRgba (0, a.R / 255.0, a.G / 255.0, a.B / 255.0, a.A / 255.0);
			g.AddColorStopRgba (1, b.R / 255.0, b.G / 255.0, b.B / 255.0, b.A / 255.0);
			cr.SetSource (g);
		}
		public override object Clone () => new LinearGradientBrush (p1, p2, LinearColors[0], LinearColors[^1]);
	}

	public enum LinearGradientMode { Horizontal = 0, Vertical = 1, ForwardDiagonal = 2, BackwardDiagonal = 3 }

	/// <summary>A 3x2 affine matrix.</summary>
	public sealed class Matrix : IDisposable, ICloneable
	{
		internal Matrix3x2 Value;

		public Matrix () { Value = Matrix3x2.Identity; }
		public Matrix (float m11, float m12, float m21, float m22, float dx, float dy) { Value = new Matrix3x2 (m11, m12, m21, m22, dx, dy); }
		internal Matrix (Matrix3x2 value) { Value = value; }

		public float[] Elements => [Value.M11, Value.M12, Value.M21, Value.M22, Value.M31, Value.M32];
		public float OffsetX => Value.M31;
		public float OffsetY => Value.M32;
		public bool IsIdentity => Value.IsIdentity;
		public bool IsInvertible => Matrix3x2.Invert (Value, out _);

		public void Reset () => Value = Matrix3x2.Identity;
		public void Multiply (Matrix matrix) => Multiply (matrix, MatrixOrder.Prepend);
		public void Multiply (Matrix matrix, MatrixOrder order) => Combine (matrix.Value, order);
		public void Translate (float offsetX, float offsetY) => Translate (offsetX, offsetY, MatrixOrder.Prepend);
		public void Translate (float offsetX, float offsetY, MatrixOrder order) => Combine (Matrix3x2.CreateTranslation (offsetX, offsetY), order);
		public void Scale (float scaleX, float scaleY) => Scale (scaleX, scaleY, MatrixOrder.Prepend);
		public void Scale (float scaleX, float scaleY, MatrixOrder order) => Combine (Matrix3x2.CreateScale (scaleX, scaleY), order);
		public void Rotate (float angle) => Rotate (angle, MatrixOrder.Prepend);
		public void Rotate (float angle, MatrixOrder order) => Combine (Matrix3x2.CreateRotation (angle * MathF.PI / 180f), order);
		public void RotateAt (float angle, PointF point) => RotateAt (angle, point, MatrixOrder.Prepend);
		public void RotateAt (float angle, PointF point, MatrixOrder order) => Combine (Matrix3x2.CreateRotation (angle * MathF.PI / 180f, new Vector2 (point.X, point.Y)), order);
		public void Shear (float shearX, float shearY) => Combine (new Matrix3x2 (1, shearY, shearX, 1, 0, 0), MatrixOrder.Prepend);
		public void Invert () { if (Matrix3x2.Invert (Value, out Matrix3x2 inv)) Value = inv; }
		private void Combine (Matrix3x2 m, MatrixOrder order) => Value = order == MatrixOrder.Prepend ? m * Value : Value * m;

		public void TransformPoints (PointF[] pts)
		{
			for (int i = 0; i < pts.Length; i++) {
				Vector2 v = Vector2.Transform (new Vector2 (pts[i].X, pts[i].Y), Value);
				pts[i] = new PointF (v.X, v.Y);
			}
		}

		public void TransformPoints (Point[] pts)
		{
			for (int i = 0; i < pts.Length; i++) {
				Vector2 v = Vector2.Transform (new Vector2 (pts[i].X, pts[i].Y), Value);
				pts[i] = new Point ((int) MathF.Round (v.X), (int) MathF.Round (v.Y));
			}
		}

		public void TransformVectors (PointF[] pts)
		{
			for (int i = 0; i < pts.Length; i++) {
				Vector2 v = Vector2.TransformNormal (new Vector2 (pts[i].X, pts[i].Y), Value);
				pts[i] = new PointF (v.X, v.Y);
			}
		}

		public Matrix Clone () => new (Value);
		object ICloneable.Clone () => Clone ();
		public void Dispose () { }
	}

	/// <summary>A recorded sequence of figures, replayed onto a Cairo context when drawn.</summary>
	public sealed class GraphicsPath : IDisposable, ICloneable
	{
		private readonly List<(Matrix3x2 Transform, Action<Cairo.Context> Add)> figures = [];

		public GraphicsPath () { }
		public GraphicsPath (FillMode fillMode) { FillMode = fillMode; }

		public FillMode FillMode { get; set; }
		public int PointCount => figures.Count * 4;

		private void Add (Action<Cairo.Context> add) => figures.Add ((Matrix3x2.Identity, add));

		internal void AddTo (Cairo.Context cr)
		{
			foreach (var (t, add) in figures) {
				cr.Save ();
				cr.Transform (CairoInterop.ToCairo (t));
				add (cr);
				cr.Restore ();
			}
		}

		public void Reset () => figures.Clear ();
		public void StartFigure () { }
		public void CloseFigure () => Add (cr => cr.ClosePath ());
		public void CloseAllFigures () => CloseFigure ();

		public void Transform (Matrix matrix)
		{
			for (int i = 0; i < figures.Count; i++)
				figures[i] = (figures[i].Transform * matrix.Value, figures[i].Add);
		}

		public void AddLine (PointF pt1, PointF pt2) => AddLine (pt1.X, pt1.Y, pt2.X, pt2.Y);
		public void AddLine (Point pt1, Point pt2) => AddLine (pt1.X, pt1.Y, pt2.X, pt2.Y);
		public void AddLine (int x1, int y1, int x2, int y2) => AddLine ((float) x1, y1, x2, y2);
		public void AddLine (float x1, float y1, float x2, float y2) => Add (cr => { cr.MoveTo (x1, y1); cr.LineTo (x2, y2); });
		public void AddLines (PointF[] points) => Add (cr => Graphics.Poly (cr, points, false));
		public void AddLines (Point[] points) => AddLines (Graphics.ToF (points));
		public void AddPolygon (PointF[] points) => Add (cr => Graphics.Poly (cr, (PointF[]) points.Clone (), true));
		public void AddPolygon (Point[] points) => AddPolygon (Graphics.ToF (points));
		public void AddRectangle (RectangleF rect) => Add (cr => cr.Rectangle (rect.X, rect.Y, rect.Width, rect.Height));
		public void AddRectangle (Rectangle rect) => AddRectangle ((RectangleF) rect);
		public void AddRectangles (Rectangle[] rects) { foreach (Rectangle r in rects) AddRectangle (r); }
		public void AddRectangles (RectangleF[] rects) { foreach (RectangleF r in rects) AddRectangle (r); }
		public void AddEllipse (RectangleF rect) => AddEllipse (rect.X, rect.Y, rect.Width, rect.Height);
		public void AddEllipse (Rectangle rect) => AddEllipse (rect.X, rect.Y, rect.Width, rect.Height);
		public void AddEllipse (int x, int y, int width, int height) => AddEllipse ((float) x, y, width, height);
		public void AddEllipse (float x, float y, float width, float height) => Add (cr => { cr.NewSubPath (); EllipsePath (cr, x, y, width, height, 0, 360); cr.ClosePath (); });
		public void AddArc (RectangleF rect, float startAngle, float sweepAngle) => Add (cr => EllipsePath (cr, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle));
		public void AddArc (Rectangle rect, float startAngle, float sweepAngle) => AddArc ((RectangleF) rect, startAngle, sweepAngle);
		public void AddArc (float x, float y, float width, float height, float startAngle, float sweepAngle) => AddArc (new RectangleF (x, y, width, height), startAngle, sweepAngle);
		public void AddPie (Rectangle rect, float startAngle, float sweepAngle) => Add (cr => { cr.MoveTo (rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0); EllipsePath (cr, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle); cr.ClosePath (); });
		public void AddBezier (PointF pt1, PointF pt2, PointF pt3, PointF pt4) => Add (cr => { cr.MoveTo (pt1.X, pt1.Y); cr.CurveTo (pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y); });
		public void AddBezier (Point pt1, Point pt2, Point pt3, Point pt4) => AddBezier ((PointF) pt1, pt2, pt3, pt4);
		public void AddCurve (PointF[] points) => AddCurve (points, 0.5f);
		public void AddCurve (PointF[] points, float tension) => Add (cr => Graphics.Spline (cr, (PointF[]) points.Clone (), tension, false));
		public void AddClosedCurve (PointF[] points) => AddClosedCurve (points, 0.5f);
		public void AddClosedCurve (PointF[] points, float tension) => Add (cr => Graphics.Spline (cr, (PointF[]) points.Clone (), tension, true));
		public void AddPath (GraphicsPath addingPath, bool connect) => figures.AddRange (addingPath.figures);

		/// <summary>GDI+ emSize here is in world units (pixels), unlike Font which defaults to points.</summary>
		public void AddString (string s, FontFamily family, int style, float emSize, PointF origin, StringFormat format)
			=> AddString (s, family, style, emSize, new RectangleF (origin, SizeF.Empty), format);

		public void AddString (string s, FontFamily family, int style, float emSize, Point origin, StringFormat format)
			=> AddString (s, family, style, emSize, new RectangleF (origin, SizeF.Empty), format);

		public void AddString (string s, FontFamily family, int style, float emSize, Rectangle layoutRect, StringFormat format)
			=> AddString (s, family, style, emSize, (RectangleF) layoutRect, format);

		public void AddString (string s, FontFamily family, int style, float emSize, RectangleF layoutRect, StringFormat format)
		{
			Font font = new (family, emSize, (FontStyle) style, GraphicsUnit.Pixel);
			Add (cr => { cr.NewSubPath (); Graphics.TextPath (cr, s, font, layoutRect, format); });
		}

		private static void EllipsePath (Cairo.Context cr, double x, double y, double w, double h, double start, double sweep)
		{
			if (w <= 0 || h <= 0) return;
			cr.Save ();
			cr.Translate (x + w / 2, y + h / 2);
			cr.Scale (w / 2, h / 2);
			double a1 = start * Math.PI / 180, a2 = (start + sweep) * Math.PI / 180;
			if (sweep >= 0) cr.Arc (0, 0, 1, a1, a2); else cr.ArcNegative (0, 0, 1, a1, a2);
			cr.Restore ();
		}

		public RectangleF GetBounds ()
		{
			using Cairo.ImageSurface probe = new (Cairo.Format.A8, 1, 1);
			using Cairo.Context cr = new (probe);
			AddTo (cr);
			cr.PathExtents (out double x1, out double y1, out double x2, out double y2);
			return RectangleF.FromLTRB ((float) x1, (float) y1, (float) x2, (float) y2);
		}

		public RectangleF GetBounds (Matrix matrix) => GetBounds ();

		public bool IsVisible (PointF point) => IsVisible (point.X, point.Y);
		public bool IsVisible (float x, float y)
		{
			using Cairo.ImageSurface probe = new (Cairo.Format.A8, 1, 1);
			using Cairo.Context cr = new (probe);
			cr.FillRule = FillMode == FillMode.Winding ? Cairo.FillRule.Winding : Cairo.FillRule.EvenOdd;
			AddTo (cr);
			return cr.InFill (x, y);
		}

		public GraphicsPath Clone () { GraphicsPath p = new (FillMode); p.figures.AddRange (figures); return p; }
		object ICloneable.Clone () => Clone ();
		public void Dispose () { }
	}
}
