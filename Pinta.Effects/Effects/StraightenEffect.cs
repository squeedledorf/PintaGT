using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET's Effects > Photo > Straighten: rotates the image about its
/// centre and scales it up just enough to keep the canvas filled.
/// </summary>
public sealed class StraightenEffect : BaseEffect
{
	public override string Icon => Resources.Icons.LayerRotateZoom;

	// Drawn with Cairo in one pass rather than per row
	public sealed override bool IsTileable => false;

	public override string Name => Translations.GetString ("Straighten");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Photo");

	public StraightenData Data => (StraightenData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public StraightenEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new StraightenData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>
	/// The zoom needed so a <paramref name="width"/> x <paramref name="height"/>
	/// image rotated by <paramref name="degrees"/> still covers the whole canvas.
	/// </summary>
	public static double CoverScale (int width, int height, double degrees)
	{
		double radians = degrees * Math.PI / 180;
		double c = Math.Abs (Math.Cos (radians));
		double s = Math.Abs (Math.Sin (radians));
		return Math.Max (
			(width * c + height * s) / width,
			(width * s + height * c) / height);
	}

	public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
	{
		StraightenData data = Data;
		double degrees = Math.Clamp (data.Angle, -45, 45);
		double scale = CoverScale (src.Width, src.Height, degrees);

		using Context g = new (dst);
		foreach (RectangleI roi in rois)
			g.Rectangle (roi.X, roi.Y, roi.Width, roi.Height);
		g.Clip ();

		// Positive angles turn the image anticlockwise, like the angle picker.
		g.Translate (src.Width / 2.0, src.Height / 2.0);
		g.Rotate (-degrees * Math.PI / 180);
		g.Scale (scale, scale);
		g.Translate (-src.Width / 2.0, -src.Height / 2.0);

		using SurfacePattern pattern = new (src) {
			Filter = data.Sampling switch {
				StraightenSampling.NearestNeighbor => Filter.Nearest,
				StraightenSampling.Bilinear => Filter.Bilinear,
				_ => Filter.Best,
			},
			// Keep the edge pixels solid instead of fading to transparent
			Extend = Extend.Pad,
		};
		g.SetSource (pattern);
		g.Operator = Operator.Source;
		g.Paint ();
	}

	public enum StraightenSampling
	{
		[Caption ("Bicubic")]
		Bicubic,

		[Caption ("Nearest Neighbor")]
		NearestNeighbor,

		[Caption ("Bilinear")]
		Bilinear,
	}

	public sealed class StraightenData : EffectData
	{
		[Skip]
		public override bool IsDefault => Angle == 0;

		[Caption ("Angle")]
		[MinimumValue (-45), MaximumValue (45), DigitsValue (2), IncrementValue (0.01)]
		public double Angle { get; set; } = 0;

		[Caption ("Sampling")]
		public StraightenSampling Sampling { get; set; } = StraightenSampling.Bicubic;
	}
}
