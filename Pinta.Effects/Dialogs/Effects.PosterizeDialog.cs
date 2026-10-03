//
// PosterizeDialog.cs
//
// Author:
//      Krzysztof Marecki <marecki.krzysztof@gmail.com>
//
// Copyright (c) 2010 Krzysztof Marecki
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
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET's Posterize window: a tick box above each of the Red, Green, Blue and
/// Alpha sliders turns that channel on or off, and Linked keeps the sliders equal.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class PosterizeDialog
{
	private PosterizeData effect_data = new ();
	private HScaleSpinButtonWidget[] sliders = [];
	private Gtk.CheckButton[] channel_checks = [];
	private Gtk.CheckButton? link_button;
	private bool syncing;

	partial void Initialize ()
	{
		Title = Translations.GetString ("Posterize");
		Modal = true;
		Resizable = false;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
	}

	public static PosterizeDialog New (IChromeService chrome, PosterizeData data)
	{
		PosterizeDialog dialog = NewWithProperties ([]);
		dialog.TransientFor = chrome.MainWindow;
		dialog.effect_data = data;
		dialog.BuildChannels ();
		return dialog;
	}

	private void BuildChannels ()
	{
		PosterizeData d = effect_data;
		(string label, int value, bool enabled)[] channels = [
			(Translations.GetString ("Red"), d.Red, d.RedEnabled),
			(Translations.GetString ("Green"), d.Green, d.GreenEnabled),
			(Translations.GetString ("Blue"), d.Blue, d.BlueEnabled),
			(Translations.GetString ("Alpha"), d.Alpha, d.AlphaEnabled),
		];

		Gtk.Box content_area = this.GetContentAreaBox ();
		content_area.WidthRequest = 340;
		content_area.SetAllMargins (6);
		content_area.Spacing = 2;

		sliders = new HScaleSpinButtonWidget[channels.Length];
		channel_checks = new Gtk.CheckButton[channels.Length];

		for (int i = 0; i < channels.Length; i++) {
			HScaleSpinButtonWidget slider = HScaleSpinButtonWidget.New (channels[i].value);
			slider.MaximumValue = 64;
			slider.MinimumValue = 2;
			slider.Sensitive = channels[i].enabled;
			slider.ValueChanged += HandleValueChanged;

			Gtk.CheckButton check = Gtk.CheckButton.NewWithLabel (channels[i].label);
			check.Active = channels[i].enabled;
			check.OnToggled += (_, _) => {
				slider.Sensitive = check.Active;
				UpdateEffectData ();
			};

			sliders[i] = slider;
			channel_checks[i] = check;
			content_area.Append (check);
			content_area.Append (slider);
		}

		link_button = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Linked"));
		link_button.Active = d.Linked;
		link_button.MarginTop = 4;
		link_button.OnToggled += (_, _) => UpdateEffectData ();
		content_area.Append (link_button);
	}

	private void HandleValueChanged (object? sender, EventArgs e)
	{
		if (syncing || sender is not HScaleSpinButtonWidget widget)
			return;

		if (link_button?.Active == true) {
			syncing = true;
			foreach (HScaleSpinButtonWidget slider in sliders)
				slider.Value = widget.Value;
			syncing = false;
		}

		UpdateEffectData ();
	}

	private void UpdateEffectData ()
	{
		if (link_button is null)
			return;

		PosterizeData d = effect_data;
		d.Red = sliders[0].ValueAsInt;
		d.Green = sliders[1].ValueAsInt;
		d.Blue = sliders[2].ValueAsInt;
		d.Alpha = sliders[3].ValueAsInt;
		d.RedEnabled = channel_checks[0].Active;
		d.GreenEnabled = channel_checks[1].Active;
		d.BlueEnabled = channel_checks[2].Active;
		d.AlphaEnabled = channel_checks[3].Active;
		d.Linked = link_button.Active;

		// Only fire event once, even if all properties have changed.
		d.FirePropertyChanged ("_all_");
	}
}
