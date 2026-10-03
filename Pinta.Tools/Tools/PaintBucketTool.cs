//
// PaintBucketTool.cs
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
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class PaintBucketTool : FloodTool
{
	private const string FILL_TYPE_SETTING = "paint-bucket-fill-type";

	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;

	// Whether the live fill uses the primary colour (left click) as its foreground, or the secondary (right click).
	private bool fill_with_primary;
	// The layer as it was before the live fill, which every redo of the fill starts from.
	private ImageSurface? base_surface;
	private GlyphPicker? fill_picker;
	private Gtk.Label? fill_label;

	public PaintBucketTool (IServiceProvider services) : base (services)
	{
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();

		// As in Paint.NET, a new colour recolours the live fill.
		palette.PrimaryColorChanged += (_, _) => Reflood ();
		palette.SecondaryColorChanged += (_, _) => Reflood ();
	}

	public override string Name => Translations.GetString ("Paint Bucket");
	public override string Icon => Pinta.Resources.Icons.ToolPaintBucket;
	public override string StatusBarText => Translations.GetString (
		"Left click to fill a region with the primary color, right click to fill with the secondary color." +
		"\nHold Shift to switch between Contiguous and Global fill." +
		"\nDrag the nub to move the fill; press Enter or click Finish when done."
	);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.PaintBucket.png"), 21, 21, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_F);
	public override int Priority => 17;
	protected override bool CalculatePolygonSet => false;
	protected override bool ShowBlendModeButton => true;
	protected override bool ShowSelectionQualityButton => true;
	protected override bool ShowFinishButton => true;
	protected override IWorkspaceService Workspace => workspace;

	private FillPatterns.Pattern SelectedPattern => FillPatterns.All[FillPicker.SelectedIndex];

	// Paint.NET order: Flood Mode, Fill, then Tolerance and the sampling controls.
	protected override void AppendFloodControls (Gtk.Box tb)
	{
		tb.Append (ModeLabel);
		tb.Append (ModeDropDown);
		tb.Append (FillLabel);
		tb.Append (FillPicker.Button);
		tb.Append (Separator);
		tb.Append (ToleranceLabel);
		tb.Append (ToleranceSlider);
		AppendSamplingControls (tb);
	}

	protected override void OnBlendModeChanged ()
		=> Reflood ();

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (fill_picker is not null)
			settings.PutSetting (FILL_TYPE_SETTING, fill_picker.SelectedIndex);
	}

	protected override BaseHistoryItem BeginLiveFill (Document document, ToolMouseEventArgs e)
	{
		fill_with_primary = e.MouseButton == MouseButton.Left;

		base_surface = document.Layers.CurrentUserLayer.Surface.Clone ();

		var hist = new SimpleHistoryItem (Icon, Name);
		hist.TakeSnapshotOfLayer (document.Layers.CurrentUserLayer);
		return hist;
	}

	protected override void RestoreBeforeFill (Document document)
	{
		if (base_surface is null)
			return;

		using Context g = new (document.Layers.CurrentUserLayer.Surface);
		g.SetSourceSurface (base_surface, 0, 0);
		g.Operator = Operator.Source;
		g.Paint ();
	}

	protected override void EndLiveFill ()
	{
		base_surface?.Dispose ();
		base_surface = null;
	}

	protected override void OnFillRegionComputed (Document document, BitMask stencil)
	{
		document.Layers.ToolLayer.Clear ();
		var surf = document.Layers.ToolLayer.Surface;

		Color foreground = fill_with_primary ? palette.PrimaryColor : palette.SecondaryColor;
		Color background = fill_with_primary ? palette.SecondaryColor : palette.PrimaryColor;
		ColorBgra fg = foreground.ToColorBgra ();
		ColorBgra bg = background.ToColorBgra ();
		FillPatterns.Pattern pattern = SelectedPattern;

		// Overwrite replaces the pixels under an opaque mask of the stencil instead of blending onto them.
		bool overwrite = !UseAlphaBlending;
		using ImageSurface? mask = overwrite ? CairoExtensions.CreateImageSurface (Format.Argb32, surf.Width, surf.Height) : null;

		var width = surf.Width;
		surf.Flush ();

		// Color in any pixel that the stencil says we need to fill
		Parallel.For (0, stencil.Height, y => {
			var stencil_width = stencil.Width;
			var dst_data = surf.GetPixelData ();
			Span<ColorBgra> mask_data = mask is null ? [] : mask.GetPixelData ();

			for (var x = 0; x < stencil_width; ++x) {
				if (!stencil.Get (x, y))
					continue;

				dst_data[y * width + x] = pattern.IsOn (x, y) ? fg : bg;
				if (!mask_data.IsEmpty)
					mask_data[y * width + x] = ColorBgra.Black;
			}
		});

		surf.MarkDirty ();
		mask?.MarkDirty ();

		// Composite the fill onto the real layer with the tool's blend mode, respecting any selection area,
		// so a translucent color blends with the existing pixels.
		using (Context layer_ctx = document.CreateClippedContext ()) {
			if (mask is not null) {
				layer_ctx.Operator = Operator.Source;
				layer_ctx.SetSourceSurface (surf, 0, 0);
				layer_ctx.MaskSurface (mask, 0, 0);
			} else {
				layer_ctx.BlendSurface (surf, SelectedBlendMode);
			}
		}

		document.Layers.ToolLayer.Clear ();
		document.Workspace.Invalidate ();
	}

	private Gtk.Label FillLabel => fill_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Fill")));

	private GlyphPicker FillPicker {
		get {
			if (fill_picker is null) {
				fill_picker = new GlyphPicker (
					FillPatterns.All.Select (p => new GlyphPicker.Item (p.Name, CreatePatternGlyph (p))).ToArray (),
					columns: 1, showNameOnButton: true, showNamesInList: true);
				fill_picker.SelectedIndex = Settings.GetSetting (FILL_TYPE_SETTING, 0);
				fill_picker.Changed += (_, _) => Reflood ();
			}

			return fill_picker;
		}
	}

	/// <summary>A 16×16 swatch of the pattern in black on white.</summary>
	private static Gdk.Texture CreatePatternGlyph (FillPatterns.Pattern pattern)
	{
		const int size = 16;
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, size, size);
		Span<ColorBgra> data = surface.GetPixelData ();
		for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
				data[y * size + x] = pattern.IsOn (x, y) ? ColorBgra.Black : ColorBgra.White;
		surface.MarkDirty ();
		return surface.ToTexture ();
	}
}
