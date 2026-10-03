//
// LayerPropertiesDialog.cs
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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta;

[GObject.Subclass<Gtk.Dialog>]
public sealed partial class LayerPropertiesDialog
{
	private LayerProperties initial_properties = new (string.Empty, false, 0.0, BlendMode.Normal);

	private double current_layer_opacity;
	private bool current_layer_hidden;
	private string current_layer_name = string.Empty;
	private BlendMode current_layer_blend_mode;

	private Gtk.Entry layer_name_entry;
	private Gtk.CheckButton visibility_checkbox;
	private Gtk.SpinButton opacity_spinner;
	private Gtk.Scale opacity_slider;
	private Gtk.ComboBoxText blend_combo_box;

	private WorkspaceManager workspace = null!; // NRT - set by factory method

	private bool initializing;

	[MemberNotNull (nameof (layer_name_entry))]
	[MemberNotNull (nameof (visibility_checkbox))]
	[MemberNotNull (nameof (opacity_slider))]
	[MemberNotNull (nameof (opacity_spinner))]
	[MemberNotNull (nameof (blend_combo_box))]
	partial void Initialize ()
	{
		const int spacing = 6;

		Gtk.Label nameLabel = Gtk.Label.New (Translations.GetString ("Name"));
		nameLabel.Halign = Gtk.Align.Start;

		Gtk.Entry layerNameEntry = Gtk.Entry.New ();
		layerNameEntry.Hexpand = true;
		layerNameEntry.Halign = Gtk.Align.Fill;
		layerNameEntry.OnChanged += OnLayerNameChanged;
		layerNameEntry.SetActivatesDefault (true);

		Gtk.CheckButton visibilityCheckbox = Gtk.CheckButton.NewWithLabel (Translations.GetString ("Visible"));
		visibilityCheckbox.OnToggled += OnVisibilityToggled;

		Gtk.Label blendLabel = Gtk.Label.New (Translations.GetString ("Blend Mode") + ":");
		blendLabel.Halign = Gtk.Align.Start;

		Gtk.ComboBoxText blendComboBox = Gtk.ComboBoxText.New ();

		foreach (string name in UserBlendOps.GetAllBlendModeNames ())
			blendComboBox.AppendText (name);

		blendComboBox.Halign = Gtk.Align.Start;
		blendComboBox.OnChanged += OnBlendModeChanged;

		Gtk.Label opacityLabel = Gtk.Label.New (Translations.GetString ("Opacity"));
		opacityLabel.Halign = Gtk.Align.Start;

		// Opacity is shown as 0-255, as in Paint.NET.
		Gtk.SpinButton opacitySpinner = Gtk.SpinButton.NewWithRange (0, 255, 1);
		opacitySpinner.Adjustment!.PageIncrement = 10;
		opacitySpinner.ClimbRate = 1;
		opacitySpinner.OnValueChanged += OnOpacitySpinnerChanged;
		opacitySpinner.SetActivatesDefaultImmediate (true);
		opacitySpinner.WidthChars = 6;

		Gtk.Scale opacitySlider = Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 255, 1);
		opacitySlider.Digits = 0;
		opacitySlider.Adjustment!.PageIncrement = 10;
		opacitySlider.Hexpand = true;
		opacitySlider.Halign = Gtk.Align.Fill;
		opacitySlider.OnValueChanged += OnOpacitySliderChanged;

		Gtk.Box opacityBox = Gtk.Box.New (Gtk.Orientation.Horizontal, spacing);
		opacityBox.Append (opacitySlider);
		opacityBox.Append (opacitySpinner.WithStackedStepButtons ());

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = spacing;
		grid.ColumnSpacing = spacing;
		grid.ColumnHomogeneous = false;
		grid.Attach (nameLabel, 0, 0, 2, 1);
		grid.Attach (layerNameEntry, 0, 1, 2, 1);
		// Paint.NET's layout: Name and Opacity are headings above their fields; Blend Mode sits on one row.
		grid.Attach (opacityLabel, 0, 2, 2, 1);
		grid.Attach (opacityBox, 0, 3, 2, 1);
		Gtk.Box blendBox = Gtk.Box.New (Gtk.Orientation.Horizontal, spacing);
		blendBox.Append (blendLabel);
		blendBox.Append (blendComboBox);
		grid.Attach (blendBox, 0, 4, 2, 1);
		grid.Attach (visibilityCheckbox, 0, 5, 2, 1);

		// --- Initialization (Gtk.Window)

		Title = Translations.GetString ("Layer Properties");
		Modal = true;
		DefaultWidth = 302;
		Resizable = false;
		IconName = Resources.Icons.LayerProperties;

		// --- Initialization (Gtk.Dialog)

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
		this.PressOkOnEnter ();

		// --- Initialization

		var contentArea = this.GetContentAreaBox ();
		contentArea.Spacing = spacing;
		contentArea.SetAllMargins (10);
		contentArea.Append (grid);

