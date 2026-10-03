//
// SaveConfigurationDialog.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva
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
using Pinta.Core;

namespace Pinta;

/// <summary>
/// Paint.NET's Save Configuration dialog: the format's settings on the left, and on the right a
/// preview of the image as the saved file will load, with the file size.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class SaveConfigurationDialog
{
	private SaveConfigurationEventArgs args = null!; // NRT - set by factory method

	private Gtk.Adjustment quality = null!;
	private Gtk.DropDown bit_depth = null!;
	private Gtk.Adjustment dithering = null!;
	private Gtk.Adjustment threshold = null!;
	private Gtk.Widget[] eight_bit_only = [];

	private Gtk.Label size_label = null!;
	private Gtk.Picture preview = null!;

	private bool busy;
	private bool pending;
	private bool closed;
	private Task? running;
	private readonly System.Collections.Generic.List<Gtk.SpinButton> spins = [];

	partial void Initialize ()
	{
		Title = Translations.GetString ("Save Configuration");
		Modal = true;
		IconName = Resources.StandardIcons.DocumentSave;
		DefaultWidth = 925;
		DefaultHeight = 533;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
	}

	private void Configure (SaveConfigurationEventArgs e)
	{
		args = e;
		TransientFor = e.ParentWindow;
		SaveConfiguration config = e.Configuration;

		// --- Settings (left)

		Gtk.Box settings = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		settings.WidthRequest = 190;
		settings.Hexpand = false; // The rules in the section headers would otherwise widen this column.
		settings.Append (ImageSizeFields.SectionHeader (Translations.GetString ("Settings")));

		if (e.FileType == "png") {
			bit_depth = Gtk.DropDown.NewFromStrings ([
				Translations.GetString ("Auto-detect"),
				Translations.GetString ("32-bit"),
				Translations.GetString ("24-bit"),
				Translations.GetString ("8-bit")]);
			bit_depth.Halign = Gtk.Align.Start;
			bit_depth.Selected = (uint) config.BitDepth;
			Gtk.DropDown.SelectedPropertyDefinition.Notify (bit_depth, (_, _) => {
				UpdateSensitivity ();
				Refresh ();
			});

			Gtk.DropDown quantization = Gtk.DropDown.NewFromStrings ([Translations.GetString ("Octree")]);
			quantization.Halign = Gtk.Align.Start;

			dithering = Gtk.Adjustment.New (config.DitheringLevel, 0, SaveConfiguration.MaxDitheringLevel, 1, 1, 0);
			threshold = Gtk.Adjustment.New (config.TransparencyThreshold, 0, 255, 1, 16, 0);

			Gtk.Label thresholdNote = Gtk.Label.New (Translations.GetString ("Pixels with an alpha value less than the threshold will be fully transparent."));
			thresholdNote.Wrap = true;
			thresholdNote.Xalign = 0;
			thresholdNote.MaxWidthChars = 28;
			thresholdNote.AddCssClass ("dim-label");

			Gtk.Widget quantizationHeader = Indented (ImageSizeFields.SectionHeader (Translations.GetString ("Quantization algorithm")));
			Gtk.Widget ditheringHeader = Indented (ImageSizeFields.SectionHeader (Translations.GetString ("Dithering level")));
			Gtk.Widget ditheringRow = SliderRow (dithering);
			Gtk.Widget thresholdHeader = Indented (ImageSizeFields.SectionHeader (Translations.GetString ("Transparency threshold")));
			Gtk.Widget thresholdRow = SliderRow (threshold);

			settings.Append (Indented (ImageSizeFields.SectionHeader (Translations.GetString ("Bit Depth"))));
			settings.Append (bit_depth);
			settings.Append (quantizationHeader);
			settings.Append (quantization);
			settings.Append (ditheringHeader);
			settings.Append (ditheringRow);
			settings.Append (thresholdHeader);
			settings.Append (thresholdRow);
			settings.Append (thresholdNote);

			eight_bit_only = [quantizationHeader, quantization, ditheringHeader, ditheringRow, thresholdHeader, thresholdRow, thresholdNote];
			dithering.OnValueChanged += (_, _) => Refresh ();
			threshold.OnValueChanged += (_, _) => Refresh ();
			UpdateSensitivity ();
		} else {
			quality = Gtk.Adjustment.New (config.JpegQuality, 1, 100, 1, 10, 0);
			quality.OnValueChanged += (_, _) => Refresh ();
			settings.Append (Indented (ImageSizeFields.SectionHeader (Translations.GetString ("Quality"))));
			settings.Append (SliderRow (quality));
		}

		Gtk.Button defaults = Gtk.Button.NewWithMnemonic (Translations.GetString ("_Defaults"));
		defaults.Halign = Gtk.Align.Center;
		defaults.MarginTop = 12;
		defaults.WidthRequest = 82;
		defaults.OnClicked += (_, _) => ApplyDefaults ();
		settings.Append (defaults);

		// --- Preview (right)

		size_label = Gtk.Label.New (Translations.GetString ("Preview, file size: {0}", "…"));

		preview = Gtk.Picture.New ();
		preview.CanShrink = false;
		preview.Halign = Gtk.Align.Start;
		preview.Valign = Gtk.Align.Start;

		Gtk.ScrolledWindow scroller = Gtk.ScrolledWindow.New ();
		scroller.Child = preview;
		scroller.Hexpand = true;
		scroller.Vexpand = true;
		scroller.HasFrame = true;

		Gtk.Box previewBox = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		previewBox.Append (ImageSizeFields.SectionHeader (size_label));
		previewBox.Append (scroller);

		Gtk.Box main = Gtk.Box.New (Gtk.Orientation.Horizontal, 12);
		main.Append (settings);
		main.Append (previewBox);

		Gtk.Box content = this.GetContentAreaBox ();
		content.SetAllMargins (8);
		content.Append (main);

		OnResponse += (_, r) => {
			foreach (Gtk.SpinButton spin in spins)
				spin.Update (); // Commit a typed value that Enter has not applied yet
			// Let a preview still being encoded finish before the image it reads is freed.
			closed = true;
			running?.Wait ();
			if (r.ResponseId == (int) Gtk.ResponseType.Ok)
				args.Configuration = Current;
			else
				args.Cancel = true;
		};

		Refresh ();
	}

	public static SaveConfigurationDialog New (SaveConfigurationEventArgs e)
	{
		SaveConfigurationDialog dialog = NewWithProperties ([]);
		dialog.Configure (e);
		return dialog;
	}

	private SaveConfiguration Current
		=> args.FileType == "png"
			? args.Configuration with {
				BitDepth = (PngBitDepth) bit_depth.Selected,
				DitheringLevel = (int) dithering.Value,
				TransparencyThreshold = (int) threshold.Value,
			}
			: args.Configuration with { JpegQuality = (int) quality.Value };

	private void ApplyDefaults ()
	{
		SaveConfiguration d = SaveConfiguration.Defaults;
		if (args.FileType == "png") {
			bit_depth.Selected = (uint) d.BitDepth;
			dithering.Value = d.DitheringLevel;
			threshold.Value = d.TransparencyThreshold;
		} else
			quality.Value = d.JpegQuality;
	}

	private void UpdateSensitivity ()
	{
		bool eightBit = (PngBitDepth) bit_depth.Selected == PngBitDepth.Bpp8;
		foreach (Gtk.Widget w in eight_bit_only)
			w.Sensitive = eightBit;
	}

	/// <summary>
	/// Encode in the background and show the result; changes made meanwhile are coalesced into one more run.
	/// </summary>
	private void Refresh ()
	{
		if (closed)
			return;
		if (busy) {
			pending = true;
			return;
		}

		busy = true;
		pending = false;
		SaveConfiguration config = Current;
		running = Task.Run (() => {
			byte[]? data = null;
			try {
				data = args.Encode (config);
			} catch (Exception ex) {
				Console.Error.WriteLine ($"Save Configuration preview failed: {ex.Message}");
			}

			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
				busy = false;
				if (closed)
					return false;
				if (data is not null)
					ShowPreview (data);
				if (pending)
					Refresh ();
				return false;
			});
		});
	}

	private void ShowPreview (byte[] data)
	{
		size_label.SetText (Translations.GetString ("Preview, file size: {0}", PrintSize.FormatBytes (data.Length)));
		try {
			using GLib.Bytes bytes = GLib.Bytes.New (data);
			preview.Paintable = Gdk.Texture.NewFromBytes (bytes);
		} catch (GLib.GException ex) {
			Console.Error.WriteLine ($"Save Configuration preview could not be decoded: {ex.Message}");
		}
	}

	private static Gtk.Widget Indented (Gtk.Widget w)
	{
		w.MarginStart = 4;
		return w;
	}

	/// <summary>A slider and a number box sharing one value, as in Paint.NET.</summary>
	private Gtk.Widget SliderRow (Gtk.Adjustment adjustment)
	{
		Gtk.Scale scale = Gtk.Scale.New (Gtk.Orientation.Horizontal, adjustment);
		scale.DrawValue = false;
		scale.Hexpand = true;

		Gtk.SpinButton spin = Gtk.SpinButton.New (adjustment, 1, 0);
		spin.SetActivatesDefaultImmediate (true);
		spins.Add (spin);

		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		row.MarginStart = 4;
		row.Append (scale);
		row.Append (spin);
		return row;
	}
}
