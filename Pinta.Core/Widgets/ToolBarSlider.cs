using System;
using Pinta.Resources;

namespace Pinta.Core;

/// <summary>
/// Paint.NET's tool bar slider: a filled bar that reads "75%" inside it, between − and + buttons.
/// A click or drag anywhere on the bar sets the value at that point, so the left edge gives the minimum.
/// </summary>
[GObject.Subclass<Gtk.Box>]
public sealed partial class ToolBarSlider
{
	/// <summary>The bar. Its own value is the filled fraction (0 to 1), not the slider's value.</summary>
	public Gtk.Scale Scale { get; private set; } = null!; // NRT - set in factory method
	private Gtk.Label value_label = null!;
	private int min;
	private int max;
	private int step;
	private double curve;

	/// <summary>
	/// <paramref name="curve"/> above 1 gives the low values more of the bar
	/// (the bar fills as (value fraction)^(1/curve)), as Paint.NET's Spacing bar does.
	/// </summary>
	public static ToolBarSlider New (int min, int max, int step, int value, double curve = 1)
	{
		ToolBarSlider widget = NewWithProperties ([]);
		widget.Build (min, max, step, value, curve);
		return widget;
	}

	public double GetValue ()
		=> ToolBarSliderMath.ToValue (Scale.GetValue (), min, max, step, curve);

	public void SetValue (double value)
		=> Scale.SetValue (ToolBarSliderMath.ToFraction (value, min, max, curve));

	partial void Initialize ()
	{
		SetOrientation (Gtk.Orientation.Horizontal);
		Spacing = 2;
	}

	private void Build (int min, int max, int step, int value, double curve)
	{
		this.min = min;
		this.max = max;
		this.step = step;
		this.curve = curve;

		// Arrow keys move the bar by about one value step.
		double fraction_step = max > min ? (double) step / (max - min) : 1;
		Scale = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 1, fraction_step);
		Scale.WidthRequest = 175; // Paint.NET's bar width
		Scale.DrawValue = false;
		Scale.SetCssClasses ([Styles.ToolBarScale]);
		// Like the other tool bar controls, never keep keyboard focus away from the canvas.
		Scale.FocusOnClick = false;
		SetValue (value);

		value_label = Gtk.Label.New (null);
		value_label.Halign = Gtk.Align.Start;
		value_label.Valign = Gtk.Align.Center;
		value_label.MarginStart = 4;
		value_label.CanTarget = false;

		Gtk.Overlay bar = Gtk.Overlay.New ();
		bar.SetChild (Scale);
		bar.AddOverlay (value_label);

		// Set the value straight from the pointer position, ahead of the scale's own handling
		// (which centres its knob on the click and so cannot reach the ends).
		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		drag.OnDragBegin += (_, args) => {
			drag.SetState (Gtk.EventSequenceState.Claimed);
			SetFractionAt (args.StartX);
		};
		drag.OnDragUpdate += (_, args) => {
			if (drag.GetStartPoint (out double x, out double _))
				SetFractionAt (x + args.OffsetX);
		};
		Scale.AddController (drag);

		// Keys and the scroll wheel move the value one step at a time. Left to the scale they would
		// move the fill fraction, which its default rounding snaps to tenths of the bar.
		Scale.OnChangeValue += (_, args) => {
			double current = Scale.GetValue ();
			if (args.Value != current)
				SetValue (Math.Clamp (GetValue () + Math.Sign (args.Value - current) * step, min, max));
			return true;
		};

		Scale.OnValueChanged += (_, _) => UpdateLabel ();

		Append (CreateStepButton ("list-remove-symbolic", -1));
		Append (bar);
		Append (CreateStepButton ("list-add-symbolic", 1));

		UpdateLabel ();
	}

	private Gtk.Button CreateStepButton (string icon, int direction)
	{
		Gtk.Button button = Gtk.Button.NewFromIconName (icon);
		button.AddCssClass (AdwaitaStyles.Flat);
		button.FocusOnClick = false;
		button.CanFocus = false;
		button.Valign = Gtk.Align.Center;
		button.OnClicked += (_, _) => SetValue (Math.Clamp (GetValue () + direction * step, min, max));
		return button;
	}

	private void SetFractionAt (double x)
	{
		double width = Scale.GetWidth ();
		// The outer pixels at each end (around the bar's border) already count as the end.
		Scale.SetValue (width > 4 ? Math.Clamp ((x - 2) / (width - 4), 0, 1) : 0);
		// Snap the bar to the value it now shows.
		SetValue (GetValue ());
	}

	private void UpdateLabel ()
	{
		string text = $"{GetValue ():F0}%";

		// White text where the blue fill reaches past the text, the normal text color elsewhere.
		// Before the first allocation the width is still 0, so fall back to the requested width.
		double fill = Scale.GetValue () * Math.Max (Scale.GetWidth (), Scale.WidthRequest);
		value_label.SetText (text);
		value_label.GetLayout ().GetPixelSize (out int text_width, out _);
		if (fill >= value_label.MarginStart + text_width + 2)
			value_label.SetMarkup ($"<span foreground=\"#ffffff\">{text}</span>");
	}
}

/// <summary>
/// The mapping between a <see cref="ToolBarSlider"/>'s value and how far its bar is filled.
/// </summary>
public static class ToolBarSliderMath
{
	/// <summary>
	/// The value shown for a bar filled to <paramref name="fraction"/>, rounded to the step.
	/// </summary>
	public static double ToValue (double fraction, double min, double max, double step, double curve = 1)
	{
		double value = min + Math.Pow (Math.Clamp (fraction, 0, 1), curve) * (max - min);
		if (step > 0)
			value = min + Math.Round ((value - min) / step) * step;
		return Math.Clamp (value, min, max);
	}

	/// <summary>
	/// How far the bar is filled for <paramref name="value"/>. The inverse of <see cref="ToValue"/>.
	/// </summary>
	public static double ToFraction (double value, double min, double max, double curve = 1)
	{
		if (max <= min)
			return 0;
		return Math.Pow (Math.Clamp ((value - min) / (max - min), 0, 1), 1 / curve);
	}
}