		// --- References to keep

		layer_name_entry = layerNameEntry;
		visibility_checkbox = visibilityCheckbox;
		blend_combo_box = blendComboBox;
		opacity_spinner = opacitySpinner;
		opacity_slider = opacitySlider;
	}

	public static LayerPropertiesDialog New (IChromeService chrome, WorkspaceManager workspace)
	{
		LayerPropertiesDialog dialog = NewWithProperties ([]);
		dialog.Configure (chrome, workspace);
		return dialog;
	}

	private void Configure (IChromeService chrome, WorkspaceManager workspace)
	{
		this.workspace = workspace;
		TransientFor = chrome.MainWindow;

		Document doc = workspace.ActiveDocument;

		string currentLayerName = doc.Layers.CurrentUserLayer.Name;
		bool currentLayerHidden = doc.Layers.CurrentUserLayer.Hidden;
		double currentLayerOpacity = doc.Layers.CurrentUserLayer.Opacity;
		BlendMode currentLayerBlendMode = doc.Layers.CurrentUserLayer.BlendMode;

		LayerProperties initialProperties = new (
			currentLayerName,
			currentLayerHidden,
			currentLayerOpacity,
			currentLayerBlendMode);

		// Don't let the rounded 0-255 value overwrite the layer's exact opacity unless the user changes it.
		initializing = true;
		layer_name_entry.SetText (initialProperties.Name);
		visibility_checkbox.Active = !initialProperties.Hidden;
		opacity_spinner.Value = Math.Round (initialProperties.Opacity * 255);
		opacity_slider.SetValue (Math.Round (initialProperties.Opacity * 255));
		initializing = false;

		var allBlendmodes = UserBlendOps.GetAllBlendModeNames ().ToImmutableArray ();
		var index = allBlendmodes.IndexOf (UserBlendOps.GetBlendModeName (currentLayerBlendMode));
		blend_combo_box.Active = index;

		current_layer_name = currentLayerName;
		current_layer_hidden = currentLayerHidden;
		current_layer_opacity = currentLayerOpacity;
		current_layer_blend_mode = currentLayerBlendMode;

		initial_properties = initialProperties;
	}

	public bool AreLayerPropertiesUpdated =>
		initial_properties.Opacity != current_layer_opacity
		|| initial_properties.Hidden != current_layer_hidden
		|| initial_properties.Name != current_layer_name
		|| initial_properties.BlendMode != current_layer_blend_mode;

	public LayerProperties InitialLayerProperties
		=> initial_properties;

	public LayerProperties UpdatedLayerProperties
		=> new (
			current_layer_name,
			current_layer_hidden,
			current_layer_opacity,
			current_layer_blend_mode);

	private void OnLayerNameChanged (object? sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;
		current_layer_name = layer_name_entry.GetText ();
		doc.Layers.CurrentUserLayer.Name = current_layer_name;
	}

	private void OnVisibilityToggled (object? sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		current_layer_hidden = !visibility_checkbox.Active;

		doc.Layers.CurrentUserLayer.Hidden = current_layer_hidden;

		if (doc.Layers.SelectionLayer != null)
			doc.Layers.SelectionLayer.Hidden = doc.Layers.CurrentUserLayer.Hidden; // Update Visibility for SelectionLayer and force redraw

		workspace.Invalidate ();
	}

	private void OnOpacitySliderChanged (object? sender, EventArgs e)
	{
		opacity_spinner.Value = opacity_slider.GetValue ();
		UpdateOpacity ();
	}

	private void OnOpacitySpinnerChanged (object? sender, EventArgs e)
	{
		opacity_slider.SetValue (opacity_spinner.Value);
		UpdateOpacity ();
	}

	private void UpdateOpacity ()
	{
		if (initializing)
			return;

		Document doc = workspace.ActiveDocument;

		//TODO check redraws are being throttled.
		current_layer_opacity = opacity_spinner.Value / 255d;

		doc.Layers.CurrentUserLayer.Opacity = current_layer_opacity;

		if (doc.Layers.SelectionLayer != null)
			doc.Layers.SelectionLayer.Opacity = doc.Layers.CurrentUserLayer.Opacity; // Update Opacity for SelectionLayer and force redraw

		workspace.Invalidate ();
	}

	private void OnBlendModeChanged (object? sender, EventArgs e)
	{
		Document doc = workspace.ActiveDocument;

		current_layer_blend_mode = UserBlendOps.GetBlendModeByName (blend_combo_box.GetActiveText ()!);

		doc.Layers.CurrentUserLayer.BlendMode = current_layer_blend_mode;

		if (doc.Layers.SelectionLayer != null)
			doc.Layers.SelectionLayer.BlendMode = doc.Layers.CurrentUserLayer.BlendMode; //Update BlendMode for SelectionLayer and force redraw

		workspace.Invalidate ();
	}
}

