using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class HighlightsShadowsEffect : BaseEffect
{
	// Tiles are single rows, and each would have to blur 6 * radius rows around itself; blur once instead.
	public sealed override bool IsTileable
		=> false;

	public override string Icon
		=> Resources.Icons.AdjustmentsHighlightsShadows;

	public override string Name
		=> Translations.GetString ("Highlights / Shadows");

	public override bool IsConfigurable
		=> true;

	public HighlightsShadowsData Data
		=> (HighlightsShadowsData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public HighlightsShadowsEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new HighlightsShadowsData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>
	/// Level shift for each luma value. Shadows is a power curve (it lifts deep shadows the most, relative
	/// to their level) and Highlights is its mirror image. Fitted by eye to the Paint.NET 5 documentation example,
	/// where Shadows 82 behaves like a gamma of about 0.8.
	/// </summary>
	public static int[] CreateShiftTable (int shadows, int highlights)
	{
		double shadowGamma = Math.Pow (2, -0.4 * shadows / 100d);
		double highlightGamma = Math.Pow (2, 0.4 * highlights / 100d);
		int[] table = new int[256];
		for (int i = 0; i < 256; i++) {
			double l = Math.Pow (i / 255d, shadowGamma);
			l = 1 - Math.Pow (1 - l, highlightGamma);
			table[i] = (int) Math.Round (l * 255) - i;
		}
		return table;
	}

	/// <summary>
	/// Shifts every channel of a premultiplied pixel by <paramref name="shift"/> levels of its straight color.
	/// </summary>
	public static ColorBgra Apply (in ColorBgra color, int shift)
	{
		if (color.A == 0)
			return color;
		ColorBgra straight = color.ToStraightAlpha ();
		ColorBgra result = ColorBgra.FromBgraClamped (straight.B + shift, straight.G + shift, straight.R + shift, straight.A);
		return color.A == 255 ? result : result.ToPremultipliedAlpha ();
	}

	/// <summary>
	/// Three box-blur passes per axis (close to a Gaussian of the same radius); windows are cut at the edges of the area.
	/// </summary>
	public static float[] Blur (float[] values, int width, int height, int radius)
	{
		float[] a = (float[]) values.Clone ();
		float[] b = new float[a.Length];
		for (int pass = 0; pass < 3; pass++) {
			BoxPass (a, b, width, height, radius, 1, width);
			BoxPass (b, a, height, width, radius, width, 1);
		}
		return a;
	}

	// Blurs `lines` lines of length `length`; consecutive samples are `step` apart, consecutive lines `lineStep` apart.
	private static void BoxPass (float[] src, float[] dst, int length, int lines, int radius, int step, int lineStep)
	{
		double[] prefix = new double[length + 1];
		for (int line = 0; line < lines; line++) {
			int start = line * lineStep;
			for (int i = 0; i < length; i++)
				prefix[i + 1] = prefix[i] + src[start + i * step];
			for (int i = 0; i < length; i++) {
				int lo = Math.Max (0, i - radius);
				int hi = Math.Min (length - 1, i + radius);
				dst[start + i * step] = (float) ((prefix[hi + 1] - prefix[lo]) / (hi - lo + 1));
			}
		}
	}

	// ponytail: the radius is rounded to whole pixels for the box blur; use a true Gaussian if fractional radii matter.
	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		int[] shifts = CreateShiftTable (Data.Shadows, Data.Highlights);
		double clarity = Data.Clarity / 100d;
		int radius = (int) Math.Round (Data.Radius);

		// Three box passes reach 3 * radius pixels, so read that far around the rendered area.
		RectangleI area = roi.Inflated (3 * radius, 3 * radius).Intersect (source.GetBounds ());
		Size size = source.GetSize ();
		ReadOnlySpan<ColorBgra> sourceData = source.GetReadOnlyPixelData ();
		Span<ColorBgra> destinationData = destination.GetPixelData ();

		float[] luma = new float[area.Width * area.Height];
		for (int y = 0; y < area.Height; y++)
			for (int x = 0; x < area.Width; x++)
				luma[y * area.Width + x] = sourceData[(area.Y + y) * size.Width + area.X + x].ToStraightAlpha ().GetIntensityByte ();
		float[] local = radius > 0 ? Blur (luma, area.Width, area.Height, radius) : luma;

		foreach (var pixel in Tiling.GeneratePixelOffsets (roi, size)) {
			int i = (pixel.coordinates.Y - area.Y) * area.Width + pixel.coordinates.X - area.X;
			// The tone curve follows the neighbourhood brightness, so detail inside dark or bright areas survives;
			// Clarity scales that detail (the difference from the neighbourhood).
			double shift = shifts[(int) (local[i] + 0.5f)] + clarity * (luma[i] - local[i]);
			destinationData[pixel.memoryOffset] = Apply (sourceData[pixel.memoryOffset], (int) Math.Round (shift));
		}
	}

	public sealed class HighlightsShadowsData : EffectData
	{
		// Order and controls follow the Paint.NET 5 dialog; the Radius range and default are a guess from its screenshot.
		[Caption ("Highlights"), MinimumValue (-100), MaximumValue (100)]
		public int Highlights { get; set; } = 0;

		[Caption ("Shadows"), MinimumValue (-100), MaximumValue (100)]
		public int Shadows { get; set; } = 0;

		[Caption ("Clarity"), MinimumValue (-100), MaximumValue (100)]
		public int Clarity { get; set; } = 0;

		[Caption ("Radius"), MinimumValue (0), MaximumValue (32), DigitsValue (2), IncrementValue (0.01)]
		public double Radius { get; set; } = 5;

		[Skip]
		public override bool IsDefault
			=> Highlights == 0 && Shadows == 0 && Clarity == 0;
	}
}
