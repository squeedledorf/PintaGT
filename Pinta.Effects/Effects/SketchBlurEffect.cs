using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// A coarse-brush painting look: every pixel streams a fixed, scattered set of
/// neighbours through a P² quantile estimator and takes its estimate of the percentile.
/// Smoothness sets how many neighbours are streamed.
/// </summary>
public sealed class SketchBlurEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsArtisticPencilSketch;

	public sealed override bool IsTileable => true;

	public override string Name => Translations.GetString ("Sketch Blur");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Blurs");

	public SketchBlurData Data => (SketchBlurData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public SketchBlurEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new SketchBlurData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>A fixed, evenly scattered set of offsets inside the disc, in stream order.</summary>
	public static PointI[] CreateOffsets (double radius, int smoothness)
	{
		int count = 16 * smoothness + 8;
		var offsets = new PointI[count];
		Random random = new (8191); // Fixed seed: the same settings always paint the same strokes.
		for (int i = 0; i < count; ++i) {
			// Uniform over the disc area.
			double r = radius * Math.Sqrt (random.NextDouble ());
			double a = random.NextDouble () * 2 * Math.PI;
			offsets[i] = new ((int) Math.Round (r * Math.Cos (a)), (int) Math.Round (r * Math.Sin (a)));
		}
		return offsets;
	}

	public override void Render (ImageSurface src, ImageSurface dest, ReadOnlySpan<RectangleI> rois)
	{
		if (Data.Radius <= 0)
			return; // Copy src to dest

		PointI[] offsets = CreateOffsets (Data.Radius, Data.Smoothness);
		double p = Data.Percentile / 100.0;
		int width = src.Width;
		int height = src.Height;
		ReadOnlySpan<ColorBgra> src_data = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dst_data = dest.GetPixelData ();

		foreach (var rect in rois) {
			for (int y = rect.Top; y <= rect.Bottom; ++y) {
				for (int x = rect.Left; x <= rect.Right; ++x) {
					P2QuantileEstimator eb = new (p), eg = new (p), er = new (p), ea = new (p);
					foreach (var o in offsets) {
						int sx = Math.Clamp (x + o.X, 0, width - 1);
						int sy = Math.Clamp (y + o.Y, 0, height - 1);
						ColorBgra c = src_data[sy * width + sx].ToStraightAlpha ();
						eb.Add (c.B);
						eg.Add (c.G);
						er.Add (c.R);
						ea.Add (c.A);
					}
					dst_data[y * width + x] = ColorBgra.FromBgra (
						ToByte (eb.Estimate),
						ToByte (eg.Estimate),
						ToByte (er.Estimate),
						ToByte (ea.Estimate)).ToPremultipliedAlpha ();
				}
			}
		}
	}

	private static byte ToByte (double v) => (byte) Math.Clamp (Math.Round (v), 0, 255);

	public sealed class SketchBlurData : EffectData
	{
		[Caption ("Radius")]
		[MinimumValue (0), MaximumValue (200)]
		public double Radius { get; set; } = 16;

		[Caption ("Percentile")]
		[MinimumValue (0), MaximumValue (100)]
		public int Percentile { get; set; } = 50;

		[Caption ("Smoothness")]
		[MinimumValue (1), MaximumValue (20)]
		public int Smoothness { get; set; } = 3;

		[Skip]
		public override bool IsDefault => Radius == 0;
	}
}
