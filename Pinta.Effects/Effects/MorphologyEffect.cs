using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET-style Distort > Morphology: per-channel maximum (Dilate) or
/// minimum (Erode) over a rectangle around each pixel. Dilate grows light
/// regions, Erode grows dark ones.
/// </summary>
public sealed class MorphologyEffect : BaseEffect
{
	public override string Icon
		=> Resources.Icons.EffectsDistortPixelate;

	public sealed override bool IsTileable
		=> true;

	public override string Name
		// Translators: Image morphology, an effect that grows (dilates) or shrinks (erodes) the regions of an image
		=> Translations.GetString ("Morphology");

	public override bool IsConfigurable
		=> true;

	public override string EffectMenuCategory
		=> Translations.GetString ("Distort");

	public MorphologyData Data
		=> (MorphologyData) EffectData!; // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public MorphologyEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new MorphologyData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		MorphologyData data = Data;
		int radiusX = data.Width;
		int radiusY = data.Linked ? data.Width : data.Height;
		Apply (
			source.GetReadOnlyPixelData (),
			destination.GetPixelData (),
			source.Width,
			source.Height,
			roi,
			radiusX,
			radiusY,
			data.Mode == MorphologyMode.Dilate);
	}

	/// <summary>
	/// Separable rectangular min/max filter: a horizontal pass into a scratch
	/// buffer covering the rows the vertical pass needs, then a vertical pass.
	/// Pixels outside the image are ignored. Min/max per channel keeps
	/// premultiplied colours valid.
	/// </summary>
	internal static void Apply (
		ReadOnlySpan<ColorBgra> src,
		Span<ColorBgra> dst,
		int width,
		int height,
		RectangleI roi,
		int radiusX,
		int radiusY,
		bool dilate)
	{
		// ponytail: O(radius) per pixel per pass; switch to van Herk/Gil-Werman if large radii get slow.
		int top = Math.Max (0, roi.Top - radiusY);
		int bottom = Math.Min (height - 1, roi.Bottom + radiusY);
		int columns = roi.Width;
		ColorBgra[] rows = new ColorBgra[(bottom - top + 1) * columns];

		for (int y = top; y <= bottom; ++y) {
			ReadOnlySpan<ColorBgra> row = src.Slice (y * width, width);
			for (int x = roi.Left; x <= roi.Right; ++x) {
				int x0 = Math.Max (0, x - radiusX);
				int x1 = Math.Min (width - 1, x + radiusX);
				rows[(y - top) * columns + x - roi.Left] = Reduce (row, x0, x1, 1, dilate);
			}
		}

		for (int y = roi.Top; y <= roi.Bottom; ++y) {
			int y0 = Math.Max (0, y - radiusY) - top;
			int y1 = Math.Min (height - 1, y + radiusY) - top;
			for (int x = roi.Left; x <= roi.Right; ++x) {
				int column = x - roi.Left;
				dst[y * width + x] = Reduce (rows, y0 * columns + column, y1 * columns + column, columns, dilate);
			}
		}
	}

	private static ColorBgra Reduce (ReadOnlySpan<ColorBgra> data, int first, int last, int stride, bool dilate)
	{
		ColorBgra c = data[first];
		int b = c.B, g = c.G, r = c.R, a = c.A;
		for (int i = first + stride; i <= last; i += stride) {
			c = data[i];
			if (dilate) {
				b = Math.Max (b, c.B); g = Math.Max (g, c.G); r = Math.Max (r, c.R); a = Math.Max (a, c.A);
			} else {
				b = Math.Min (b, c.B); g = Math.Min (g, c.G); r = Math.Min (r, c.R); a = Math.Min (a, c.A);
			}
		}
		return ColorBgra.FromBgra ((byte) b, (byte) g, (byte) r, (byte) a);
	}

	public enum MorphologyMode
	{
		// Translators: Morphology mode that grows the light regions of the image
		[Caption ("Dilate")] Dilate,

		// Translators: Morphology mode that grows the dark regions of the image
		[Caption ("Erode")] Erode,
	}

	public sealed class MorphologyData : EffectData
	{
		[Caption ("Width")]
		[MinimumValue (0), MaximumValue (100)]
		public int Width { get; set; } = 5;

		[Caption ("Height")]
		[MinimumValue (0), MaximumValue (100)]
		[VisibleWhen (nameof (ShowHeight))]
		public int Height { get; set; } = 5;

		[Caption ("Linked")]
		public bool Linked { get; set; } = true;

		[Caption ("Mode")]
		public MorphologyMode Mode { get; set; } = MorphologyMode.Dilate;

		[Skip]
		public bool ShowHeight => !Linked;

		[Skip]
		public override bool IsDefault => Width == 0 && (Linked || Height == 0);
	}
}
