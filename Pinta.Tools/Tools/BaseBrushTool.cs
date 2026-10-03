// 
// BaseBrushTool.cs
//  
// Author:
//       Joseph Hillenbrand <joehillen@gmail.com>
// 
// Copyright (c) 2010 Joseph Hillenbrand
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

// This is a base class for brush type tools (paintbrush, eraser, etc)
public abstract class BaseBrushTool : BaseTool
{
	protected IPaletteService Palette { get; }

	protected ImageSurface? undo_surface;
	protected bool surface_modified;
	protected MouseButton mouse_button;

	protected BaseBrushTool (IServiceProvider services) : base (services)
	{
		Palette = services.GetService<IPaletteService> ();

		BrushWidthSpinButton.TooltipText = Translations.GetString ("Change brush size.") + "\n"
			+ "\n" + Translations.GetString ("Shortcut keys:")
			+ "\n" + Translations.GetString ("Press {0} to decrease brush size", "\"[\"")
			+ "\n" + Translations.GetString ("Press {0} to increase brush size", "\"]\"")
			// Translators: {0} is 'Ctrl', or a platform-specific key such as 'Command' on macOS. {1} is a number.
			+ "\n" + Translations.GetString ("Hold {0} to change it by {1}", services.GetService<SystemManager> ().CtrlLabel (), BrushWidthLargeStep);
		BrushWidthSpinButton.OnValueChanged += (_, _) => OnBrushWidthChanged ();
	}

	protected override bool ShowAntialiasingButton => true;

	protected double BrushWidth {
		get => brush_width?.Value ?? DEFAULT_BRUSH_WIDTH;
		set {
			if (brush_width is not null)
				brush_width.Value = value;
		}
	}

	/// <summary>
	/// The brush width rounded up, for cursors and dirty-rectangle padding.
	/// </summary>
	protected int BrushWidthCeiling => (int) Math.Ceiling (BrushWidth);

	/// <summary>
	/// How far Ctrl+[ and Ctrl+] change the brush width.
	/// </summary>
	internal const int BrushWidthLargeStep = 5;

	/// <summary>
	/// Whether the tool paints Paint.NET style dabs, with Hardness, Spacing and Smoothing options.
	/// </summary>
	protected virtual bool ShowDabOptions => false;

	/// <summary>
	/// Whether the next stroke is painted with dabs (see <see cref="StampDabs"/>).
	/// </summary>
	protected virtual bool PaintsWithDabs => ShowDabOptions;

	/// <summary>
	/// The shape of the dabs.
	/// </summary>
	protected virtual BrushTip Tip => BrushTip.Circle;

	protected override void OnBuildToolBar (Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (BrushWidthLabel);
		tb.Append (BrushWidthBox);

		if (!ShowDabOptions)
			return;

		tb.Append (HardnessLabel);
		tb.Append (HardnessSlider);
		tb.Append (SpacingLabel);
		tb.Append (SpacingSlider);

		OnBuildBrushToolBar (tb);

		tb.Append (DabSeparator);
		tb.Append (SmoothingDropDown);
	}

	/// <summary>
	/// Adds a dab tool's own options, after Spacing and before the Smoothing button.
	/// </summary>
	protected virtual void OnBuildBrushToolBar (Box tb)
	{
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// If we are already drawing, ignore any additional mouse down events
		if (mouse_button != MouseButton.None)
			return;

		surface_modified = false;
		undo_surface = document.Layers.CurrentUserLayer.Surface.Clone ();
		mouse_button = e.MouseButton;

		if (PaintsWithDabs)
			BeginDabStroke (document);

		OnMouseMove (document, e);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (undo_surface != null && surface_modified) {
			document.History.PushNewItem (new SimpleHistoryItem (Icon, Name, undo_surface, document.Layers.CurrentUserLayerIndex));
		}

		surface_modified = false;
		undo_surface = null;
		mouse_button = MouseButton.None;
		EndDabStroke ();
	}

