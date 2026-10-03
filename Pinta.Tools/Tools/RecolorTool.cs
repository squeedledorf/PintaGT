//
// RecolorTool.cs
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

// Some methods from Paint.Net:

/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
/////////////////////////////////////////////////////////////////////////////////

using System;
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public class RecolorTool : BaseBrushTool
{
	private readonly IWorkspaceService workspace;

	private enum SamplingMode
	{
		Once = 0,
		SecondaryColor = 1,
	}

	private BitMask? stencil;
	private ColorBgra? sampled_color;

	public RecolorTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
	}

	public override string Name => Translations.GetString ("Recolor");
	public override string Icon => Pinta.Resources.Icons.ToolRecolor;
	public override string StatusBarText => Translations.GetString (
		"Sampling Once: left click replaces the color under the cursor with the primary color, right click with the secondary color." +
		"\nSampling Secondary Color: left click replaces the secondary color with the primary color, right click reverses.");
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.Recolor.png"), 9, 18, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_R);
	protected float Tolerance => (float) (ToleranceSlider.GetValue () / 100);
	private SamplingMode Sampling => SamplingDropDown.SelectedItem.GetTagOrDefault (SamplingMode.Once);
	public override int Priority => 31;

	protected override bool ShowDabOptions => true;

	protected override void OnBuildBrushToolBar (Box tb)
	{
		// Paint.NET's order: Tolerance, then the sampling mode.
		tb.Append (ToleranceLabel);
		tb.Append (ToleranceSlider);

		tb.Append (SamplingLabel);
		tb.Append (SamplingDropDown);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		document.Layers.ToolLayer.Clear ();
		stencil = new BitMask (document.ImageSize.Width, document.ImageSize.Height);

		// In "Sampling Once" mode the color to replace is the one under the cursor when the stroke starts.
		sampled_color = document.Workspace.PointInCanvas (e.PointDouble)
			? document.Layers.CurrentUserLayer.Surface.GetColorBgra (e.Point)
			: null;

		base.OnMouseDown (document, e);
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		// This should have been created in OnMouseDown
		if (stencil is null)
			return;

		if (mouse_button is not (MouseButton.Left or MouseButton.Right))
			return;

		Recolor (document, StampDabs (e.PointDouble));
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (stencil is not null && mouse_button is (MouseButton.Left or MouseButton.Right))
			Recolor (document, FinishDabStroke ());

		stencil = null;
		base.OnMouseUp (document, e);
	}

	/// <summary>
	/// Recomputes the layer inside <paramref name="dirty"/>: the original pixels, with the
	/// recolored ones painted over them through the stroke mask.
	/// </summary>
	private void Recolor (Document document, RectangleI dirty)
	{
		if (dirty.IsEmpty || stencil is null || StrokeMask is null || undo_surface is null)
			return;

		ColorBgra primary = Palette.PrimaryColor.ToColorBgra ();
		ColorBgra secondary = Palette.SecondaryColor.ToColorBgra ();
		bool left = mouse_button == MouseButton.Left;

		// match: the color being replaced. replacement: the color it becomes.
		ColorBgra match;
		ColorBgra replacement = left ? primary : secondary;

		if (Sampling == SamplingMode.SecondaryColor)
			match = left ? secondary : primary;
		else if (sampled_color.HasValue)
			match = sampled_color.Value;
		else
			return;

		surface_modified = true;

		var tmp_layer = document.Layers.ToolLayer.Surface;
		var myTolerance = (int) (Tolerance * 256);

		tmp_layer.Flush ();

		var tmp_data = tmp_layer.GetPixelData ();
		var tmp_width = tmp_layer.Width;
		var orig_data = undo_surface.GetReadOnlyPixelData ();
		var orig_width = undo_surface.Width;

		// The stencil lets us know if we've already checked this
		// pixel, providing a nice perf boost
		for (var i = dirty.Left; i <= dirty.Right; i++)
			for (var j = dirty.Top; j <= dirty.Bottom; j++) {
				if (stencil[i, j])
					continue;

				ColorBgra orig_color = orig_data[j * orig_width + i];
				if (ColorBgra.ColorsWithinTolerance (match, orig_color, myTolerance))
					tmp_data[j * tmp_width + i] = AdjustColorDifference (match, replacement, orig_color);

				stencil[i, j] = true;
			}

		tmp_layer.MarkDirty ();

		using (Context g = document.CreateClippedContext ()) {
			g.Rectangle (dirty.ToDouble ());
			g.Clip ();

			g.Operator = Operator.Source;
			g.SetSourceSurface (undo_surface, 0, 0);
			g.Paint ();

			g.Operator = Operator.Over;
			g.SetSourceSurface (tmp_layer, 0, 0);
			g.MaskSurface (StrokeMask, 0, 0);
		}

		document.Workspace.Invalidate (dirty);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (tolerance_slider is not null)
			settings.PutSetting (SettingNames.RECOLOR_TOLERANCE, (int) tolerance_slider.GetValue ());

		if (sampling_button is not null)
			settings.PutSetting (SettingNames.RECOLOR_SAMPLING, sampling_button.SelectedIndex);
	}

	#region Private PDN Methods
	private static ColorBgra AdjustColorDifference (ColorBgra oldColor, ColorBgra newColor, ColorBgra basisColor)
	{
		return ColorBgra.FromBgra (
			b: AdjustColorByte (oldColor.B, newColor.B, basisColor.B),
			g: AdjustColorByte (oldColor.G, newColor.G, basisColor.G),
			r: AdjustColorByte (oldColor.R, newColor.R, basisColor.R),
			a: basisColor.A
		);
	}

	private static byte AdjustColorByte (byte oldByte, byte newByte, byte basisByte)
	{
		if (oldByte > newByte)
			return Utility.ClampToByte (basisByte - (oldByte - newByte));
		else
			return Utility.ClampToByte (basisByte + (newByte - oldByte));
	}
	#endregion

	private Label? tolerance_label;
	private ToolBarSlider? tolerance_slider;

	private Label ToleranceLabel => tolerance_label ??= Label.New (string.Format ("  {0}: ", Translations.GetString ("Tolerance")));
	private ToolBarSlider ToleranceSlider => tolerance_slider ??= GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.RECOLOR_TOLERANCE, 50));

	private Label? sampling_label;
	private ToolBarDropDownButton? sampling_button;

	private Label SamplingLabel => sampling_label ??= Label.New (string.Format ("  {0}: ", Translations.GetString ("Sampling")));
	private ToolBarDropDownButton SamplingDropDown {
		get {
			if (sampling_button is null) {
				sampling_button = ToolBarDropDownButton.New ();

				sampling_button.AddItem (Translations.GetString ("Sampling Once"), Pinta.Resources.Icons.ToolColorPicker, SamplingMode.Once);
				sampling_button.AddItem (Translations.GetString ("Sampling Secondary Color"), Pinta.Resources.Icons.ColorModeColor, SamplingMode.SecondaryColor);

				sampling_button.SelectedIndex = Settings.GetSetting (SettingNames.RECOLOR_SAMPLING, 0);
			}

			return sampling_button;
		}
	}
}
