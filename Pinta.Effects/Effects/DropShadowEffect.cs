using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET's Effects > Object > Drop Shadow: a blurred, offset copy of the
/// object's alpha in a chosen colour, drawn underneath the object.
/// </summary>
public sealed class DropShadowEffect : BaseEffect
{
	public override string Icon => Resources.Icons.EffectsDefault;

	// The blur reads pixels far outside any single row
	public sealed override bool IsTileable => false;

	public override string Name => Translations.GetString ("Drop Shadow");

	public override bool IsConfigurable => true;

	public override string EffectMenuCategory => Translations.GetString ("Object");

	public DropShadowData Data => (DropShadowData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public DropShadowEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new DropShadowData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	public override void Render (ImageSurface src, ImageSurface dst, ReadOnlySpan<RectangleI> rois)
	{
		DropShadowData data = Data;
		int width = src.Width;
		int height = src.Height;
		ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dstData = dst.GetPixelData ();

		// Shadow falls in the picker's direction: 0° is right, 90° is up.
		double radians = data.Angle.ToRadians ().Radians;
		int offsetX = (int) Math.Round (data.Distance * Math.Cos (radians));
		int offsetY = (int) Math.Round (-data.Distance * Math.Sin (radians));

		float[] alpha = new float[width * height];
		for (int y = 0; y < height; y++) {
			int sy = y - offsetY;
			if (sy < 0 || sy >= height)
				continue;
			for (int x = 0; x < width; x++) {
				int sx = x - offsetX;
				if (sx >= 0 && sx < width)
					alpha[y * width + x] = srcData[sy * width + sx].A / 255f;
			}
		}

		BlurAlpha (alpha, width, height, data.ShadowRadius);

		ColorBgra color = data.Color.ToColorBgra (); // premultiplied
		float strength = (float) data.Opacity;

		foreach (RectangleI roi in rois) {
			for (int y = roi.Top; y <= roi.Bottom; y++) {
				for (int x = roi.Left; x <= roi.Right; x++) {
					int i = y * width + x;
					float a = Math.Clamp (alpha[i] * strength, 0f, 1f);
					ColorBgra shadow = ColorBgra.FromBgra (
						(byte) (color.B * a + 0.5f),
						(byte) (color.G * a + 0.5f),
						(byte) (color.R * a + 0.5f),
						(byte) (color.A * a + 0.5f));
					dstData[i] = data.OnlyDrawShadow
						? shadow
						: UserBlendOps.NormalBlendOp.ApplyStatic (shadow, srcData[i]);
				}
			}
		}
	}

	/// <summary>
	/// Blurs an alpha map in place with three box passes (close to a Gaussian),
	/// so the shadow fades out over roughly <paramref name="radius"/> pixels.
	/// Pixels outside the image count as transparent.
	/// </summary>
	public static void BlurAlpha (float[] alpha, int width, int height, double radius)
	{
		int box = (int) Math.Round (radius / 3);
		if (box <= 0)
			return;

		float[] line = new float[Math.Max (width, height)];
		for (int pass = 0; pass < 3; pass++) {
			for (int y = 0; y < height; y++)
				BoxBlurLine (alpha, y * width, 1, width, box, line);
			for (int x = 0; x < width; x++)
				BoxBlurLine (alpha, x, width, height, box, line);
		}
	}

	private static void BoxBlurLine (float[] data, int start, int stride, int count, int box, float[] line)
	{
		for (int i = 0; i < count; i++)
			line[i] = data[start + i * stride];

		float scale = 1f / (2 * box + 1);
		float sum = 0;
		for (int i = 0; i <= box && i < count; i++)
			sum += line[i];

		for (int i = 0; i < count; i++) {
			data[start + i * stride] = sum * scale;
			int add = i + box + 1;
			int remove = i - box;
			if (add < count)
				sum += line[add];
			if (remove >= 0)
				sum -= line[remove];
		}
	}

	public sealed class DropShadowData : EffectData
	{
		[Caption ("Shadow Radius")]
		[MinimumValue (0), MaximumValue (100), DigitsValue (1), IncrementValue (0.1)]
		public double ShadowRadius { get; set; } = 6;

		[Caption ("Distance")]
		[MinimumValue (0), MaximumValue (100), DigitsValue (1), IncrementValue (0.1)]
		public double Distance { get; set; } = 6;

		[Caption ("Angle")]
		public DegreesAngle Angle { get; set; } = new (315);

		[Caption ("Opacity")]
		[MinimumValue (0), MaximumValue (1), DigitsValue (2), IncrementValue (0.01)]
		public double Opacity { get; set; } = 0.5;

		[Caption ("Color")]
		public Color Color { get; set; } = Color.Black;

		[Caption ("Only Draw Shadow")]
		[Hint ("This effect needs a layer with transparency. Put the object you want to shadow on its own layer.")]
		public bool OnlyDrawShadow { get; set; } = false;
	}
}
