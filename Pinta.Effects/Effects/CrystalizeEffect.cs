using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET-style Distort > Crystalize: the image is broken into irregular
/// polygon cells (a Voronoi diagram of jittered grid points), each filled
/// with the source colour found at its control point.
/// </summary>
public sealed class CrystalizeEffect : BaseEffect
{
	public override string Icon
		=> Resources.Icons.EffectsRenderVoronoiDiagram;

	public sealed override bool IsTileable
		=> true;

	public override string Name
		// Translators: An effect that breaks the image into irregular polygon-shaped cells, like crystals
		=> Translations.GetString ("Crystalize");

	public override bool IsConfigurable
		=> true;

	public override string EffectMenuCategory
		=> Translations.GetString ("Distort");

	public CrystalizeData Data
		=> (CrystalizeData) EffectData!; // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public CrystalizeEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new CrystalizeData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		int cellSize = Data.CellSize;
		int seed = Data.Seed.Value;
		ImmutableArray<PointD> offsets = Sampling.CreateSamplingOffsets (Data.Quality);

		int width = source.Width;
		int height = source.Height;
		ReadOnlySpan<ColorBgra> src = source.GetReadOnlyPixelData ();
		Span<ColorBgra> dst = destination.GetPixelData ();
		Span<ColorBgra> samples = stackalloc ColorBgra[offsets.Length];

		for (int y = roi.Top; y <= roi.Bottom; ++y) {
			for (int x = roi.Left; x <= roi.Right; ++x) {
				for (int i = 0; i < offsets.Length; ++i) {
					PointD site = NearestSite (x + offsets[i].X, y + offsets[i].Y, cellSize, seed);
					int sx = Math.Clamp ((int) site.X, 0, width - 1);
					int sy = Math.Clamp ((int) site.Y, 0, height - 1);
					samples[i] = src[sy * width + sx];
				}
				dst[y * width + x] = ColorBgra.Blend (samples, ColorBgra.Transparent);
			}
		}
	}

	/// <summary>
	/// Returns the control point nearest to (x, y). There is one control point per
	/// grid cell, placed at a pseudo-random position inside it, so the result only
	/// depends on the location and seed, which keeps tiles seamless.
	/// </summary>
	internal static PointD NearestSite (double x, double y, int cellSize, int seed)
	{
		int cx = (int) Math.Floor (x / cellSize);
		int cy = (int) Math.Floor (y / cellSize);

		PointD best = default;
		double bestDistance = double.MaxValue;

		// With a fully jittered point per cell, the nearest one can be two cells away.
		for (int j = cy - 2; j <= cy + 2; ++j) {
			for (int i = cx - 2; i <= cx + 2; ++i) {
				PointD site = Site (i, j, cellSize, seed);
				double dx = site.X - x;
				double dy = site.Y - y;
				double d = dx * dx + dy * dy;
				if (d >= bestDistance) continue;
				bestDistance = d;
				best = site;
			}
		}

		return best;
	}

	/// <summary>The control point of grid cell (i, j), somewhere inside that cell.</summary>
	internal static PointD Site (int i, int j, int cellSize, int seed)
	{
		uint h = Hash (i, j, seed);
		return new (
			X: (i + (h & 0xFFFF) / 65536.0) * cellSize,
			Y: (j + (h >> 16) / 65536.0) * cellSize);
	}

	private static uint Hash (int x, int y, int seed)
	{
		uint h = unchecked((uint) x * 0x8DA6B343u ^ (uint) y * 0xD8163841u ^ (uint) seed * 0xCB1AB31Fu);
		h ^= h >> 16;
		h *= 0x7FEB352Du;
		h ^= h >> 15;
		h *= 0x846CA68Bu;
		h ^= h >> 16;
		return h;
	}

	public sealed class CrystalizeData : EffectData
	{
		[Caption ("Cell Size")]
		[MinimumValue (2), MaximumValue (250)]
		public int CellSize { get; set; } = 8;

		[Caption ("Quality")]
		[MinimumValue (1), MaximumValue (5)]
		public int Quality { get; set; } = 2;

		[Caption ("Random Noise Seed")]
		public RandomSeed Seed { get; set; } = new (0);
	}
}
