//
// ResizeImageDialog.cs
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
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta;

/// <summary>
/// Image > Resize, laid out as Paint.NET's: the shared size block, then Options with
/// Resampling (Bicubic by default) and Use gamma correction.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class ResizeImageDialog
{
	private Gtk.DropDown resampling_dropdown;
	private Gtk.CheckButton gamma_checkbox;
	private Gtk.Box size_area;

	private ImageSizeFields fields = null!; // NRT - set by factory method
	private ISettingsService settings = null!;
	private double original_dpi;

	const int SPACING = 6;

	[MemberNotNull (nameof (resampling_dropdown), nameof (gamma_checkbox), nameof (size_area))]
	partial void Initialize ()
	{
		Gtk.StringList modes = Gtk.StringList.New ([]);
		foreach (ResamplingMode mode in Enum.GetValues<ResamplingMode> ())
			modes.Append (mode.GetLabel ());
		Gtk.DropDown resamplingDropdown = Gtk.DropDown.New (modes, expression: null);
		resamplingDropdown.Hexpand = true;

		Gtk.Button resetButton = Gtk.Button.NewFromIconName (Resources.StandardIcons.EditUndo);
		resetButton.TooltipText = Translations.GetString ("Reset");
		resetButton.OnClicked += (_, _) => {
			fields.Reset (original_dpi);
			resamplingDropdown.Selected = (uint) ResamplingMode.Bicubic;
		};

		Gtk.Label resamplingLabel = Gtk.Label.NewWithMnemonic (Translations.GetString ("_Resampling:"));
		resamplingLabel.Xalign = 0;
		resamplingLabel.MnemonicWidget = resamplingDropdown;

		Gtk.Grid optionsGrid = Gtk.Grid.New ();
		optionsGrid.RowSpacing = SPACING;
		optionsGrid.ColumnSpacing = SPACING;
		optionsGrid.Attach (resamplingLabel, 0, 0, 1, 1);
		optionsGrid.Attach (resamplingDropdown, 1, 0, 1, 1);
		optionsGrid.Attach (resetButton, 2, 0, 1, 1);

		Gtk.CheckButton gammaCheckbox = Gtk.CheckButton.NewWithMnemonic (Translations.GetString ("Use _gamma correction"));

		Gtk.Box sizeArea = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

		Gtk.Box mainVbox = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		mainVbox.Append (sizeArea);
		mainVbox.Append (ImageSizeFields.SectionHeader (Translations.GetString ("Options")));
		mainVbox.Append (optionsGrid);
		mainVbox.Append (gammaCheckbox);

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("Resize");
		Modal = true;
		Resizable = false;
		IconName = Resources.Icons.ImageResize;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
		OnResponse += OnDialogResponse;

		Gtk.Box contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);
		contentArea.Append (mainVbox);

		// --- References to keep

		resampling_dropdown = resamplingDropdown;
		gamma_checkbox = gammaCheckbox;
		size_area = sizeArea;
	}

	private void Configure (IChromeService chrome, IWorkspaceService workspace, ISettingsService settings)
	{
		TransientFor = chrome.MainWindow;
		this.settings = settings;
		original_dpi = workspace.ActiveDocument.Dpi;

		// Always start from the current image's size, as in Paint.NET.
		fields = new ImageSizeFields (
			workspace.ImageSize,
			original_dpi,
			(PrintUnit) settings.GetSetting (SettingNames.PRINT_UNITS, (int) PrintUnit.Inches),
			withPercentage: true);
		size_area.Append (fields.Widget);

		fields.MaintainAspectRatio = settings.GetSetting (SettingNames.RESIZE_IMAGE_MAINTAIN_ASPECT, true);
		resampling_dropdown.Selected = (uint) Math.Clamp (
			settings.GetSetting (SettingNames.RESIZE_IMAGE_RESAMPLING, (int) ResamplingMode.Bicubic),
			0,
			Enum.GetValues<ResamplingMode> ().Length - 1);
		gamma_checkbox.Active = settings.GetSetting (SettingNames.RESIZE_IMAGE_GAMMA, true);

		// Paint.NET opens in "By absolute size" with Width selected, so typing a number sets the width.
		fields.ByPercentage = settings.GetSetting (SettingNames.RESIZE_IMAGE_USE_PERCENTAGE, false);
		OnMap += (_, _) => fields.FocusFirstField ();
	}

	internal static ResizeImageDialog New (IChromeService chrome, IWorkspaceService workspace, ISettingsService settings)
	{
		ResizeImageDialog dialog = NewWithProperties ([]);
		dialog.Configure (chrome, workspace, settings);
		return dialog;
	}

	private void OnDialogResponse (Gtk.Dialog sender, ResponseSignalArgs args)
	{
		if (args.ResponseId != (int) Gtk.ResponseType.Ok)
			return;

		// Save settings for next time
		settings.PutSetting (SettingNames.RESIZE_IMAGE_MAINTAIN_ASPECT, fields.MaintainAspectRatio);
		settings.PutSetting (SettingNames.RESIZE_IMAGE_USE_PERCENTAGE, fields.ByPercentage);
		settings.PutSetting (SettingNames.RESIZE_IMAGE_RESAMPLING, (int) resampling_dropdown.Selected);
		settings.PutSetting (SettingNames.RESIZE_IMAGE_GAMMA, gamma_checkbox.Active);
		settings.PutSetting (SettingNames.PRINT_UNITS, (int) fields.Unit);
	}

	public ResizeImageOptions GetResizeImageOptions ()
	{
		fields.CommitTypedValues ();
		return new (
			fields.PixelSize,
			(ResamplingMode) resampling_dropdown.Selected,
			gamma_checkbox.Active,
			fields.Dpi);
	}
}
