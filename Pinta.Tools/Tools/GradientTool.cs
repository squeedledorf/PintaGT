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

		selected_type = (GradientType) Math.Clamp (Settings.GetSetting (SettingNames.GRADIENT_TYPE, 0), 0, (int) GradientType.SpiralCounterclockwise);
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
	private GradientType SelectedGradientType => selected_type;
	private GradientRepeatMode SelectedRepeatMode => (GradientRepeatMode) RepeatPicker.SelectedIndex;
	private GradientColorMode SelectedGradientColorMode => ColorModeDropDown.SelectedItem.GetTagOrDefault (GradientColorMode.Color);
	public override IEnumerable<IToolHandle> Handles => [handle];

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		// Paint.NET: the seven gradient types as a row of toggle buttons, then the colour and repeat mode dropdowns.
		foreach (Gtk.ToggleButton button in TypeButtons)
			tb.Append (button);
		tb.Append (TypeSeparator);
		tb.Append (ColorModeDropDown);
		tb.Append (RepeatPicker.Button);
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

		// Paint.NET names every step of a gradient plainly "Gradient".
		if (undo_surface != null) {
			document.History.PushNewItem (new GradientHistoryItem (Icon, Name, undo_surface,
				document.Layers.CurrentUserLayerIndex, undo_data!.Value, this));
		}
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

		if (type_buttons is not null)
			settings.PutSetting (SettingNames.GRADIENT_TYPE, (int) selected_type);
		if (repeat_picker is not null)
			settings.PutSetting (REPEAT_MODE_SETTING, repeat_picker.SelectedIndex);
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

		// The pixels are already on the layer, so finishing adds no history item. Undoing the last
		// "Gradient" item still goes back to before it, and redoing it brings the handles back.
		handle.Active = false;
		base_surface = null;
		document?.Workspace.Invalidate ();

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
		GradientRenderer renderer = CreateGradientRenderer (SelectedGradientType, SelectedGradientColorMode == GradientColorMode.Transparency);
		renderer.RepeatMode = SelectedRepeatMode;
		return renderer;
	}

	private static GradientRenderer CreateGradientRenderer (GradientType type, bool alpha_only)
	{
		var op = new UserBlendOps.NormalBlendOp ();

		return type switch {
			GradientType.Linear => new GradientRenderers.LinearClamped (alpha_only, op),
			GradientType.LinearReflected => new GradientRenderers.LinearReflected (alpha_only, op),
			GradientType.Radial => new GradientRenderers.Radial (alpha_only, op),
			GradientType.Diamond => new GradientRenderers.LinearDiamond (alpha_only, op),
			GradientType.Conical => new GradientRenderers.Conical (alpha_only, op),
			GradientType.SpiralClockwise => new GradientRenderers.Spiral (clockwise: true, alpha_only, op),
			GradientType.SpiralCounterclockwise => new GradientRenderers.Spiral (clockwise: false, alpha_only, op),
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

	private const string REPEAT_MODE_SETTING = "gradient-repeat-mode";

	private GradientType selected_type;
	private Gtk.ToggleButton[]? type_buttons;
	private Gtk.Separator? type_sep;
	private GlyphPicker? repeat_picker;
	private ToolBarDropDownButton? color_mode_button;

	private Gtk.Separator TypeSeparator => type_sep ??= GtkExtensions.CreateToolBarSeparator ();

	private Gtk.ToggleButton[] TypeButtons {
		get {
			if (type_buttons is not null)
				return type_buttons;

			(GradientType Type, string Name)[] types = [
				(GradientType.Linear, Translations.GetString ("Linear Gradient")),
				(GradientType.LinearReflected, Translations.GetString ("Linear Reflected Gradient")),
				(GradientType.Diamond, Translations.GetString ("Linear Diamond Gradient")),
				(GradientType.Radial, Translations.GetString ("Radial Gradient")),
				(GradientType.Conical, Translations.GetString ("Conical Gradient")),
				(GradientType.SpiralClockwise, Translations.GetString ("Spiral Gradient (Clockwise)")),
				(GradientType.SpiralCounterclockwise, Translations.GetString ("Spiral Gradient (Counterclockwise)")),
			];

			type_buttons = new Gtk.ToggleButton[types.Length];
			Gtk.ToggleButton? group = null;

			for (int i = 0; i < types.Length; i++) {
				GradientType type = types[i].Type;
				Gtk.ToggleButton button = Gtk.ToggleButton.New ();
				button.Child = Gtk.Image.NewFromPaintable (CreateTypeGlyph (type));
				button.TooltipText = types[i].Name;
				button.HasFrame = false;
				button.CanFocus = false;
				button.FocusOnClick = false;
				if (group is null)
					group = button;
				else
					button.SetGroup (group);
				button.Active = type == selected_type;
				button.OnToggled += (_, _) => {
					if (!button.Active)
						return;
					selected_type = type;
					HandleGradientTypeChanged (button, EventArgs.Empty);
				};
				type_buttons[i] = button;
			}

			return type_buttons;
		}
	}

	/// <summary>A framed black-to-white preview of the gradient type, drawn by its own renderer.</summary>
	private static Gdk.Texture CreateTypeGlyph (GradientType type)
	{
		const int size = 16;
		bool spiral = type is GradientType.SpiralClockwise or GradientType.SpiralCounterclockwise;
		GradientRenderer renderer = CreateGradientRenderer (type, alpha_only: false);
		renderer.StartColor = ColorBgra.Black;
		renderer.EndColor = ColorBgra.White;
		renderer.StartPoint = new PointD (type == GradientType.Linear ? 1 : size / 2, size / 2);
		renderer.EndPoint = spiral ? new PointD (size / 2, 1) : new PointD (size - 2, size / 2);
		renderer.RepeatMode = spiral ? GradientRepeatMode.RepeatWrapped : GradientRepeatMode.NoRepeat;
		renderer.BeforeRender ();

		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, size, size);
		renderer.Render (surface, [new RectangleI (1, 1, size - 3, size - 3)]);

		using (Context g = new (surface)) {
			g.Rectangle (0.5, 0.5, size - 1, size - 1);
			g.SetSourceColor (new Color (0.45, 0.45, 0.45));
			g.LineWidth = 1;
			g.Stroke ();
		}

		return surface.ToTexture ();
	}

	private GlyphPicker RepeatPicker {
		get {
			if (repeat_picker is null) {
				repeat_picker = new GlyphPicker ([
					new (Translations.GetString ("No Repeat"), CreateRepeatGlyph (GradientRepeatMode.NoRepeat)),
					new (Translations.GetString ("Repeat Wrapped"), CreateRepeatGlyph (GradientRepeatMode.RepeatWrapped)),
					new (Translations.GetString ("Repeat Reflected"), CreateRepeatGlyph (GradientRepeatMode.RepeatReflected)),
				], columns: 1, showNameOnButton: true, showNamesInList: true);

				repeat_picker.SelectedIndex = Settings.GetSetting (REPEAT_MODE_SETTING, 0);
				repeat_picker.Changed += HandleGradientTypeChanged;
			}

			return repeat_picker;
		}
	}

	/// <summary>The gradient's profile: a ramp that levels off, a sawtooth, or a triangle wave.</summary>
	private static Gdk.Texture CreateRepeatGlyph (GradientRepeatMode mode)
		=> GlyphPicker.CreateGlyph (16, 16, g => {
			PointD[] points = mode switch {
				GradientRepeatMode.RepeatWrapped => [new (2, 14), new (7, 2), new (8, 14), new (14, 2)],
				GradientRepeatMode.RepeatReflected => [new (2, 14), new (8, 2), new (14, 14)],
				_ => [new (2, 14), new (4, 14), new (12, 2), new (14, 2)],
			};
			g.MoveTo (points[0].X, points[0].Y);
			foreach (PointD p in points[1..])
				g.LineTo (p.X, p.Y);
			g.SetSourceColor (GlyphPicker.GlyphStroke);
			g.LineWidth = 1.5;
			g.LineJoin = LineJoin.Round;
			g.Stroke ();
		});

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
		Conical,
		SpiralClockwise,
		SpiralCounterclockwise,
	}
}
