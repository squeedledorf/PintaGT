//
// Ruler.cs
//
// Author:
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2020 Cameron White
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
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

public enum MetricType
{
	Pixels,
	Inches,
	Centimeters,
}

/// <summary>
/// Replacement for Gtk.Ruler, which was removed in GTK3.
/// Based on the original GTK2 widget and Inkscape's ruler widget.
/// </summary>
[GObject.Subclass<Gtk.DrawingArea>]
public sealed partial class Ruler
{
	private double position = 0;
	private MetricType metric = MetricType.Pixels;

	private Surface? cached_surface = null;
	private Size? last_known_size = null;

	private NumberRange<double>? selection_bounds = null;
	public NumberRange<double>? SelectionBounds {
		get => selection_bounds;
		set {
			if (selection_bounds == value) return;
			selection_bounds = value;
			QueueDraw ();
		}
	}

	/// <summary>
	/// Whether the ruler is horizontal or vertical.
	/// </summary>
	public Gtk.Orientation Orientation { get; private set; }

	/// <summary>
	/// Metric type used for the ruler.
	/// </summary>
	public MetricType Metric {
		get => metric;
		set {
			if (metric == value) return;
			metric = value;
			QueueFullRedraw ();
		}
	}

	/// <summary>
	/// The position of the mark along the ruler.
	/// </summary>
	public double Position {
		get => position;
		set {
			if (position == value) return;
			position = value;
			QueueFullRedraw ();
		}
	}

	private NumberRange<double> ruler_range;
	public NumberRange<double> RulerRange {
		get => ruler_range;
		set {
			if (ruler_range == value) return;
			ruler_range = value;
			QueueFullRedraw ();
		}
	}

	partial void Initialize ()
	{
		SetDrawFunc ((area, context, width, height) => Draw (context, new Size (width, height)));
	}

	/// <summary>
	/// Paint.NET's slim rulers: 16px of ticks and labels plus a 1px edge line.
	/// </summary>
	public const int THICKNESS = 17;

	// Paint.NET's tiny ruler numbers (about 7px tall digits).
	private const int LABEL_FONT_PIXELS = 9;

	// Tick lengths by depth: the labelled tick spans the ruler, then the half and the small ticks.
	private static readonly ImmutableArray<int> tick_lengths = [THICKNESS, 10, 6, 4, 3];

	private void SetOrientation (Gtk.Orientation orientation)
	{
		Orientation = orientation;
		AddCssClass ("pdn-ruler");

		if (Orientation == Gtk.Orientation.Horizontal)
			HeightRequest = THICKNESS;
		else
			WidthRequest = THICKNESS;
	}

	public static Ruler New (Gtk.Orientation orientation)
	{
		Ruler ruler = NewWithProperties ([]);
		ruler.SetOrientation (orientation);
		return ruler;
	}

	// Invalidates cache _and_ queues redraw. Like a full refresh
	private void QueueFullRedraw ()
	{
		InvalidateCache ();
		QueueDraw ();
	}

	private void InvalidateCache ()
	{
		cached_surface?.Dispose ();
		cached_surface = null;
	}

