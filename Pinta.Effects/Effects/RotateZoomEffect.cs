/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Roll / rotate / zoom mapping ported from Paint.NET 3.36's RotateZoomEffect. //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.Effects;

/// <summary>
/// Layers > Rotate / Zoom, as in Paint.NET: a 3D roll of the layer, a rotation, a pan and a zoom.
/// Not listed in the Effects menu; the Layers menu action runs it through the live preview.
/// </summary>
public sealed class RotateZoomEffect : BaseEffect
{
	public override string Icon => Resources.Icons.LayerRotateZoom;
	public override string Name => Translations.GetString ("Rotate / Zoom");
	public override bool IsTileable => true;
	public override bool IsConfigurable => true;

	public RotateZoomData Data => (RotateZoomData) EffectData!; // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;

	public RotateZoomEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new RotateZoomData ();
	}

	public override async Task<bool> LaunchConfiguration ()
	{
		using RotateZoomDialog dialog = RotateZoomDialog.New (chrome.MainWindow, Name, Icon, Data, workspace);
		Gtk.ResponseType response = await dialog.RunAsync ();
		dialog.Destroy ();
		return response == Gtk.ResponseType.Ok;
	}

	protected override void Render (ImageSurface src, ImageSurface dst, RectangleI roi)
	{
		RotateZoomData data = Data;
		int width = dst.Width;
		int height = dst.Height;
		ReadOnlySpan<ColorBgra> srcData = src.GetReadOnlyPixelData ();
		Span<ColorBgra> dstData = dst.GetPixelData ();

		// Supersampling would soften an untouched layer, so leave it as it is.
		if (data.IsDefault) {
			for (int y = roi.Top; y <= roi.Bottom; ++y)
				srcData.Slice (y * width + roi.Left, roi.Width).CopyTo (dstData.Slice (y * width + roi.Left, roi.Width));
			return;
		}

		Mapping map = new (data, width, height);
		int quality = Math.Clamp (data.Quality, 1, RotateZoomData.MaxQuality);
		Span<ColorBgra> samples = stackalloc ColorBgra[quality * quality];

		for (int y = roi.Top; y <= roi.Bottom; ++y) {
			for (int x = roi.Left; x <= roi.Right; ++x) {
				int n = 0;
				for (int sy = 0; sy < quality; ++sy) {
					double oy = (sy + 0.5) / quality - 0.5;
					for (int sx = 0; sx < quality; ++sx) {
						double ox = (sx + 0.5) / quality - 0.5;
						samples[n++] =
							map.ToSource (x + ox, y + oy, out double u, out double v)
							? Sample (srcData, width, height, u, v, data.TilingMode, data.Sampling)
							: ColorBgra.Transparent;
					}
				}
				dstData[y * width + x] = n == 1 ? samples[0] : ColorBgra.Blend (samples);
			}
		}
	}

	/// <summary>
	/// The inverse mapping from a destination pixel to the source point it shows, from Paint.NET 3.36:
	/// the image is pre-rotated to the roll direction, tilted about that axis and seen in perspective,
	/// then rotated back and by the angle.
	/// </summary>
	private readonly struct Mapping
	{
		private readonly double dxdx, dydx, dzdx, dxdy, dydy, dzdy;
		private readonly double center_x, center_y, pan_x, pan_y, div_z, zoom;

		public Mapping (RotateZoomData data, int width, int height)
		{
			double direction = data.RollDirection * Math.PI / 180;
			double pre = direction;
			double post = -direction - data.Angle * Math.PI / 180;
			// At 90 the image is edge-on and the mapping divides by zero.
			double tilt = Math.Asin (Math.Clamp (data.RollAmount, 0, 89.99) / 90);

			(dxdx, dydx, dzdx) = Transform (1, 0, pre, post, tilt);
			(dxdy, dydy, dzdy) = Transform (0, 1, pre, post, tilt);

			center_x = width / 2.0;
			center_y = height / 2.0;
			pan_x = data.Pan.X * width / 2.0;
			pan_y = data.Pan.Y * height / 2.0;
			div_z = 0.5 * Math.Sqrt (width * (double) width + height * (double) height);
			zoom = data.Zoom;
		}

		private static (double X, double Y, double Z) Transform (double x, double y, double pre, double post, double tilt)
		{
			double r = -tilt;
			double cr = Math.Cos (r), sr = Math.Sin (r);
			double cb = Math.Cos (pre), sb = Math.Sin (pre);
			double ca = Math.Cos (post), sa = Math.Sin (post);

			double sx = (x * cb + y * sb) / cr;
			double sy = -x * sb + y * cb;
			double sz = sx * sr;
			sx /= cr;

			return (sx * ca + sy * sa, -sx * sa + sy * ca, sz);
		}

		public bool ToSource (double x, double y, out double u, out double v)
		{
			// Pixel centres sit at +0.5, so a half turn maps pixel x onto width - 1 - x exactly.
			double mx = x + 0.5 - pan_x - center_x;
			double my = y + 0.5 - pan_y - center_y;
			double rx = dxdx * mx + dxdy * my;
			double ry = dydx * mx + dydy * my;
			double rz = dzdx * mx + dzdy * my;

			u = v = 0;
			if (zoom <= 0 || rz <= -div_z)
				return false; // Behind the viewer.

			double div = div_z / (zoom * (div_z + rz));
			u = rx * div + center_x - 0.5;
			v = ry * div + center_y - 0.5;
			return true;
		}
	}

	private static ColorBgra Sample (
		ReadOnlySpan<ColorBgra> src,
		int width,
		int height,
		double u,
		double v,
		RotateZoomTiling tiling,
		RotateZoomSampling sampling)
	{
		if (!double.IsFinite (u) || !double.IsFinite (v) || Math.Abs (u) > 1e8 || Math.Abs (v) > 1e8)
			return ColorBgra.Transparent;

		if (sampling == RotateZoomSampling.NearestNeighbor)
			return Pixel (src, width, height, (int) Math.Floor (u + 0.5), (int) Math.Floor (v + 0.5), tiling);

		// Bilinear, with pixels outside the layer transparent when not tiling, so the edges fade.
		if (tiling == RotateZoomTiling.None && (u < -1 || v < -1 || u > width || v > height))
			return ColorBgra.Transparent;

		int iu = (int) Math.Floor (u);
		int iv = (int) Math.Floor (v);
		uint fx = (uint) (256 * (u - iu));
		uint fy = (uint) (256 * (v - iv));
		uint ifx = 256 - fx;
		uint ify = 256 - fy;

		return ColorBgra.BlendColors4W16IP (
			Pixel (src, width, height, iu, iv, tiling), ifx * ify,
			Pixel (src, width, height, iu + 1, iv, tiling), fx * ify,
			Pixel (src, width, height, iu, iv + 1, tiling), ifx * fy,
			Pixel (src, width, height, iu + 1, iv + 1, tiling), fx * fy);
	}

	private static ColorBgra Pixel (ReadOnlySpan<ColorBgra> src, int width, int height, int x, int y, RotateZoomTiling tiling)
	{
		switch (tiling) {
			case RotateZoomTiling.Repeat:
				x = Wrap (x, width);
				y = Wrap (y, height);
				break;
			case RotateZoomTiling.Mirror:
				x = Mirror (x, width);
				y = Mirror (y, height);
				break;
			default:
				if (x < 0 || y < 0 || x >= width || y >= height)
					return ColorBgra.Transparent;
				break;
		}
		return src[y * width + x];
	}

	private static int Wrap (int i, int n)
		=> ((i % n) + n) % n;

	private static int Mirror (int i, int n)
	{
		int m = Wrap (i, 2 * n);
		return m < n ? m : 2 * n - 1 - m;
	}
}

