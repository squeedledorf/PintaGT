//
// SelectTool.cs
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
using System.Collections.Generic;
using Pinta.Core;

namespace Pinta.Tools;

public abstract class SelectTool : BaseTool
{
	private readonly IToolService tools;
	private readonly IWorkspaceService workspace;

	private SelectionHistoryItem? hist = default;
	private CombineMode combine_mode = default;

	// A new shape being dragged out (as opposed to a nub drag that resizes the last one).
	private bool drawing;
	private PointD draw_anchor;
	private PointD draw_start_view;

	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override bool IsSelectionTool => true;
	protected override bool ShowAntialiasingButton => false;
	private readonly RectangleHandle handle;
	public override IEnumerable<IToolHandle> Handles => [handle];

	public SelectTool (IServiceProvider services) : base (services)
	{
		tools = services.GetService<IToolService> ();
		workspace = services.GetService<IWorkspaceService> ();

		handle = new (workspace) { InvertIfNegative = true };

		workspace.SelectionChanged += AfterSelectionChange;
	}

	protected abstract void DrawShape (Document document, RectangleD r, Layer l);

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);
		workspace.SelectionHandler.BuildToolbar (tb, Settings);

		tb.Append (DrawModeSeparator);
		tb.Append (DrawModeDropDown);
		tb.Append (SizeWidthLabel);
		tb.Append (SizeWidthSpin);
		tb.Append (SizeSwapButton);
		tb.Append (SizeHeightLabel);
		tb.Append (SizeHeightSpin);
		UpdateSizeFields ();
	}

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (!handle.IsDragging && !drawing && TryDeselectOnKey (e))
			return true;

		return base.OnKeyDown (document, e);
	}

	/// <summary>
	/// As in Paint.NET, Enter in a selection tool deselects.
	/// Esc is left alone: Paint.NET 5.2's docs list only Enter and Ctrl+D for deselecting.
	/// </summary>
	internal static bool TryDeselectOnKey (ToolKeyEventArgs e)
	{
		switch (e.Key.Value) {
			case Gdk.Constants.KEY_Return:
			case Gdk.Constants.KEY_KP_Enter:
				break;
			default:
				return false;
		}

		Command deselect = PintaCore.Actions.Edit.Deselect;
		if (!deselect.Sensitive)
			return false;

		deselect.Activate ();
		return true;
	}

	private static PointD AdjustMousePosition (Document document, in PointD position)
	{
		double x = Math.Round (Math.Clamp (position.X, 0, document.ImageSize.Width));
		double y = Math.Round (Math.Clamp (position.Y, 0, document.ImageSize.Height));
		return new (x, y);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// Ignore extra button clicks while drawing
		if (handle.IsDragging || drawing)
			return;

		hist = new SelectionHistoryItem (workspace, Icon, Name);
		hist.TakeSnapshot ();

		// Hidden handles (no selection, or an inverted one) can't be grabbed.
		// A modifier or a right click asks for a new shape to combine with the selection, so it
		// never grabs the previous selection's nubs; a Fixed Size selection has no size to drag.
		bool plain_click = e.MouseButton == MouseButton.Left && !e.IsControlPressed && !e.IsAltPressed;
		if (plain_click && DrawMode != SelectionDrawMode.FixedSize && handle.Active && handle.BeginDrag (e.PointDouble, document.ImageSize))
			return;

		// Start drawing a new shape. A Width/Height still being typed is only committed on Enter or
		// focus-out, and clicking the canvas does neither, so commit it now.
		if (DrawMode != SelectionDrawMode.AnySize) {
			SizeWidthSpin.Update ();
			SizeHeightSpin.Update ();
		}

		combine_mode = PintaCore.Workspace.SelectionHandler.DetermineCombineMode (e);

		draw_anchor = AdjustMousePosition (document, e.PointDouble);
		draw_start_view = e.WindowPoint;
		drawing = true;

		document.PreviousSelection = document.Selection.Clone ();
		document.Selection.SelectionPolygons.Clear ();

		UpdateDrawnShape (document, draw_anchor, e.IsShiftPressed);
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (!handle.IsDragging && !drawing) {
			UpdateCursor (e.WindowPoint);
			return;
		}

		PointD adjusted = AdjustMousePosition (document, e.PointDouble);

		if (drawing) {
			UpdateDrawnShape (document, adjusted, e.IsShiftPressed);
		} else {
			handle.UpdateDrag (adjusted, e.IsShiftPressed);
			ReDraw (document);
			SelectionModeHandler.PerformSelectionMode (document, combine_mode, document.Selection.SelectionPolygons);
		}

		// Autoscroll is always on, as in Paint.NET.
		var view = (Gtk.Viewport) document.Workspace.Canvas.Parent!;
		var h_adjust = view.GetHadjustment ()!.PageSize;
		var v_adjust = view.GetVadjustment ()!.PageSize;

		//step of 10 pixels or of 1% of visible area, whichever is greater
		int canvasStep = (int) Math.Max (10, h_adjust * 0.01);

		PointI direction = default;
		if (e.RootPoint.X < 0)
			direction = new PointI (-canvasStep, 0); //move left
		if (e.RootPoint.Y < 0)
			direction = new PointI (0, -canvasStep); //move up
		if (e.RootPoint.X > h_adjust)
			direction = new PointI (canvasStep, 0); //move right
		if (e.RootPoint.Y > v_adjust)
			direction = new PointI (0, canvasStep); //move down

		if (direction != default)
			document.Workspace.ScrollCanvas (direction);
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (!handle.IsDragging && !drawing)
			return;

		PointD adjusted = AdjustMousePosition (document, e.PointDouble);

		bool keep;
		if (drawing) {
			// A click with Fixed Size places the box; otherwise the pointer must have moved.
			bool moved = draw_start_view.DistanceSquared (e.WindowPoint) > 1;
			keep = (moved || DrawMode == SelectionDrawMode.FixedSize) && handle.Rectangle is { Width: > 0, Height: > 0 };
		} else {
			keep = handle.HasDragged (adjusted) && handle.Rectangle.Width > 0 && handle.Rectangle.Height > 0;
		}

		if (keep) {
			ReDraw (document);

			SelectionModeHandler.PerformSelectionMode (document, combine_mode, document.Selection.SelectionPolygons);

			document.Selection.HandleBounds = handle.Rectangle;

			if (hist != null) {
				document.History.PushNewItem (hist);
				hist = null;
			}

			EndDrawing ();
		} else {
			// If the user didn't move the mouse, they want to deselect

			// Mark as being done interactive drawing before invoking the deselect action.
			// This will allow AfterSelectionChanged() to clear the selection.
			EndDrawing ();

			if (hist != null) {
				// Roll back any changes made to the selection, e.g. in OnMouseDown().
				hist.Undo ();

				hist = null;
			}

			PintaCore.Actions.Edit.Deselect.Activate ();
		}

		// Update the mouse cursor.
		UpdateCursor (e.WindowPoint);
	}

	private void EndDrawing ()
	{
		if (handle.IsDragging)
			handle.EndDrag ();
		drawing = false;
	}

	private void UpdateDrawnShape (Document document, PointD pointer, bool shift)
	{
		handle.Rectangle = ComputeDrawRectangle (DrawMode, draw_anchor, pointer, SizeWidthSpin.Value, SizeHeightSpin.Value, document.ImageSize, shift);
		ReDraw (document);
		SelectionModeHandler.PerformSelectionMode (document, combine_mode, document.Selection.SelectionPolygons);
	}

	/// <summary>
	/// The rectangle for a shape dragged from <paramref name="anchor"/> to <paramref name="pointer"/>, kept inside the image.
	/// Any Size follows the pointer (Shift makes it square). Fixed Ratio keeps width:height and grows to reach the pointer.
	/// Fixed Size is width × height with its top-left corner at the pointer. As in Paint.NET, Fixed Ratio and
	/// Fixed Size stop at the canvas edge rather than change the ratio or the size.
	/// </summary>
	public static RectangleD ComputeDrawRectangle (SelectionDrawMode mode, PointD anchor, PointD pointer, double width, double height, Size imageSize, bool shift)
	{
		if (mode == SelectionDrawMode.FixedSize) {
			double w = Math.Clamp (Math.Round (width), 1, imageSize.Width);
			double h = Math.Clamp (Math.Round (height), 1, imageSize.Height);
			double x = Math.Clamp (Math.Round (pointer.X), 0, imageSize.Width - w);
			double y = Math.Clamp (Math.Round (pointer.Y), 0, imageSize.Height - h);
			return new RectangleD (x, y, w, h);
		}

		if (mode == SelectionDrawMode.AnySize && !shift)
			return RectangleD.FromPoints (anchor, pointer, invertIfNegative: true);

		double dx = pointer.X - anchor.X;
		double dy = pointer.Y - anchor.Y;
		double ratio_w = mode == SelectionDrawMode.FixedRatio ? Math.Max (width, 1e-6) : 1;
		double ratio_h = mode == SelectionDrawMode.FixedRatio ? Math.Max (height, 1e-6) : 1;

		// Grow until the shape reaches the pointer, but stop at the canvas edge in that direction.
		double room_x = dx >= 0 ? imageSize.Width - anchor.X : anchor.X;
		double room_y = dy >= 0 ? imageSize.Height - anchor.Y : anchor.Y;
		double scale = Math.Max (Math.Abs (dx) / ratio_w, Math.Abs (dy) / ratio_h);
		scale = Math.Min (scale, Math.Min (room_x / ratio_w, room_y / ratio_h));

		double rw = Math.Min (Math.Round (scale * ratio_w), room_x);
		double rh = Math.Min (Math.Round (scale * ratio_h), room_y);
		double rx = dx >= 0 ? anchor.X : anchor.X - rw;
		double ry = dy >= 0 ? anchor.Y : anchor.Y - rh;
		return new RectangleD (rx, ry, rw, rh);
	}

	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);

		// When entering the tool, update the selection handles from the
		// document's current selection.
		if (document is null) return;

		LoadFromDocument (document);
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		workspace.SelectionHandler.OnSaveSettings (settings);

		if (draw_mode_button is null)
			return;

		StoreSizeFields ();
		settings.PutSetting (SettingPrefix + "-draw-mode", draw_mode_button.SelectedIndex);
		settings.PutSetting (SettingPrefix + "-ratio-width", ratio_size.Width);
		settings.PutSetting (SettingPrefix + "-ratio-height", ratio_size.Height);
		settings.PutSetting (SettingPrefix + "-fixed-width", fixed_size.Width);
		settings.PutSetting (SettingPrefix + "-fixed-height", fixed_size.Height);
	}

	private void ReDraw (Document document)
	{
		document.Selection.Visible = true;

		ShowHandles (true);

		RectangleD rect = handle.Rectangle;
		DrawShape (document, rect, document.Layers.SelectionLayer);
	}

	private void ShowHandles (bool visible)
	{
		handle.Active = visible;
	}

	private void UpdateCursor (PointD viewPos)
	{
		Gdk.Cursor? cursor = handle.Active ?
			handle.GetCursorAtPoint (viewPos) :
			null;

		SetCursor (cursor ?? DefaultCursor);
	}

	protected override void OnAfterUndo (Document document)
	{
		base.OnAfterUndo (document);
		LoadFromDocument (document);
	}

	protected override void OnAfterRedo (Document document)
	{
		base.OnAfterRedo (document);
		LoadFromDocument (document);
	}

	private void AfterSelectionChange (object? sender, EventArgs event_args)
	{
		if (handle.IsDragging || drawing || !workspace.HasOpenDocuments)
			return;

		// TODO: Try to remove this ActiveDocument call
		LoadFromDocument (workspace.ActiveDocument);
	}

	/// <summary>
	/// Initialize from the document's selection.
	/// </summary>
	private void LoadFromDocument (Document document)
	{
		DocumentSelection selection = document.Selection;
		handle.Rectangle = selection.HandleBounds;
		// An empty HandleBounds (e.g. after Invert Selection) has no rectangle to resize.
		ShowHandles (document.Selection.Visible && tools.CurrentTool == this && selection.HandleBounds is { Width: > 0, Height: > 0 });
	}

	// Paint.NET's draw modes: Any Size, or Fixed Ratio / Fixed Size with Width and Height fields.
	private ToolBarDropDownButton? draw_mode_button;
	private Gtk.Separator? draw_mode_sep;
	private Gtk.Label? size_width_label;
	private Gtk.Label? size_height_label;
	private Gtk.SpinButton? size_width_spin;
	private Gtk.SpinButton? size_height_spin;
	private Gtk.Button? size_swap_button;
	private (double Width, double Height) ratio_size;
	private (double Width, double Height) fixed_size;
	private SelectionDrawMode shown_size_mode = SelectionDrawMode.AnySize;

	private string SettingPrefix => GetType ().Name.ToLowerInvariant ();

	private SelectionDrawMode DrawMode => draw_mode_button?.SelectedItem.GetTagOrDefault (SelectionDrawMode.AnySize) ?? SelectionDrawMode.AnySize;

	private Gtk.Separator DrawModeSeparator => draw_mode_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Gtk.Label SizeWidthLabel => size_width_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Width")));
	private Gtk.Label SizeHeightLabel => size_height_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Height")));
	private Gtk.SpinButton SizeWidthSpin => size_width_spin ??= CreateSizeSpin ();
	private Gtk.SpinButton SizeHeightSpin => size_height_spin ??= CreateSizeSpin ();

	private ToolBarDropDownButton DrawModeDropDown {
		get {
			if (draw_mode_button is null) {
				ratio_size = (Settings.GetSetting (SettingPrefix + "-ratio-width", 4.0), Settings.GetSetting (SettingPrefix + "-ratio-height", 3.0));
				fixed_size = (Settings.GetSetting (SettingPrefix + "-fixed-width", 300.0), Settings.GetSetting (SettingPrefix + "-fixed-height", 200.0));

				draw_mode_button = ToolBarDropDownButton.New (showLabel: true);
				draw_mode_button.AddItem (Translations.GetString ("Any Size"), Pinta.Resources.Icons.SelectionDrawAnySize, SelectionDrawMode.AnySize);
				draw_mode_button.AddItem (Translations.GetString ("Fixed Ratio"), Pinta.Resources.Icons.SelectionDrawFixedRatio, SelectionDrawMode.FixedRatio);
				draw_mode_button.AddItem (Translations.GetString ("Fixed Size"), Pinta.Resources.Icons.SelectionDrawFixedSize, SelectionDrawMode.FixedSize);
				draw_mode_button.SelectedIndex = Math.Clamp (Settings.GetSetting (SettingPrefix + "-draw-mode", 0), 0, 2);
				draw_mode_button.SelectedItemChanged += (_, _) => UpdateSizeFields ();
			}

			return draw_mode_button;
		}
	}

	private Gtk.Button SizeSwapButton {
		get {
			if (size_swap_button is null) {
				size_swap_button = Gtk.Button.NewFromIconName (Pinta.Resources.StandardIcons.EditSwap);
				size_swap_button.TooltipText = Translations.GetString ("Swap width and height");
				size_swap_button.HasFrame = false;
				size_swap_button.CanFocus = false;
				size_swap_button.FocusOnClick = false;
				size_swap_button.OnClicked += (_, _) => {
					double w = SizeWidthSpin.Value;
					SizeWidthSpin.Value = SizeHeightSpin.Value;
					SizeHeightSpin.Value = w;
				};
			}

			return size_swap_button;
		}
	}

	private static Gtk.SpinButton CreateSizeSpin ()
	{
		// Integers and decimals can be typed, as in Paint.NET; whole values show without decimals.
		Gtk.SpinButton spin = GtkExtensions.CreateToolBarSpinButton (0.01, 65535, 1, 1);
		spin.Digits = 2;
		spin.WidthChars = 5;
		spin.OnOutput += (_, _) => {
			if (spin.Value != Math.Floor (spin.Value))
				return false;
			spin.SetText (spin.Value.ToString ("0"));
			return true;
		};
		return spin;
	}

	/// <summary>
	/// Shows the Width/Height fields for Fixed Ratio and Fixed Size; each mode keeps its own values.
	/// </summary>
	private void UpdateSizeFields ()
	{
		StoreSizeFields ();

		SelectionDrawMode mode = DrawMode;
		if (mode == SelectionDrawMode.FixedRatio)
			(SizeWidthSpin.Value, SizeHeightSpin.Value) = ratio_size;
		else if (mode == SelectionDrawMode.FixedSize)
			(SizeWidthSpin.Value, SizeHeightSpin.Value) = fixed_size;
		shown_size_mode = mode;

		bool visible = mode != SelectionDrawMode.AnySize;
		SizeWidthLabel.Visible = visible;
		SizeWidthSpin.Visible = visible;
		SizeSwapButton.Visible = visible;
		SizeHeightLabel.Visible = visible;
		SizeHeightSpin.Visible = visible;
	}

	private void StoreSizeFields ()
	{
		if (shown_size_mode == SelectionDrawMode.FixedRatio)
			ratio_size = (SizeWidthSpin.Value, SizeHeightSpin.Value);
		else if (shown_size_mode == SelectionDrawMode.FixedSize)
			fixed_size = (SizeWidthSpin.Value, SizeHeightSpin.Value);
	}
}

public enum SelectionDrawMode
{
	AnySize,
	FixedRatio,
	FixedSize,
}