	private static readonly ImmutableArray<double> pixels_ruler_scale = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];
	private static readonly ImmutableArray<int> pixels_subdivide = [1, 5, 10, 50, 100];

	private static readonly ImmutableArray<double> inches_ruler_scale = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
	private static readonly ImmutableArray<int> inches_subdivide = [1, 2, 4, 8, 16];

	private static readonly ImmutableArray<double> centimeters_ruler_scale = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];
	private static readonly ImmutableArray<int> centimeters_subdivide = [1, 5, 10, 50, 100];

	private readonly record struct RulerDrawSettings (
		ImmutableArray<int> SubDivide,
		NumberRange<double> ScaledRange,
		Pango.FontDescription Font,
		double Increment,
		int DivideIndex,
		double PixelsPerTick,
		double UnitsPerTick,
		NumberRange<int> Ticks,
		double MarkerPosition,
		RectangleD RulerOuterLine,
		Size EffectiveSize,
		Color Color,
		Gtk.Orientation Orientation);

	private RulerDrawSettings CreateSettings (Size preliminarySize)
	{
		GetStyleContext ().GetColor (out Gdk.RGBA color);

		RectangleD rulerOuterLine = Orientation switch {

			Gtk.Orientation.Vertical => new (
				X: preliminarySize.Width - 1,
				Y: 0,
				Width: 1,
				Height: preliminarySize.Height),

			Gtk.Orientation.Horizontal => new (
				X: 0,
				Y: preliminarySize.Height - 1,
				Width: preliminarySize.Width,
				Height: 1),

			_ => throw new UnreachableException (),
		};

		Size effectiveSize = Orientation switch {
			Gtk.Orientation.Vertical => new (preliminarySize.Height, preliminarySize.Width),// Swap so that width is the longer dimension (horizontal).
			Gtk.Orientation.Horizontal => preliminarySize,
			_ => throw new UnreachableException (),
		};

		ImmutableArray<double> rulerScale = Metric switch {
			MetricType.Pixels => pixels_ruler_scale,
			MetricType.Inches => inches_ruler_scale,
			MetricType.Centimeters => centimeters_ruler_scale,
			_ => throw new UnreachableException (),
		};

		ImmutableArray<int> subdivide = Metric switch {
			MetricType.Pixels => pixels_subdivide,
			MetricType.Inches => inches_subdivide,
			MetricType.Centimeters => centimeters_subdivide,
			_ => throw new UnreachableException (),
		};

		// The same conversion as the status bar's readouts.
		double pixels_per_unit = ViewActions.PixelsPerUnit ((int) Metric);

		// Find our scaled range.

		NumberRange<double> scaledRange = new (
			lower: RulerRange.Lower / pixels_per_unit,
			upper: RulerRange.Upper / pixels_per_unit);

		double maxSize = scaledRange.Upper - scaledRange.Lower;

		// There must be enough space between the large ticks for the text labels;
		// like Paint.NET, labelled ticks are never closer than about 60px.
		Pango.FontDescription font = GetPangoContext ().GetFontDescription ()!.Copy ()!;
		font.SetAbsoluteSize (LABEL_FONT_PIXELS * Pango.Constants.SCALE);
		int maxDigits = ((int) -Math.Abs (maxSize)).ToString ().Length;
		int minSeparation = Math.Max (60, maxDigits * LABEL_FONT_PIXELS);

		double increment = effectiveSize.Width / maxSize;

		// Figure out how to display the ticks.
		int scaleIndex;
		for (scaleIndex = 0; scaleIndex < rulerScale.Length - 1; ++scaleIndex) {
			if (rulerScale[scaleIndex] * increment > minSeparation)
				break;
		}

		int divideIndex;
		for (divideIndex = 0; divideIndex < subdivide.Length - 1; ++divideIndex) {
			if (rulerScale[scaleIndex] * increment < 5 * subdivide[divideIndex + 1])
				break;
		}

		double pixelsPerTick = increment * rulerScale[scaleIndex] / subdivide[divideIndex];
		double unitsPerTick = pixelsPerTick / increment;
		double ticksPerUnit = 1.0 / unitsPerTick;

		return new (
			SubDivide: subdivide,
			ScaledRange: scaledRange,
			Font: font,
			Increment: increment,
			DivideIndex: divideIndex,
			PixelsPerTick: pixelsPerTick,
			UnitsPerTick: unitsPerTick,
			Ticks: new (
				lower: (int) Math.Floor (scaledRange.Lower * ticksPerUnit),
				upper: (int) Math.Ceiling (scaledRange.Upper * ticksPerUnit)),
			MarkerPosition: GetPositionOnRuler (Position, effectiveSize.Width),
			RulerOuterLine: rulerOuterLine,
			EffectiveSize: effectiveSize,
			Color: color.ToCairoColor (),
			Orientation: Orientation);
	}

	private void Draw (Context cr, Size preliminarySize)
	{
		if (last_known_size != preliminarySize) {
			InvalidateCache ();
			last_known_size = new Size (preliminarySize.Width, preliminarySize.Height);
		}

		RulerDrawSettings settings = CreateSettings (preliminarySize);

		cached_surface ??= CreateBaseRuler (settings, preliminarySize);

		// Draw the selection projection if a selection exists
		if (selection_bounds.HasValue) {

			// Convert selection coordinates to ruler widget coordinates
			double p1 = GetPositionOnRuler (selection_bounds.Value.Lower, settings.EffectiveSize.Width);
			double p2 = GetPositionOnRuler (selection_bounds.Value.Upper, settings.EffectiveSize.Width);

			cr.SetSourceRgba ( // Semi-transparent blue
				red: 0.21,
				green: 0.52,
				blue: 0.89,
				alpha: 0.25);

			switch (Orientation) {
				case Gtk.Orientation.Horizontal:
					cr.Rectangle (p1, 0, p2 - p1, settings.EffectiveSize.Height);
					break;
				default:
					cr.Rectangle (0, p1, settings.EffectiveSize.Height, p2 - p1);
					break;
			}

			cr.Fill ();
		}

		cr.SetSourceSurface (cached_surface, 0, 0);
		cr.Paint ();

		cr.SetSourceColor (settings.Color);
		cr.LineWidth = 1.0;

		// Draw marker
		switch (settings.Orientation) {
			case Gtk.Orientation.Horizontal:
				cr.MoveTo (settings.MarkerPosition, 0);
				cr.LineTo (settings.MarkerPosition, settings.EffectiveSize.Height);
				break;
			case Gtk.Orientation.Vertical:
				cr.MoveTo (0, settings.MarkerPosition);
				cr.LineTo (settings.EffectiveSize.Height, settings.MarkerPosition);
				break;
		}

		cr.Stroke ();
	}

	private ImageSurface CreateBaseRuler (in RulerDrawSettings settings, Size preliminarySize)
	{
		ImageSurface result = new (
			Format.Argb32,
			preliminarySize.Width,
			preliminarySize.Height);

		using Context drawingContext = new (result);

		// Grey ticks and edge, darker numbers, as in Paint.NET.
		Color tickColor = settings.Color with { A = settings.Color.A * 0.5 };
		Color labelColor = settings.Color with { A = settings.Color.A * 0.7 };

		drawingContext.SetSourceColor (tickColor);
		drawingContext.LineWidth = 1.0;
		drawingContext.Rectangle (settings.RulerOuterLine);
		drawingContext.Fill ();

		for (int i = settings.Ticks.Lower; i <= settings.Ticks.Upper; ++i) {

			// Position of tick (add 0.5 to center tick on pixel).
			double tickPosition = Math.Floor (i * settings.PixelsPerTick - settings.ScaledRange.Lower * settings.Increment) + 0.5;

			// Length of tick: the deeper its subdivision, the shorter.
			int depth = 0;
			for (int j = settings.DivideIndex; j > 0; --j) {
				if (i % settings.SubDivide[j] == 0) break;
				depth++;
			}
			int tickHeight = Math.Min (settings.EffectiveSize.Height, tick_lengths[Math.Min (depth, tick_lengths.Length - 1)]);

			// Numbers beside the labelled ticks, in the increasing direction. The vertical ruler's read
			// upwards, as in Paint.NET.
			if (i % settings.SubDivide[settings.DivideIndex] == 0) {

				string label = ((int) Math.Round (i * settings.UnitsPerTick)).ToString ();
				var layout = CreatePangoLayout (label);
				layout.SetFontDescription (settings.Font);
				layout.GetPixelSize (out int labelWidth, out _);

				drawingContext.SetSourceColor (labelColor);
				if (settings.Orientation == Gtk.Orientation.Horizontal) {
					drawingContext.MoveTo (tickPosition + 2, 0);
					PangoCairo.Functions.ShowLayout (drawingContext, layout);
				} else {
					drawingContext.Save ();
					drawingContext.Translate (0, tickPosition + 2 + labelWidth);
					drawingContext.Rotate (-0.5 * Math.PI);
					drawingContext.MoveTo (0, 0);
					PangoCairo.Functions.ShowLayout (drawingContext, layout);
					drawingContext.Restore ();
				}
				drawingContext.SetSourceColor (tickColor);
			}

			// Draw ticks
			if (settings.Orientation == Gtk.Orientation.Horizontal) {
				drawingContext.MoveTo (tickPosition, settings.EffectiveSize.Height - tickHeight);
				drawingContext.LineTo (tickPosition, settings.EffectiveSize.Height);
			} else {
				drawingContext.MoveTo (settings.EffectiveSize.Height - tickHeight, tickPosition);
				drawingContext.LineTo (settings.EffectiveSize.Height, tickPosition);
			}
			drawingContext.Stroke ();
		}

		return result;
	}

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	private double GetPositionOnRuler (double position, double width)
	{
		double range = RulerRange.Upper - RulerRange.Lower;
		double scaledWidth = width / range;
		double positionFromLower = position - RulerRange.Lower;
		return positionFromLower * scaledWidth;
	}
}