	/// <summary>
	/// Coverage of the current dab stroke: black pixels whose alpha is how much of each pixel the
	/// stroke covers. Tools paint through it, recomputing the touched area from the undo surface,
	/// so a pixel is never painted twice in one stroke.
	/// </summary>
	protected ImageSurface? StrokeMask => stroke_mask;

	private ImageSurface? stroke_mask;
	private BrushDabStroke? dab_stroke;
	// Pointer positions are screen pixel corners; dabs go at the screen pixel's centre,
	// so at 100% a click paints the pixel under the cursor instead of the four around its corner.
	private double half_screen_pixel;

	protected void BeginDabStroke (Document document)
	{
		stroke_mask?.Dispose ();
		stroke_mask = CairoExtensions.CreateImageSurface (Format.Argb32, document.ImageSize.Width, document.ImageSize.Height);
		half_screen_pixel = 0.5 / document.Workspace.Scale;
		dab_stroke = new BrushDabStroke (
			BrushWidth,
			HardnessSlider.GetValue () / 100,
			SpacingSlider.GetValue () / 100,
			Smoothing,
			UseAntialiasing,
			Tip);
	}

	/// <summary>
	/// Stamps the dabs up to the pointer position into <see cref="StrokeMask"/>.
	/// Returns the area that changed (empty when no dab landed on the image).
	/// </summary>
	protected RectangleI StampDabs (PointD position)
		=> dab_stroke is null ? RectangleI.Zero : Stamp (dab_stroke.AddPoint (new PointD (position.X + half_screen_pixel, position.Y + half_screen_pixel)));

	/// <summary>
	/// Stamps the end of the path that smoothing held back. Call before committing the stroke.
	/// </summary>
	protected RectangleI FinishDabStroke ()
		=> dab_stroke is null ? RectangleI.Zero : Stamp (dab_stroke.Finish ());

	protected void EndDabStroke ()
	{
		stroke_mask?.Dispose ();
		stroke_mask = null;
		dab_stroke = null;
	}

	private RectangleI Stamp (List<PointD> dabs)
	{
		if (stroke_mask is null || dab_stroke is null || dabs.Count == 0)
			return RectangleI.Zero;

		stroke_mask.Flush ();
		Span<ColorBgra> data = stroke_mask.GetPixelData ();
		RectangleI? dirty = null;
		foreach (PointD dab in dabs) {
			RectangleI r = dab_stroke.Stamp (data, stroke_mask.Width, stroke_mask.Height, dab);
			if (!r.IsEmpty)
				dirty = dirty?.Union (r) ?? r;
		}
		stroke_mask.MarkDirty ();
		return dirty ?? RectangleI.Zero;
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		switch (e.Key.Value) {
			case Gdk.Constants.KEY_bracketleft:
				BrushWidth -= e.IsControlPressed ? BrushWidthLargeStep : 1;
				return true;
			case Gdk.Constants.KEY_bracketright:
				BrushWidth += e.IsControlPressed ? BrushWidthLargeStep : 1;
				return true;
		}

		return base.OnKeyDown (document, e);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (brush_width is not null)
			settings.PutSetting (SettingNames.BrushWidth (this), brush_width.Value);
		if (hardness_slider is not null)
			settings.PutSetting (DabSettingName ("hardness"), (int) hardness_slider.GetValue ());
		if (spacing_slider is not null)
			settings.PutSetting (DabSettingName ("spacing"), (int) spacing_slider.GetValue ());
		if (smoothing_button is not null)
			settings.PutSetting (DabSettingName ("smoothing"), Smoothing);
	}

	private string DabSettingName (string option)
		=> $"{GetType ().Name.ToLowerInvariant ()}-brush-{option}";

	protected virtual void OnBrushWidthChanged ()
	{
		// Change the cursor when the BrushWidth is changed.
		SetCursor (DefaultCursor);
	}


	private SpinButton? brush_width;
	private Label? brush_width_label;