public enum RotateZoomTiling
{
	[Caption ("None")] None,
	[Caption ("Repeat")] Repeat,
	[Caption ("Mirror")] Mirror,
}

public enum RotateZoomSampling
{
	[Caption ("Bilinear")] Bilinear,
	[Caption ("Nearest Neighbor")] NearestNeighbor,
}

public sealed class RotateZoomData : EffectData
{
	public const int MaxQuality = 8;
	public const double MaxZoom = 16;
	public const double MaxPan = 10;

	/// <summary>Rotation about the Z axis, counter-clockwise, in degrees.</summary>
	public double Angle { get; set; } = 0;

	/// <summary>The compass direction the image tips towards: 0 = East, 90 = South.</summary>
	public double RollDirection { get; set; } = 0;

	/// <summary>The tilt, 0 (face-on) to 90 (edge-on).</summary>
	public double RollAmount { get; set; } = 0;

	/// <summary>Offset of the image centre, in half image sizes.</summary>
	public PointD Pan { get; set; } = new (0, 0);

	public double Zoom { get; set; } = 1;

	public int Quality { get; set; } = 2;

	public RotateZoomTiling TilingMode { get; set; } = RotateZoomTiling.None;

	public RotateZoomSampling Sampling { get; set; } = RotateZoomSampling.Bilinear;

	public override bool IsDefault =>
		Angle == 0 && RollAmount == 0 && Pan == new PointD (0, 0) && Zoom == 1;
}
