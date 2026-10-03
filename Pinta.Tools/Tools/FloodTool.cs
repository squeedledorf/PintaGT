/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

// Additional code:
//
// FloodTool.cs
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
using System.Collections.Generic;
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public abstract class FloodTool : BaseTool
{
	protected Label? mode_label;
	protected ToolBarDropDownButton? mode_button;
	protected Separator? mode_sep;
	protected Label? tolerance_label;
	protected ToolBarSlider? tolerance_slider;
	private ToolBarDropDownButton? alpha_mode_button;
	private Separator? sampling_sep;
	private Label? sampling_label;
	private ToolBarDropDownButton? sampling_button;

	public FloodTool (IServiceProvider services) : base (services) { }

	protected bool IsGlobalMode => ModeDropDown.SelectedItem.GetTagOrDefault (false);
	protected float Tolerance => (float) (ToleranceSlider.GetValue () / 100);
	protected virtual bool CalculatePolygonSet => true;
	protected bool LimitToSelection { get; set; } = true;

	// Paint.NET's Sampling (Image samples the flattened image) and Tolerance alpha mode, shared by the bucket and the wand.
	private bool SampleImage => SamplingDropDown.SelectedItem.GetTagOrDefault (false);
	private bool StraightAlpha => AlphaModeDropDown.SelectedItem.GetTagOrDefault (false);
	private string SettingPrefix => GetType ().Name.ToLowerInvariant ();

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		AppendFloodControls (tb);
	}

	protected virtual void AppendFloodControls (Gtk.Box tb)
	{
		tb.Append (ModeLabel);
		tb.Append (ModeDropDown);
		tb.Append (Separator);
		tb.Append (ToleranceLabel);
		tb.Append (ToleranceSlider);
		tb.Append (AlphaModeDropDown);
		tb.Append (SamplingSeparator);
		tb.Append (SamplingLabel);
		tb.Append (SamplingDropDown);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		var pos = e.Point;

		// Don't do anything if we're outside the canvas
		if (pos.X < 0 || pos.X >= document.ImageSize.Width)
			return;
		if (pos.Y < 0 || pos.Y >= document.ImageSize.Height)
			return;

		base.OnMouseDown (document, e);

		var currentRegion = CairoExtensions.CreateRegion (document.GetSelectedBounds (true));
		// See if the mouse click is valid
		if (!currentRegion.ContainsPoint (pos.X, pos.Y) && LimitToSelection)
			return;

		using ImageSurface? sample_copy = SampleImage || StraightAlpha ? CreateSampleSurface (document, SampleImage, StraightAlpha) : null;
		ImageSurface surface = sample_copy ?? document.Layers.CurrentUserLayer.Surface;
		var stencilBuffer = new BitMask (surface.Width, surface.Height);
		var tol = (int) (Tolerance * Tolerance * 256);

		RectangleD boundingBox;

		// As in Paint.NET, Shift toggles the flood mode rather than forcing Global.
		if (IsGlobalMode ^ e.IsShiftPressed)
			CairoExtensions.FillStencilByColor (surface, stencilBuffer, surface.GetColorBgra (pos), tol, out boundingBox, currentRegion, LimitToSelection);
		else
			CairoExtensions.FillStencilFromPoint (surface, stencilBuffer, pos, tol, out boundingBox, currentRegion, LimitToSelection);

		OnFillRegionComputed (document, stencilBuffer);

		// If a derived tool is only going to use the stencil,
		// don't waste time building the polygon set
		if (CalculatePolygonSet) {
			var polygonSet = stencilBuffer.CreatePolygonSet (boundingBox, PointI.Zero);
			OnFillRegionComputed (document, polygonSet);
		}
	}

	/// <summary>
	/// A copy of the pixels the flood compares: the current layer or, for Image sampling, the flattened image.
	/// In Straight alpha mode the colour channels are un-premultiplied first, so a translucent pixel
	/// differs from an opaque one of the same colour only by its alpha.
	/// </summary>
	private static ImageSurface CreateSampleSurface (Document document, bool sampleImage, bool straightAlpha)
	{
		ImageSurface surface = sampleImage
			? document.GetFlattenedImage ()
			: document.Layers.CurrentUserLayer.Surface.Clone ();

		if (straightAlpha) {
			surface.Flush ();
			ToStraightAlpha (surface.GetPixelData ());
			surface.MarkDirty ();
		}

		return surface;
	}

	public static void ToStraightAlpha (Span<ColorBgra> pixels)
	{
		for (int i = 0; i < pixels.Length; i++)
			pixels[i] = pixels[i].ToStraightAlpha ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (alpha_mode_button is not null)
			settings.PutSetting (SettingPrefix + "-tolerance-alpha-mode", alpha_mode_button.SelectedIndex);
		if (sampling_button is not null)
			settings.PutSetting (SettingPrefix + "-sampling", sampling_button.SelectedIndex);
		if (mode_button is not null)
			settings.PutSetting (SettingNames.FloodToolFillMode (this), mode_button.SelectedIndex);
		if (tolerance_slider is not null)
			settings.PutSetting (SettingNames.FloodToolFillTolerance (this), (int) tolerance_slider.GetValue ());
	}

	protected virtual void OnFillRegionComputed (Document document, IReadOnlyList<IReadOnlyList<PointI>> polygonSet) { }
	protected virtual void OnFillRegionComputed (Document document, BitMask stencil) { }

	protected Label ModeLabel => mode_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Flood Mode")));
	protected Label ToleranceLabel => tolerance_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Tolerance")));
	protected ToolBarSlider ToleranceSlider => tolerance_slider ??= GtkExtensions.CreateToolBarSlider (0, 100, 1, Settings.GetSetting (SettingNames.FloodToolFillTolerance (this), 50));
	protected Separator Separator => mode_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Separator SamplingSeparator => sampling_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Label SamplingLabel => sampling_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Sampling")));

	private ToolBarDropDownButton AlphaModeDropDown {
		get {
			if (alpha_mode_button is null) {
				alpha_mode_button = ToolBarDropDownButton.New ();
				alpha_mode_button.AddItem (Translations.GetString ("Premultiplied"), Pinta.Resources.Icons.ToleranceAlphaPremultiplied, false);
				alpha_mode_button.AddItem (Translations.GetString ("Straight"), Pinta.Resources.Icons.ToleranceAlphaStraight, true);
				alpha_mode_button.SelectedIndex = Math.Clamp (Settings.GetSetting (SettingPrefix + "-tolerance-alpha-mode", 0), 0, 1);
			}

			return alpha_mode_button;
		}
	}

	private ToolBarDropDownButton SamplingDropDown {
		get {
			if (sampling_button is null) {
				sampling_button = ToolBarDropDownButton.New (showLabel: true);
				sampling_button.AddItem (Translations.GetString ("Image"), Pinta.Resources.Icons.ResizeCanvasBase, true);
				sampling_button.AddItem (Translations.GetString ("Layer"), Pinta.Resources.Icons.LayerMergeDown, false);
				// Layer by default, as in Paint.NET.
				sampling_button.SelectedIndex = Math.Clamp (Settings.GetSetting (SettingPrefix + "-sampling", 1), 0, 1);
			}

			return sampling_button;
		}
	}

	protected ToolBarDropDownButton ModeDropDown {
		get {
			if (mode_button is null) {
				mode_button = ToolBarDropDownButton.New ();

				mode_button.AddItem (Translations.GetString ("Contiguous"), Pinta.Resources.Icons.ToolFreeformShape, false);
				mode_button.AddItem (Translations.GetString ("Global"), Pinta.Resources.Icons.HelpWebsite, true);

				mode_button.SelectedIndex = Settings.GetSetting (SettingNames.FloodToolFillMode (this), 0);
			}

			return mode_button;
		}
	}
}
