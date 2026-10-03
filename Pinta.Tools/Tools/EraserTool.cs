//
// EraserTool.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class EraserTool : BaseBrushTool
{
	private enum EraserType
	{
		Normal = 0,
		Smooth = 1,
	}

	private PointI? last_point = null;
	private EraserType eraser_type = EraserType.Normal;

	// Coverage of the current stroke. The layer is recomputed from the undo surface
	// and this mask, so overlapping segments never erase a pixel more than once.
	private ImageSurface? stroke_mask;

	private const int LUT_Resolution = 256;
	private readonly Lazy<byte[,]> lazy_lut_factor = new (CreateLookupTable);
	private readonly IWorkspaceService workspace;

	public EraserTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};
	}

	public override string Name
		=> Translations.GetString ("Eraser");

	public override string Icon
		=> Pinta.Resources.Icons.ToolEraser;

	public override string StatusBarText
		=> Translations.GetString ("Left click to erase using the primary color's transparency, right click to erase using the secondary color's transparency.");

	public override Gdk.Key ShortcutKey
		=> new (Gdk.Constants.KEY_E);

	public override int Priority => 23;

	public override Gdk.Cursor DefaultCursor {
		get {
			double scale = workspace.GetScale ();
			var icon = GdkExtensions.CreateIconWithShape (
				"Cursor.Eraser.png",
				CursorShape.Ellipse,
				scale,
				BrushWidthCeiling,
				8,
				22,
				out int iconOffsetX,
				out int iconOffsetY);

			return Gdk.Cursor.NewFromTexture (icon, iconOffsetX, iconOffsetY, null);
		}
	}

	protected override void OnBuildToolBar (Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (TypeLabel);
		tb.Append (TypeComboBox);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (mouse_button == MouseButton.None) {
			stroke_mask?.Dispose ();
			RectangleI bounds = new (PointI.Zero, document.ImageSize);
			stroke_mask = CairoExtensions.CreateImageSurface (Format.Argb32, bounds.Width, bounds.Height);
		}

		base.OnMouseDown (document, e);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		base.OnMouseUp (document, e);

		stroke_mask?.Dispose ();
		stroke_mask = null;
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		PointI newPoint = e.Point;
		PointD newPointD = e.PointDouble;

		if (mouse_button == MouseButton.None || stroke_mask is null || undo_surface is null) {
			last_point = null;
			return;
		}

		if (!last_point.HasValue)
			last_point = newPoint;

		if (document.Workspace.PointInCanvas (newPointD))
			surface_modified = true;

		PointD lastPointD = (PointD) last_point.Value;

		switch (eraser_type) {

			case EraserType.Normal:
				// End on the integer point like the start, or a 1 px dab lands on a pixel corner and smears into 4.
				MaskNormal (stroke_mask, lastPointD, (PointD) newPoint);
				break;

			case EraserType.Smooth:
				MaskSmooth (stroke_mask, lastPointD, newPointD);
				break;
		}

		int dirtyPadding = BrushWidthCeiling + 2;

		RectangleI dirty =
			RectangleI.FromPoints (
				last_point.Value,
				newPoint)
			.Inflated (
				dirtyPadding,
				dirtyPadding);

		// Left click erases by the primary color's alpha, right click by the secondary's.
		// The eraser never paints color.
		double eraseAlpha = (mouse_button == MouseButton.Right ? Palette.SecondaryColor : Palette.PrimaryColor).A;

		using (Context g = document.CreateClippedContext ()) {
			g.Rectangle (document.ClampToImageSize (dirty).ToDouble ());
			g.Clip ();

			g.Operator = Operator.Source;
			g.SetSourceSurface (undo_surface, 0, 0);
			g.Paint ();

			g.Operator = Operator.DestOut;
			g.SetSourceSurface (stroke_mask, 0, 0);
			g.PaintWithAlpha (eraseAlpha);
		}

		if (document.Workspace.IsPartiallyOffscreen (dirty))
			document.Workspace.Invalidate ();
		else
			document.Workspace.Invalidate (document.ClampToImageSize (dirty));

		last_point = newPoint;
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (type_combobox is not null)
			settings.PutSetting (SettingNames.ERASER_ERASE_TYPE, type_combobox.ComboBox.Active);
	}

	private static byte[,] CreateLookupTable ()
	{
		int arrayDimensions = LUT_Resolution + 1;
		byte[,] result = new byte[arrayDimensions, arrayDimensions];
		for (int dy = 0; dy < arrayDimensions; dy++) {
			for (int dx = 0; dx < arrayDimensions; dx++) {
				double d = Mathematics.Magnitude<double> (dx, dy) / LUT_Resolution;
				result[dy, dx] =
					d > 1.0
					? (byte) 255
					: (byte) (255.0 - Math.Cos (Math.Sqrt (d) * Math.PI / 2.0) * 255.0);
			}
		}
		return result;
	}

	private void MaskNormal (ImageSurface mask, PointD start, PointD end)
	{
		using Context g = new (mask);

		g.Antialias = UseAntialiasing ? Antialias.Subpixel : Antialias.None;

		// Adding 0.5 forces cairo into the correct square:
		// See https://bugs.launchpad.net/bugs/672232
		g.MoveTo (start.X + 0.5, start.Y + 0.5);
		g.LineTo (end.X + 0.5, end.Y + 0.5);

		g.SetSourceColor (new Color (0, 0, 0, 1));
		g.LineWidth = BrushWidth;
		g.LineJoin = LineJoin.Round;
		g.LineCap = LineCap.Round;

		g.Stroke ();
	}

	private void MaskSmooth (ImageSurface mask, PointD start, PointD end)
	{
		int rad = (int) (BrushWidth / 2.0) + 1;

		int numberOfSteps = (int) start.Distance (end) / rad + 1;

		// Initialize lookup table when first used (to prevent slower startup of the application)
		byte[,] lut_factor = lazy_lut_factor.Value;

		mask.Flush ();
		Span<ColorBgra> maskData = mask.GetPixelData ();
		RectangleI surfaceBounds = new (0, 0, mask.Width, mask.Height);

		for (var step = 0; step < numberOfSteps; step++) {

			PointD pt = Utility.Lerp (
				start,
				end,
				(float) step / numberOfSteps);

			int x = (int) pt.X;
			int y = (int) pt.Y;

			RectangleI brushBounds = new (x - rad, y - rad, 2 * rad, 2 * rad);
			RectangleI destinationBounds = RectangleI.Intersect (surfaceBounds, brushBounds);

			if (destinationBounds.Width <= 0 || destinationBounds.Height <= 0)
				continue;

			for (int iy = destinationBounds.Top; iy < destinationBounds.Bottom; iy++) {

				var row = maskData[(mask.Width * iy)..];
				int dy = Math.Abs ((iy - y) * LUT_Resolution / rad);

				for (var ix = destinationBounds.Left; ix < destinationBounds.Right; ix++) {

					int dx = Math.Abs ((ix - x) * LUT_Resolution / rad);

					// The table holds how much of the pixel is kept, so coverage is its inverse.
					byte coverage = (byte) (255 - lut_factor[dy, dx]);

					if (coverage > row[ix].A)
						row[ix] = ColorBgra.FromBgra (0, 0, 0, coverage);
				}
			}
		}

		mask.MarkDirty ();
	}

	private Label? type_label;
	private ToolBarComboBox? type_combobox;

	private Label TypeLabel => type_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Type")));
	private ToolBarComboBox TypeComboBox {
		get {
			if (type_combobox is null) {
				type_combobox = ToolBarComboBox.New (100, 0, false, Translations.GetString ("Normal"), Translations.GetString ("Smooth"));

				type_combobox.ComboBox.OnChanged += (o, e) => {
					eraser_type = (EraserType) type_combobox.ComboBox.Active;
				};

				type_combobox.ComboBox.Active = Settings.GetSetting (SettingNames.ERASER_ERASE_TYPE, 0);
			}

			return type_combobox;
		}
	}
}
