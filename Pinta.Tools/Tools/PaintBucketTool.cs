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
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class PaintBucketTool : FloodTool
{
	private readonly IPaletteService palette;
	private Color fill_color;

	public PaintBucketTool (IServiceProvider services) : base (services)
	{
		palette = services.GetService<IPaletteService> ();
	}

	public override string Name => Translations.GetString ("Paint Bucket");
	public override string Icon => Pinta.Resources.Icons.ToolPaintBucket;
	public override string StatusBarText => Translations.GetString (
		"Left click to fill a region with the primary color, right click to fill with the secondary color." +
		"\nHold Shift to switch between Contiguous and Global fill."
	);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.PaintBucket.png"), 21, 21, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_F);
	public override int Priority => 17;
	protected override bool CalculatePolygonSet => false;
	protected override bool ShowBlendModeButton => true;
	protected override bool ShowSelectionQualityButton => true;
	// ponytail: the fill commits at once, so Finish stays greyed until the live, re-editable fill exists.
	protected override bool ShowFinishButton => true;

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		fill_color = e.MouseButton switch {
			MouseButton.Left => palette.PrimaryColor,
			_ => palette.SecondaryColor,
		};

		base.OnMouseDown (document, e);
	}

	protected override void OnFillRegionComputed (Document document, BitMask stencil)
	{
		document.Layers.ToolLayer.Clear ();
		var surf = document.Layers.ToolLayer.Surface;

		var hist = new SimpleHistoryItem (Icon, Name);
		hist.TakeSnapshotOfLayer (document.Layers.CurrentUserLayer);

		// Overwrite fills the stencil with an opaque mask, through which the color replaces the pixels.
		bool overwrite = !UseAlphaBlending;
		var color = overwrite ? ColorBgra.Black : fill_color.ToColorBgra ();
		var width = surf.Width;
		surf.Flush ();

		// Color in any pixel that the stencil says we need to fill
		Parallel.For (0, stencil.Height, y => {
			var stencil_width = stencil.Width;
			var dst_data = surf.GetPixelData ();

			for (var x = 0; x < stencil_width; ++x) {
				if (stencil.Get (x, y))
					dst_data[y * width + x] = color;
			}
		});

		surf.MarkDirty ();

		// Composite the fill onto the real layer with the tool's blend mode, respecting any selection area,
		// so a translucent color blends with the existing pixels.
		using (Context layer_ctx = document.CreateClippedContext ()) {
			if (overwrite) {
				layer_ctx.Operator = Operator.Source;
				layer_ctx.SetSourceColor (fill_color);
				layer_ctx.MaskSurface (surf, 0, 0);
			} else {
				layer_ctx.BlendSurface (surf, SelectedBlendMode);
			}
		}

		document.Layers.ToolLayer.Clear ();
		document.History.PushNewItem (hist);
		document.Workspace.Invalidate ();
	}
}
