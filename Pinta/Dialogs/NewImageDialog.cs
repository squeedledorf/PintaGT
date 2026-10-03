//
// NewImageDialog.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2015 Jonathan Pobst
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

using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta;

/// <summary>
/// File > New, laid out as Paint.NET's: "New size", Maintain aspect ratio, Pixel size
/// (Width, Height, Resolution) and Print size. The new image is white, as in Paint.NET.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class NewImageDialog
{
	private Gtk.Box size_area;
	private ImageSizeFields fields = null!; // NRT - set by factory method

	[MemberNotNull (nameof (size_area))]
	partial void Initialize ()
	{
		Gtk.Box sizeArea = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);
		contentArea.Append (sizeArea);

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("New");
		Modal = true;
		Resizable = false;
		IconName = Resources.StandardIcons.DocumentNew;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);

		this.PressOkOnEnter ();

		// --- References to keep
		size_area = sizeArea;
	}

	private void Configure (IChromeService chrome, ISettingsService settings, Size initialSize)
	{
		TransientFor = chrome.MainWindow;

		fields = new ImageSizeFields (
			initialSize,
			Document.DefaultDpi,
			(PrintUnit) settings.GetSetting (SettingNames.PRINT_UNITS, (int) PrintUnit.Inches),
			withPercentage: false);
		size_area.Append (fields.Widget);

		// Select the width once the dialog is up, so typing replaces it (focusing the window clears an earlier selection).
		OnMap += (_, _) => fields.FocusFirstField ();

		OnResponse += (_, args) => {
			if (args.ResponseId == (int) Gtk.ResponseType.Ok)
				settings.PutSetting (SettingNames.PRINT_UNITS, (int) fields.Unit);
		};
	}

	/// <summary>
	/// Configures and builds a NewImageDialog object.
	/// </summary>
	/// <param name="initialSize">The starting size: the clipboard image's, or the last one used.</param>
	public static NewImageDialog New (IChromeService chrome, ISettingsService settings, Size initialSize)
	{
		NewImageDialog dialog = NewWithProperties ([]);
		dialog.Configure (chrome, settings, initialSize);
		return dialog;
	}

	public NewImageOptions GetNewImageOptions ()
	{
		fields.CommitTypedValues ();
		return new (fields.PixelSize, fields.Dpi);
	}
}
