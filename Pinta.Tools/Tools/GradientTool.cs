//
// GradientTool.cs
//
// Author:
//       Olivier Dufour <olivier.duff@gmail.com>
//
// Copyright (c) 2010 Olivier Dufour
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
using System.Collections.Immutable;
using System.Linq;
using Cairo;
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class GradientTool : BaseTool
{
	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;

	private ImageSurface? undo_surface;
	private GradientData? undo_data;

	// The layer as it was before the live gradient was drawn, which every redraw blends onto.
	private ImageSurface? base_surface;

	private bool is_newly_created = false;

	public bool is_reversed = false;
	MouseButton drag_button;

	// The fixed end of the line while a handle is dragged, used for Shift angle snapping.
	private PointD drag_anchor;
	private const double SNAP_ANGLE_STEP = Math.PI / 12; // 15 degrees

	public LineHandle handle;

	public GradientTool (IServiceProvider services) : base (services)
	{
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();

		handle = new LineHandle (workspace);
	}

	public override string Name => Translations.GetString ("Gradient");
	public override string Icon => Pinta.Resources.Icons.ToolGradient;
	public override string StatusBarText => Translations.GetString ("Click and drag to draw gradient from primary to secondary color." +
									"\nRight click to reverse." +
									"\nClick on a control point and drag to move it." +
									"\nRight click on a control point to swap the colors." +
									"\nHold Shift to snap the angle to 15 degrees.");
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_G);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.Gradient.png"), 9, 18, null);
	public override int Priority => 19;
	protected override bool ShowBlendModeButton => true;
	protected override bool ShowSelectionQualityButton => true;
	protected override bool ShowFinishButton => true;
	protected override bool CanFinish => handle.Active;
	private GradientType SelectedGradientType => GradientDropDown.SelectedItem.GetTagOrDefault (GradientType.Linear);
	private GradientColorMode SelectedGradientColorMode => ColorModeDropDown.SelectedItem.GetTagOrDefault (GradientColorMode.Color);
	public override IEnumerable<IToolHandle> Handles => [handle];

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (GradientLabel);
		tb.Append (GradientDropDown);
		tb.Append (GtkExtensions.CreateToolBarSeparator ());
		tb.Append (ModeLabel);
		tb.Append (ColorModeDropDown);
	}

	protected override void OnBlendModeChanged ()
	{
		if (handle.Active)
			RenderGradient ();
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		if (handle.IsDragging)
			return;

		undo_data = this.Data;
		undo_surface = document.Layers.CurrentUserLayer.Surface.Clone ();

		if (handle.BeginDrag (e.PointDouble)) {
			SetCursor (DefaultCursor);
			drag_button = e.MouseButton;

			// The handle nearer the click is the one being dragged.
			bool dragging_start = e.PointDouble.Distance (handle.StartPosition) <= e.PointDouble.Distance (handle.EndPosition);
			drag_anchor = dragging_start ? handle.EndPosition : handle.StartPosition;

			// Right click on a handle swaps the colors.
			if (e.MouseButton == MouseButton.Right) {
				is_reversed = !is_reversed;
				RenderGradient ();
			}

			return;
		}

		drag_anchor = e.PointDouble;

		RectangleI handleDirtyRegion = handle.StartNewLine (e.PointDouble);
		document.Workspace.InvalidateWindowRect (handleDirtyRegion);

		is_reversed = e.MouseButton == MouseButton.Right;

		is_newly_created = true;
		base_surface = undo_surface;
		drag_button = e.MouseButton;

		palette.PrimaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;

		palette.PrimaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (!handle.IsDragging || e.MouseButton != drag_button)
			return;

		handle.EndDrag ();
		UpdateCursorAndHandle (e.PointDouble, document);

		document.Layers.ToolLayer.Clear ();

		if (undo_surface != null) {
			string name = is_newly_created
				? Translations.GetString ("Gradient Created")
				: Translations.GetString ("Gradient Modified");
			document.History.PushNewItem (new GradientHistoryItem (Icon, name, undo_surface,
				document.Layers.CurrentUserLayerIndex, undo_data!.Value, this));
		}

		is_newly_created = false;
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (!handle.IsDragging) {
			UpdateCursorAndHandle (e.PointDouble, document);
			return;
		}

		PointD point = e.IsShiftPressed ? SnapAngle (drag_anchor, e.PointDouble) : e.PointDouble;
		RectangleI handleDirtyRegion = handle.Drag (point);
		document.Workspace.InvalidateWindowRect (handleDirtyRegion);

		RenderGradient ();
	}

	private static PointD SnapAngle (PointD anchor, PointD point)
	{
		double dx = point.X - anchor.X;
		double dy = point.Y - anchor.Y;
		double length = Math.Sqrt (dx * dx + dy * dy);
		double angle = Math.Round (Math.Atan2 (dy, dx) / SNAP_ANGLE_STEP) * SNAP_ANGLE_STEP;
		return new PointD (anchor.X + length * Math.Cos (angle), anchor.Y + length * Math.Sin (angle));
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (e.Key.Value == Gdk.Constants.KEY_Return) {
			Finalize (document);
		}
		return base.OnKeyDown (document, e);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (gradient_button is not null)
			settings.PutSetting (SettingNames.GRADIENT_TYPE, gradient_button.SelectedIndex);
		if (color_mode_button is not null)
			settings.PutSetting (SettingNames.GRADIENT_COLOR_MODE, color_mode_button.SelectedIndex);
	}

	protected override void OnCommit (Document? document)
	{
		Finalize (document);
		base.OnCommit (document);
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		Finalize (document);
		base.OnDeactivated (document, newTool);
	}

	private void Finalize (Document? document)
	{
		if (!handle.Active) { return; }

		if (document != null) {
			undo_data = Data;
			undo_surface = document.Layers.CurrentUserLayer.Surface.Clone ();
			document.History.PushNewItem (new GradientHistoryItem (Icon, Name + " " + Translations.GetString ("Finalized"), undo_surface,
						document.Layers.CurrentUserLayerIndex, undo_data!.Value, this));
		}
		handle.Active = false;
		base_surface = null;

		palette.PrimaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
	}

	private void UpdateCursorAndHandle (PointD canvasPoint, Document document)
	{
		Gdk.Cursor? cursor = handle.UpdateHoverHandle (canvasPoint, out RectangleI handleDirtyRegion);
		SetCursor (cursor ?? DefaultCursor);
		document.Workspace.InvalidateWindowRect (handleDirtyRegion);
	}

	private GradientRenderer CreateGradientRenderer ()
	{
		var op = new UserBlendOps.NormalBlendOp ();
		bool alpha_only = SelectedGradientColorMode == GradientColorMode.Transparency;

		return SelectedGradientType switch {
			GradientType.Linear => new GradientRenderers.LinearClamped (alpha_only, op),
			GradientType.LinearReflected => new GradientRenderers.LinearReflected (alpha_only, op),
			GradientType.Radial => new GradientRenderers.Radial (alpha_only, op),
			GradientType.Diamond => new GradientRenderers.LinearDiamond (alpha_only, op),
			GradientType.Conical => new GradientRenderers.Conical (alpha_only, op),
			_ => throw new InvalidOperationException ("Unknown gradient type."),
		};
	}

	private void RenderGradient ()
	{
		Document document = workspace.ActiveDocument;

		var gr = CreateGradientRenderer ();

		gr.Reversed = is_reversed;

		if (is_reversed && !gr.AlphaOnly) {
			gr.StartColor = palette.SecondaryColor.ToColorBgra ();
			gr.EndColor = palette.PrimaryColor.ToColorBgra ();
		} else {
			gr.StartColor = palette.PrimaryColor.ToColorBgra ();
			gr.EndColor = palette.SecondaryColor.ToColorBgra ();
		}

		gr.StartPoint = handle.StartPosition;
		gr.EndPoint = handle.EndPosition;
		// A blend mode other than Normal renders the bare gradient, then blends it onto the layer below.
		BlendMode blend_mode = SelectedBlendMode;
		bool use_blend_mode = !gr.AlphaOnly && UseAlphaBlending && blend_mode != BlendMode.Normal;
		gr.AlphaBlending = UseAlphaBlending && !use_blend_mode;

		gr.BeforeRender ();

		var selection_bounds = document.GetSelectedBounds (true);
		var scratch_layer = document.Layers.ToolLayer.Surface;
		document.Layers.ToolLayer.Hidden = true;
		ImageSurface original = base_surface ?? undo_surface!;

		// Initialize the scratch layer with the (original) current layer, if any blending is required.
		if (gr.AlphaOnly || (gr.AlphaBlending && (gr.StartColor.A != 255 || gr.EndColor.A != 255))) {
			using Context g = new (scratch_layer);
			document.Selection.Clip (g);
			g.SetSourceSurface (original, 0, 0);
			g.Operator = Operator.Source;
			g.Paint ();
		}

		ReadOnlySpan<RectangleI> selection_bounds_array = [selection_bounds];
		gr.Render (scratch_layer, selection_bounds_array);

		// Transfer the result back to the current layer.
		using Context context = document.CreateClippedContext ();

		if (use_blend_mode) {
			context.SetSourceSurface (original, 0, 0);
			context.Operator = Operator.Source;
			context.Paint ();
			context.BlendSurface (scratch_layer, blend_mode);
		} else {
			context.SetSourceSurface (scratch_layer, 0, 0);
			context.Operator = Operator.Source;
			context.Paint ();
		}

		selection_bounds = selection_bounds.Inflated (5, 5);
		document.Workspace.Invalidate (selection_bounds);
	}

	public GradientData Data {
		get {
			return new GradientData (
				handle.StartPosition,
				handle.EndPosition,
				handle.Active,
				this.is_reversed
			);
		}

		set {
			this.is_reversed = value.IsReversed;
			handle.ApplyData (
				value.StartPosition,
				value.EndPosition,
				value.Active
				);
		}
	}

	private Gtk.Label? gradient_label;
	private ToolBarDropDownButton? gradient_button;
	private Gtk.Label? color_mode_label;
	private ToolBarDropDownButton? color_mode_button;

	private Gtk.Label GradientLabel => gradient_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Gradient")));
	private ToolBarDropDownButton GradientDropDown {
		get {
			if (gradient_button == null) {
				gradient_button = ToolBarDropDownButton.New ();

				gradient_button.AddItem (Translations.GetString ("Linear Gradient"), Pinta.Resources.Icons.GradientLinear, GradientType.Linear);
				gradient_button.AddItem (Translations.GetString ("Linear Reflected Gradient"), Pinta.Resources.Icons.GradientLinearReflected, GradientType.LinearReflected);
				gradient_button.AddItem (Translations.GetString ("Linear Diamond Gradient"), Pinta.Resources.Icons.GradientDiamond, GradientType.Diamond);
				gradient_button.AddItem (Translations.GetString ("Radial Gradient"), Pinta.Resources.Icons.GradientRadial, GradientType.Radial);
				gradient_button.AddItem (Translations.GetString ("Conical Gradient"), Pinta.Resources.Icons.GradientConical, GradientType.Conical);

				gradient_button.SelectedIndex = Settings.GetSetting (SettingNames.GRADIENT_TYPE, 0);
				gradient_button.SelectedItemChanged += HandleGradientTypeChanged;
			}

			return gradient_button;
		}
	}

	void HandleGradientTypeChanged (object? sender, EventArgs e)
	{
		if (handle.Active) {
			RenderGradient ();
		}
	}

	void HandlePintaCorePalettePrimaryColorChanged (object? sender, EventArgs e)
	{
		if (handle.Active) {
			RenderGradient ();
		}
	}

	private Gtk.Label ModeLabel => color_mode_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Mode")));
	private ToolBarDropDownButton ColorModeDropDown {
		get {
			if (color_mode_button == null) {
				color_mode_button = ToolBarDropDownButton.New ();

				color_mode_button.AddItem (Translations.GetString ("Color Mode"), Pinta.Resources.Icons.ColorModeColor, GradientColorMode.Color);
				color_mode_button.AddItem (Translations.GetString ("Transparency Mode"), Pinta.Resources.Icons.ColorModeTransparency, GradientColorMode.Transparency);

				color_mode_button.SelectedIndex = Settings.GetSetting (SettingNames.GRADIENT_COLOR_MODE, 0);
				color_mode_button.SelectedItemChanged += HandleGradientTypeChanged;
			}

			return color_mode_button;
		}
	}

	enum GradientType
	{
		Linear,
		LinearReflected,
		Diamond,
		Radial,
		Conical
	}
}
