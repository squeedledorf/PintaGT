//
// ResizeCanvasDialog.cs
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
/// Image > Canvas Size, laid out as Paint.NET's: the shared size block, then Options with
/// an Anchor dropdown over the 3x3 anchor grid and a Fill dropdown for the new area.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class ResizeCanvasDialog
{
	/// <summary>Paint.NET's Fill choices, in its order.</summary>
	private enum CanvasFill
	{
		Transparent,
		PrimaryColor,
		SecondaryColor,
		White,
		Black,
	}

	// The Anchor dropdown's order (Paint.NET's), mapped to Pinta's enum.
	private static readonly Anchor[] anchor_order = [
		Anchor.NW, Anchor.N, Anchor.NE,
		Anchor.W, Anchor.Center, Anchor.E,
		Anchor.SW, Anchor.S, Anchor.SE,
	];

	private Gtk.DropDown anchor_dropdown;
	private Gtk.DropDown fill_dropdown;
	private Gtk.Box size_area;

	private Gtk.Button nw_button;
	private Gtk.Button n_button;
	private Gtk.Button ne_button;
	private Gtk.Button w_button;
	private Gtk.Button e_button;
	private Gtk.Button center_button;
	private Gtk.Button sw_button;
	private Gtk.Button s_button;
	private Gtk.Button se_button;

	private Anchor anchor;

	private ImageSizeFields fields = null!; // NRT - set by factory method
	private ISettingsService settings = null!;
	private IPaletteService palette = null!;

	[MemberNotNull (nameof (anchor_dropdown), nameof (fill_dropdown), nameof (size_area))]
	[MemberNotNull (nameof (nw_button), nameof (n_button), nameof (ne_button))]
	[MemberNotNull (nameof (w_button), nameof (e_button), nameof (center_button))]
	[MemberNotNull (nameof (sw_button), nameof (s_button), nameof (se_button))]
	partial void Initialize ()
	{
		const int SPACING = 6;

		Gtk.DropDown anchorDropdown = Gtk.DropDown.NewFromStrings ([
			Translations.GetString ("Top Left"),
			Translations.GetString ("Top"),
			Translations.GetString ("Top Right"),
			Translations.GetString ("Left"),
			Translations.GetString ("Middle"),
			Translations.GetString ("Right"),
			Translations.GetString ("Bottom Left"),
			Translations.GetString ("Bottom"),
			Translations.GetString ("Bottom Right")]);
		anchorDropdown.Hexpand = true;
		Gtk.DropDown.SelectedPropertyDefinition.Notify (anchorDropdown, (_, _) => {
			if (anchorDropdown.Selected < anchor_order.Length)
				SetAnchor (anchor_order[anchorDropdown.Selected]);
		});

		Gtk.DropDown fillDropdown = Gtk.DropDown.NewFromStrings ([
			Translations.GetString ("Transparent"),
			Translations.GetString ("Primary Color"),
			Translations.GetString ("Secondary Color"),
			Translations.GetString ("White"),
			Translations.GetString ("Black")]);
		fillDropdown.Hexpand = true;

		Gtk.Button nwButton = CreateAnchorButton ();
		nwButton.OnClicked += (_, _) => SetAnchor (Anchor.NW);

		Gtk.Button nButton = CreateAnchorButton ();
		nButton.OnClicked += (_, _) => SetAnchor (Anchor.N);

		Gtk.Button neButton = CreateAnchorButton ();
		neButton.OnClicked += (_, _) => SetAnchor (Anchor.NE);

		Gtk.Button wButton = CreateAnchorButton ();
		wButton.OnClicked += (_, _) => SetAnchor (Anchor.W);

		Gtk.Button eButton = CreateAnchorButton ();
		eButton.OnClicked += (_, _) => SetAnchor (Anchor.E);

		Gtk.Button centerButton = CreateAnchorButton ();
		centerButton.OnClicked += (_, _) => SetAnchor (Anchor.Center);

		Gtk.Button swButton = CreateAnchorButton ();
		swButton.OnClicked += (_, _) => SetAnchor (Anchor.SW);

		Gtk.Button sButton = CreateAnchorButton ();
		sButton.OnClicked += (_, _) => SetAnchor (Anchor.S);

		Gtk.Button seButton = CreateAnchorButton ();
		seButton.OnClicked += (_, _) => SetAnchor (Anchor.SE);

		Gtk.Grid anchorGrid = Gtk.Grid.New ();
		anchorGrid.Halign = Gtk.Align.Center;
		anchorGrid.Attach (nwButton, 0, 0, 1, 1);
		anchorGrid.Attach (nButton, 1, 0, 1, 1);
		anchorGrid.Attach (neButton, 2, 0, 1, 1);
		anchorGrid.Attach (wButton, 0, 1, 1, 1);
		anchorGrid.Attach (centerButton, 1, 1, 1, 1);
		anchorGrid.Attach (eButton, 2, 1, 1, 1);
		anchorGrid.Attach (swButton, 0, 2, 1, 1);
		anchorGrid.Attach (sButton, 1, 2, 1, 1);
		anchorGrid.Attach (seButton, 2, 2, 1, 1);

		Gtk.Label anchorLabel = Gtk.Label.NewWithMnemonic (Translations.GetString ("_Anchor:"));
		anchorLabel.Xalign = 0;
		anchorLabel.MnemonicWidget = anchorDropdown;

		Gtk.Label fillLabel = Gtk.Label.NewWithMnemonic (Translations.GetString ("_Fill:"));
		fillLabel.Xalign = 0;
		fillLabel.MnemonicWidget = fillDropdown;

		Gtk.Grid optionsGrid = Gtk.Grid.New ();
		optionsGrid.RowSpacing = SPACING;
		optionsGrid.ColumnSpacing = SPACING;
		optionsGrid.Attach (anchorLabel, 0, 0, 1, 1);
		optionsGrid.Attach (anchorDropdown, 1, 0, 1, 1);
		optionsGrid.Attach (anchorGrid, 1, 1, 1, 1);
		optionsGrid.Attach (fillLabel, 0, 2, 1, 1);
		optionsGrid.Attach (fillDropdown, 1, 2, 1, 1);

		Gtk.Box sizeArea = Gtk.Box.New (Gtk.Orientation.Vertical, 0);

		Gtk.Box mainVbox = Gtk.Box.New (Gtk.Orientation.Vertical, SPACING);
		mainVbox.Append (sizeArea);
		mainVbox.Append (ImageSizeFields.SectionHeader (Translations.GetString ("Options")));
		mainVbox.Append (optionsGrid);

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("Canvas Size");
		Modal = true;
		Resizable = false;
		IconName = Resources.Icons.ImageResizeCanvas;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
		OnResponse += OnDialogResponse;

		var contentArea = this.GetContentAreaBox ();
		contentArea.SetAllMargins (12);
		contentArea.Append (mainVbox);

		// --- References to keep

		anchor_dropdown = anchorDropdown;
		fill_dropdown = fillDropdown;
		size_area = sizeArea;

		nw_button = nwButton;
		n_button = nButton;
		ne_button = neButton;
		w_button = wButton;
		e_button = eButton;
		center_button = centerButton;
		sw_button = swButton;
		s_button = sButton;
		se_button = seButton;
	}

	private void Configure (IChromeService chrome, IWorkspaceService workspace, ISettingsService settings, IPaletteService palette)
	{
		TransientFor = chrome.MainWindow;
		this.settings = settings;
		this.palette = palette;

		// Always start from the current image's size, as in Paint.NET.
		fields = new ImageSizeFields (
			workspace.ImageSize,
			workspace.ActiveDocument.Dpi,
			(PrintUnit) settings.GetSetting (SettingNames.PRINT_UNITS, (int) PrintUnit.Inches),
			withPercentage: true);
		size_area.Append (fields.Widget);

		fields.MaintainAspectRatio = settings.GetSetting (SettingNames.RESIZE_CANVAS_MAINTAIN_ASPECT, false);

		SetAnchor ((Anchor) settings.GetSetting (SettingNames.RESIZE_CANVAS_ANCHOR, (int) Anchor.Center));

		// The Paint.NET documentation shows White as the fill.
		fill_dropdown.Selected = (uint) Math.Clamp (
			settings.GetSetting (SettingNames.RESIZE_CANVAS_FILL, (int) CanvasFill.White),
			0,
			Enum.GetValues<CanvasFill> ().Length - 1);

		// Paint.NET opens in "By absolute size" with Width selected, so typing a number sets the width.
		fields.ByPercentage = settings.GetSetting (SettingNames.RESIZE_CANVAS_USE_PERCENTAGE, false);
		OnMap += (_, _) => fields.FocusFirstField ();
	}

	public static ResizeCanvasDialog New (IChromeService chrome, IWorkspaceService workspace, ISettingsService settings, IPaletteService palette)
	{
		ResizeCanvasDialog dialog = NewWithProperties ([]);
		dialog.Configure (chrome, workspace, settings, palette);
		return dialog;
	}

	private void OnDialogResponse (Gtk.Dialog sender, ResponseSignalArgs args)
	{
		if (args.ResponseId != (int) Gtk.ResponseType.Ok)
			return;

		// Save settings for next time
		settings.PutSetting (SettingNames.RESIZE_CANVAS_ANCHOR, (int) anchor);
		settings.PutSetting (SettingNames.RESIZE_CANVAS_MAINTAIN_ASPECT, fields.MaintainAspectRatio);
		settings.PutSetting (SettingNames.RESIZE_CANVAS_USE_PERCENTAGE, fields.ByPercentage);
		settings.PutSetting (SettingNames.RESIZE_CANVAS_FILL, (int) fill_dropdown.Selected);
		settings.PutSetting (SettingNames.PRINT_UNITS, (int) fields.Unit);
	}

	private static Gtk.Button CreateAnchorButton ()
	{
		Gtk.Button button = Gtk.Button.New ();
		button.WidthRequest = 30;
		button.HeightRequest = 30;
		return button;
	}

	public ResizeCanvasOptions GetResizeCanvasOptions ()
	{
		fields.CommitTypedValues ();
		Cairo.Color? fill = (CanvasFill) fill_dropdown.Selected switch {
			CanvasFill.PrimaryColor => palette.PrimaryColor,
			CanvasFill.SecondaryColor => palette.SecondaryColor,
			CanvasFill.White => new Cairo.Color (1, 1, 1),
			CanvasFill.Black => new Cairo.Color (0, 0, 0),
			_ => null,
		};
		return new (fields.PixelSize, anchor, null, fill, fields.Dpi);
	}

	private void SetAnchor (Anchor anchor)
	{
		this.anchor = anchor;

		uint index = (uint) Array.IndexOf (anchor_order, anchor);
		if (anchor_dropdown.Selected != index)
			anchor_dropdown.Selected = index;

		nw_button.IconName = "";
		n_button.IconName = "";
		ne_button.IconName = "";
		w_button.IconName = "";
		e_button.IconName = "";
		center_button.IconName = "";
		sw_button.IconName = "";
		s_button.IconName = "";
		se_button.IconName = "";

		switch (anchor) {

			case Anchor.NW:
				nw_button.IconName = Resources.Icons.ResizeCanvasBase;
				n_button.IconName = Resources.Icons.ResizeCanvasRight;
				w_button.IconName = Resources.Icons.ResizeCanvasDown;
				center_button.IconName = Resources.Icons.ResizeCanvasSE;
				break;

			case Anchor.N:
				nw_button.IconName = Resources.Icons.ResizeCanvasLeft;
				n_button.IconName = Resources.Icons.ResizeCanvasBase;
				ne_button.IconName = Resources.Icons.ResizeCanvasRight;
				w_button.IconName = Resources.Icons.ResizeCanvasSW;
				e_button.IconName = Resources.Icons.ResizeCanvasSE;
				center_button.IconName = Resources.Icons.ResizeCanvasDown;
				break;

			case Anchor.NE:
				ne_button.IconName = Resources.Icons.ResizeCanvasBase;
				n_button.IconName = Resources.Icons.ResizeCanvasLeft;
				e_button.IconName = Resources.Icons.ResizeCanvasDown;
				center_button.IconName = Resources.Icons.ResizeCanvasSW;
				break;

			case Anchor.W:
				nw_button.IconName = Resources.Icons.ResizeCanvasUp;
				n_button.IconName = Resources.Icons.ResizeCanvasNE;
				sw_button.IconName = Resources.Icons.ResizeCanvasDown;
				w_button.IconName = Resources.Icons.ResizeCanvasBase;
				s_button.IconName = Resources.Icons.ResizeCanvasSE;
				center_button.IconName = Resources.Icons.ResizeCanvasRight;
				break;

			case Anchor.Center:
				nw_button.IconName = Resources.Icons.ResizeCanvasNW;
				n_button.IconName = Resources.Icons.ResizeCanvasUp;
				ne_button.IconName = Resources.Icons.ResizeCanvasNE;
				w_button.IconName = Resources.Icons.ResizeCanvasLeft;
				e_button.IconName = Resources.Icons.ResizeCanvasRight;
				sw_button.IconName = Resources.Icons.ResizeCanvasSW;
				s_button.IconName = Resources.Icons.ResizeCanvasDown;
				se_button.IconName = Resources.Icons.ResizeCanvasSE;
				center_button.IconName = Resources.Icons.ResizeCanvasBase;
				break;

			case Anchor.E:
				ne_button.IconName = Resources.Icons.ResizeCanvasUp;
				n_button.IconName = Resources.Icons.ResizeCanvasNW;
				se_button.IconName = Resources.Icons.ResizeCanvasDown;
				e_button.IconName = Resources.Icons.ResizeCanvasBase;
				s_button.IconName = Resources.Icons.ResizeCanvasSW;
				center_button.IconName = Resources.Icons.ResizeCanvasLeft;
				break;

			case Anchor.SW:
				sw_button.IconName = Resources.Icons.ResizeCanvasBase;
				s_button.IconName = Resources.Icons.ResizeCanvasRight;
				w_button.IconName = Resources.Icons.ResizeCanvasUp;
				center_button.IconName = Resources.Icons.ResizeCanvasNE;
				break;

			case Anchor.S:
				sw_button.IconName = Resources.Icons.ResizeCanvasLeft;
				s_button.IconName = Resources.Icons.ResizeCanvasBase;
				se_button.IconName = Resources.Icons.ResizeCanvasRight;
				w_button.IconName = Resources.Icons.ResizeCanvasNW;
				e_button.IconName = Resources.Icons.ResizeCanvasNE;
				center_button.IconName = Resources.Icons.ResizeCanvasUp;
				break;

			case Anchor.SE:
				se_button.IconName = Resources.Icons.ResizeCanvasBase;
				s_button.IconName = Resources.Icons.ResizeCanvasLeft;
				e_button.IconName = Resources.Icons.ResizeCanvasUp;
				center_button.IconName = Resources.Icons.ResizeCanvasNW;
				break;
		}
	}
}

