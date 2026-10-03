using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET's Effects > Render > Turbulence: four channels (red, green, blue
/// and alpha) of multi-octave Perlin noise, which reads as a coloured cousin of Clouds.
/// </summary>
public sealed class TurbulenceEffect : BaseEffect
{
	public sealed override bool IsTileable => true;

	public override string Icon => Resources.Icons.EffectsRenderClouds;

	public override string Name => Translations.GetString ("Turbulence");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Render");

	public TurbulenceData Data => (TurbulenceData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public TurbulenceEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new TurbulenceData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>
	/// Per-octave amplitude falloff. Size runs 1..4096 and maps
	/// logarithmically onto smooth (0.3) .. rough (0.7).
	/// </summary>
	public static double Roughness (int size)
		=> 0.3 + 0.4 * Math.Log2 (Math.Clamp (size, 1, 4096)) / 12;

	protected override void Render (ImageSurface src, ImageSurface dst, RectangleI roi)
	{
		TurbulenceData data = Data;
		double roughness = Roughness (data.Size);
		double period = Math.Max (data.Period, 1);
		byte seed = unchecked((byte) data.Seed.Value);
		bool turbulence = data.Noise == TurbulenceNoise.Turbulence;

		ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dstData = dst.GetPixelData ();
		int width = src.Width;

		Span<double> channels = stackalloc double[4];
		for (int y = roi.Top; y <= roi.Bottom; y++) {
			for (int x = roi.Left; x <= roi.Right; x++) {
				for (int c = 0; c < 4; c++)
					channels[c] = Sample (x / period, y / period, data.Octaves, roughness, (byte) (seed + c * 67), turbulence);

				ColorBgra noise = ColorBgra.FromBgraClamped (
					(float) (channels[2] * 255),
					(float) (channels[1] * 255),
					(float) (channels[0] * 255),
					(float) (channels[3] * 255)).ToPremultipliedAlpha ();

				int i = y * width + x;
				dstData[i] = data.BlendMode == TurbulenceBlendMode.Overwrite
					? noise
					: UserBlendOps.NormalBlendOp.ApplyStatic (srcData[i], noise);
			}
		}
	}

	/// <summary>
	/// One channel of noise in 0..1. Turbulence sums |noise| (sharp ridges);
	/// Fractal Sum sums signed noise around mid-grey (brighter, more colourful).
	/// </summary>
	public static double Sample (double x, double y, int octaves, double roughness, byte seed, bool turbulence)
	{
		double sum = 0;
		double amplitude = 1;
		double total = 0;
		double frequency = 1;

		for (int i = 0; i < octaves; i++) {
			// Large offset keeps the lattice coordinates positive
			double px = 65536 + x * frequency;
			double py = 65536 + y * frequency;

			// Turn each octave's lattice (about 27°) so the octaves' grid lines
			// don't coincide; otherwise |noise| shows straight horizontal/vertical streaks.
			(x, y) = (x * 0.89 - y * 0.456, x * 0.456 + y * 0.89);
			int ix = (int) px;
			int iy = (int) py;
			double n = PerlinNoise.Compute (
				unchecked((byte) ix),
				unchecked((byte) iy),
				new PointD (px - ix, py - iy),
				(byte) (seed ^ (i * 29)));

			sum += amplitude * (turbulence ? Math.Abs (n) : n);
			total += amplitude;
			amplitude *= roughness;
			frequency *= 2;
		}

		double v = sum / total;
		// Perlin noise rarely leaves ±0.7, so stretch it to fill the range.
		return turbulence
			? Math.Clamp (v * 2.2, 0, 1)
			: Math.Clamp (0.5 + v * 1.4, 0, 1);
	}

	public enum TurbulenceNoise
	{
		[Caption ("Turbulence")]
		Turbulence,

		[Caption ("Fractal Sum")]
		FractalSum,
	}

	public enum TurbulenceBlendMode
	{
		[Caption ("Over (Normal)")]
		Normal,

		[Caption ("Overwrite")]
		Overwrite,
	}

	public sealed class TurbulenceData : EffectData
	{
		[Caption ("Octaves")]
		[MinimumValue (1), MaximumValue (12)]
		public int Octaves { get; set; } = 4;

		[Caption ("Period")]
		[MinimumValue (1), MaximumValue (400), DigitsValue (2), IncrementValue (0.01)]
		public double Period { get; set; } = 100;

		[Caption ("Size")]
		[MinimumValue (1), MaximumValue (4096)]
		public int Size { get; set; } = 4096;

		[Caption ("Noise")]
		public TurbulenceNoise Noise { get; set; } = TurbulenceNoise.Turbulence;

		[Caption ("Random Noise Seed")]
		public RandomSeed Seed { get; set; } = new (0);

		[Caption ("Blend Mode")]
		public TurbulenceBlendMode BlendMode { get; set; } = TurbulenceBlendMode.Normal;
	}
}