	protected SpinButton BrushWidthSpinButton {
		get {
			if (brush_width is null) {
				brush_width = GtkExtensions.CreateToolBarSpinButton (1, 1e5, 1, SettingNames.GetBrushWidth (Settings, SettingNames.BrushWidth (this)));
				brush_width.Digits = 2;
				// Paint.NET shows a whole brush size as "2", not "2.00".
				brush_width.OnOutput += (spin, _) => ShowWholeAsInteger (spin);
				ShowWholeAsInteger (brush_width);
			}
			return brush_width;
		}
	}
	internal static bool ShowWholeAsInteger (SpinButton spin)
	{
		double value = spin.Value;
		if (value != Math.Floor (value))
			return false; // Let GTK print the decimals.
		spin.SetText (value.ToString ("0"));
		return true;
	}

	protected Label BrushWidthLabel => brush_width_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Brush size")));

	private Box? brush_width_box;
	private Box BrushWidthBox => brush_width_box ??= BrushWidthSpinButton.WithOuterStepButtons ();

	// Paint.NET's defaults: Hardness 75%, Spacing 15%, smoothed path.
	private const int DEFAULT_HARDNESS = 75;
	private const int DEFAULT_SPACING = 15;
	// Spacing runs to 500%, with the low values given most of the bar.
	private const int MAX_SPACING = 500;
	private const double SPACING_CURVE = 2;

	private Label? hardness_label;
	private Label? spacing_label;
	private ToolBarSlider? hardness_slider;
	private ToolBarSlider? spacing_slider;
	private ToolBarDropDownButton? smoothing_button;
	private Gtk.Separator? dab_separator;

	protected Label HardnessLabel => hardness_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Hardness")));
	protected Label SpacingLabel => spacing_label ??= Label.New (string.Format (" {0}: ", Translations.GetString ("Spacing")));
	private Gtk.Separator DabSeparator => dab_separator ??= GtkExtensions.CreateToolBarSeparator ();

	protected ToolBarSlider HardnessSlider => hardness_slider ??= CreateDabSlider (
		0, 100, Settings.GetSetting (DabSettingName ("hardness"), DEFAULT_HARDNESS), 1,
		Translations.GetString ("Hardness of the brush edge. Ignored when antialiasing is off."));

	protected ToolBarSlider SpacingSlider => spacing_slider ??= CreateDabSlider (
		1, MAX_SPACING, Settings.GetSetting (DabSettingName ("spacing"), DEFAULT_SPACING), SPACING_CURVE,
		Translations.GetString ("Distance between brush stamps, as a percentage of the brush size."));

	private static ToolBarSlider CreateDabSlider (int min, int max, int value, double curve, string tooltip)
	{
		ToolBarSlider slider = GtkExtensions.CreateToolBarSlider (min, max, 1, Math.Clamp (value, min, max), curve);
		slider.TooltipText = tooltip;
		return slider;
	}

	/// <summary>
	/// Greys out Hardness, Spacing and Smoothing, for brushes that do not paint dabs.
	/// </summary>
	protected void SetDabOptionsSensitive (bool sensitive)
		=> HardnessSlider.Sensitive = SpacingSlider.Sensitive = SmoothingDropDown.Sensitive = sensitive;

	protected bool Smoothing => SmoothingDropDown.SelectedItem.GetTagOrDefault (true);

	private ToolBarDropDownButton SmoothingDropDown {
		get {
			if (smoothing_button is null) {
				smoothing_button = ToolBarDropDownButton.New ();
				smoothing_button.AddItem (Translations.GetString ("Unsmoothed path"), Pinta.Resources.Icons.ToolLine, false);
				smoothing_button.AddItem (Translations.GetString ("Smoothed path"), Pinta.Resources.Icons.ToolFreeformShape, true);
				smoothing_button.SelectedIndex = Settings.GetSetting (DabSettingName ("smoothing"), true) ? 1 : 0;
			}
			return smoothing_button;
		}
	}
}
