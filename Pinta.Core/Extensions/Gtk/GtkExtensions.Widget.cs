//
// GtkExtensions.cs
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
using System.Linq;
using Pinta.Resources;

namespace Pinta.Core;

/// <summary>
/// Style classes from libadwaita.
/// https://gnome.pages.gitlab.gnome.org/libadwaita/doc/1-latest/style-classes.html
/// </summary>
public static class AdwaitaStyles
{
	public const string Body = "body";
	public const string Compact = "compact";
	public const string DestructiveAction = "destructive-action";
	public const string DimLabel = "dim-label";
	public const string Error = "error";
	public const string Flat = "flat";
	public const string Heading = "heading";
	public const string Inline = "inline";
	public const string Linked = "linked";
	public const string Osd = "osd";
	public const string Spacer = "spacer";
	public const string SuggestedAction = "suggested-action";
	public const string Title4 = "title-4";
	public const string Toolbar = "toolbar";
	public const string Warning = "warning";
};


partial class GtkExtensions
{
	public static Gtk.Box BoxHorizontal (ReadOnlySpan<Gtk.Widget> children)
		=> Box (BoxStyle.Horizontal, children);

	public static Gtk.Box BoxVertical (ReadOnlySpan<Gtk.Widget> children)
		=> Box (BoxStyle.Vertical, children);

	public static Gtk.Box Box (
		BoxStyle style,
		ReadOnlySpan<Gtk.Widget> children) // TODO: Add 'params' keyword when updated to C#13
	{
		Gtk.Box stack = Gtk.Box.New (style.Orientation, style.Spacing ?? 0);

		// --- Optional
		if (style.CssClass is not null) stack.AddCssClass (style.CssClass);

		stack.AppendMultiple (children);

		return stack;
	}

	public static void AppendMultiple (
		this Gtk.Box box,
		ReadOnlySpan<Gtk.Widget> children) // TODO: Add 'params' keyword when updated to C#13
	{
		foreach (var child in children)
			box.Append (child);
	}

	/// <summary>
	/// In GTK4, toolbars are just a Box with a different CSS style class.
	/// </summary>
	public static Gtk.Box CreateToolBar ()
	{
		Gtk.Box toolbar = Gtk.Box.New (Gtk.Orientation.Horizontal, spacing: 0);
		toolbar.AddCssClass (AdwaitaStyles.Toolbar);
		return toolbar;
	}

	/// <summary>
	/// Remove all child widgets from a box.
	/// </summary>
	public static void RemoveAll (this Gtk.Box box)
	{
		while (box.GetFirstChild () is Gtk.Widget child)
			box.Remove (child);
	}

	private static readonly string shortcut_label = Translations.GetString ("Shortcut key");
	private static readonly string shortcuts_label = Translations.GetString ("Shortcut keys");

	public static Gtk.Button CreateToolBarItem (this Command action, bool force_icon_only = false)
	{
		string label = action.ShortLabel ?? action.Label;

		string baseTooltip = action.Tooltip ?? action.Label;

		string fullTooltip = action.Shortcuts.Length switch {
			0 => baseTooltip,
			1 => $"{baseTooltip}\n{shortcut_label}: {ReadableAcceleratorLabel (action.Shortcuts[0])}",
			_ => $"{baseTooltip}\n{shortcuts_label}:\n" + string.Join ('\n', action.Shortcuts.Select (s => $"- {ReadableAcceleratorLabel (s)}")),
		};

		Gtk.Button button = Gtk.Button.New ();
		button.ActionName = action.FullName;
		button.TooltipText = fullTooltip;

		if (action.IsImportant && !force_icon_only) {
			Adw.ButtonContent buttonContent = Adw.ButtonContent.New ();
			buttonContent.IconName = action.IconName;
			buttonContent.Label = label;
			button.Child = buttonContent;
		} else {
			button.Label = label;
			button.IconName = action.IconName;
		}

		return button;
	}

	public static Gtk.Button CreateDockToolBarItem (this Command action)
	{
		return action.CreateToolBarItem (force_icon_only: false);
	}

	public static Gtk.Separator CreateToolBarSeparator ()
	{
		// A thin vertical line between groups, as in Paint.NET's toolbars.
		return Gtk.Separator.New (Gtk.Orientation.Vertical);
	}

	public static Gtk.SpinButton CreateToolBarSpinButton (
		double min,
		double max,
		double step,
		double init_value)
	{
		Gtk.SpinButton spin = Gtk.SpinButton.NewWithRange (min, max, step);
		spin.FocusOnClick = false;
		spin.Value = init_value;
		// After a spin button is edited, return focus to the canvas so that
		// tools can handle subsequent key events.
		spin.OnValueChanged += (o, e) => {
			if (!PintaCore.Workspace.HasOpenDocuments) return;
			PintaCore.Workspace.ActiveWorkspace.GrabFocusToCanvas ();
		};
		return spin;
	}

	/// <summary>
	/// Creates a Gtk.MenuButton with the popover configured to show regular nested
	/// menus rather than sliding menus.
	/// </summary>
	public static Gtk.MenuButton CreateMenuButton (Gio.MenuModel model, string iconName, string? tooltip = null)
	{
		Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModelFull (model, Gtk.PopoverMenuFlags.Nested);

		Gtk.MenuButton menu = Gtk.MenuButton.New ();
		menu.Popover = popover;
		menu.IconName = iconName;
		menu.TooltipText = tooltip;

		return menu;
	}

	/// <summary>
	/// Paint.NET's percent bar (Hardness, Spacing, Tolerance...): "NN%" inside a filled bar, with − and + buttons.
	/// </summary>
	public static ToolBarSlider CreateToolBarSlider (
		int min,
		int max,
		int step,
		int val,
		double curve = 1)
	{
		return ToolBarSlider.New (min, max, step, val, curve);
	}

	/// <summary>
	/// Puts a spin button's − and + outside its field, as Paint.NET's tool bar does for Brush size.
	/// </summary>
	public static Gtk.Box WithOuterStepButtons (this Gtk.SpinButton spin)
	{
		// The spin button's own step buttons are its Gtk.Button children.
		for (Gtk.Widget? child = spin.GetFirstChild (); child is not null; child = child.GetNextSibling ())
			if (child is Gtk.Button)
				child.Visible = false;

		Gtk.Button Step (string icon, Gtk.SpinType direction)
		{
			Gtk.Button button = Gtk.Button.NewFromIconName (icon);
			button.AddCssClass (AdwaitaStyles.Flat);
			button.FocusOnClick = false;
			button.CanFocus = false;
			button.Valign = Gtk.Align.Center;
			button.OnClicked += (_, _) => spin.Spin (direction, 0);
			return button;
		}

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 2);
		box.Append (Step ("list-remove-symbolic", Gtk.SpinType.StepBackward));
		box.Append (spin);
		box.Append (Step ("list-add-symbolic", Gtk.SpinType.StepForward));

		// Hiding the spin button hides its step buttons too.
		spin.BindProperty (
			Gtk.Widget.VisiblePropertyDefinition.UnmanagedName,
			box,
			Gtk.Widget.VisiblePropertyDefinition.UnmanagedName,
			GObject.BindingFlags.SyncCreate);
		return box;
	}

	private const string STACKED_SPIN_CLASS = "pdn-stacked-spin";
	private const string STACKED_STEP_CLASS = "pdn-spin-step";
	private const string STACKED_STEP_PRESSED_CLASS = "pdn-spin-step-pressed";
	private const string RESET_BUTTON_CLASS = "pdn-reset-button";
	private static bool dialog_control_style_loaded;

	/// <summary>
	/// Gives a dialog spin button Paint.NET's (Win32) look: the digits right-aligned and two small
	/// arrow cells stacked at the right end of the field, instead of GTK's side-by-side buttons.
	/// Pack the returned box where the spin button would go.
	/// </summary>
	public static Gtk.Box WithStackedStepButtons (this Gtk.SpinButton spin)
	{
		EnsureDialogControlStyle ();

		for (Gtk.Widget? child = spin.GetFirstChild (); child is not null; child = child.GetNextSibling ())
			if (child is Gtk.Button)
				child.Visible = false;

		Gtk.Box steps = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		steps.Append (StackedStep (spin, "pinta-pan-up-symbolic", Gtk.SpinType.StepForward));
		steps.Append (StackedStep (spin, "pinta-pan-down-symbolic", Gtk.SpinType.StepBackward));

		// The box takes over the spin button's place in the layout; any extra width goes to the digits.
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		box.AddCssClass (STACKED_SPIN_CLASS);
		box.Halign = spin.Halign;
		box.Valign = spin.Valign;
		box.Hexpand = spin.Hexpand;
		box.Vexpand = spin.Vexpand;
		box.MarginStart = spin.MarginStart;
		box.MarginEnd = spin.MarginEnd;
		box.MarginTop = spin.MarginTop;
		box.MarginBottom = spin.MarginBottom;
		spin.Halign = spin.Valign = Gtk.Align.Fill;
		spin.Hexpand = true;
		spin.Vexpand = false;
		spin.MarginStart = spin.MarginEnd = spin.MarginTop = spin.MarginBottom = 0;
		spin.Xalign = 1;
		box.Append (spin);
		box.Append (steps);

		// Hiding or disabling the spin button does the same to its arrows.
		foreach (string property in new[] { Gtk.Widget.VisiblePropertyDefinition.UnmanagedName, Gtk.Widget.SensitivePropertyDefinition.UnmanagedName })
			spin.BindProperty (property, box, property, GObject.BindingFlags.SyncCreate);
		return box;
	}

	/// <summary>
	/// One arrow cell. Like a Win32 up-down control it steps on press and repeats while held.
	/// </summary>
	private static Gtk.Widget StackedStep (Gtk.SpinButton spin, string icon, Gtk.SpinType direction)
	{
		Gtk.Image arrow = Gtk.Image.NewFromIconName (icon);
		arrow.PixelSize = 7;
		arrow.Vexpand = true;
		arrow.AddCssClass (STACKED_STEP_CLASS);

		uint delay = 0, repeat = 0;
		void Stop ()
		{
			if (delay != 0) GLib.Functions.SourceRemove (delay);
			if (repeat != 0) GLib.Functions.SourceRemove (repeat);
			delay = repeat = 0;
			arrow.RemoveCssClass (STACKED_STEP_PRESSED_CLASS);
		}

		Gtk.GestureClick click = Gtk.GestureClick.New ();
		click.OnPressed += (_, _) => {
			Stop ();
			arrow.AddCssClass (STACKED_STEP_PRESSED_CLASS);
			spin.Update (); // Step from the typed text, as GTK's own buttons do.
			spin.Spin (direction, 0);
			delay = GLib.Functions.TimeoutAdd (GLib.Constants.PRIORITY_DEFAULT, 400, () => {
				delay = 0;
				repeat = GLib.Functions.TimeoutAdd (GLib.Constants.PRIORITY_DEFAULT, 50, () => {
					// "stopped" fires at the double-click timeout, so poll for the button still being held.
					if (!click.IsActive ()) {
						repeat = 0;
						Stop ();
						return false;
					}
					spin.Spin (direction, 0);
					return true;
				});
				return false;
			});
		};
		click.OnReleased += (_, _) => Stop ();
		click.OnCancel += (_, _) => Stop ();
		arrow.AddController (click);
		arrow.OnUnmap += (_, _) => Stop ();
		return arrow;
	}

	/// <summary>
	/// The reset arrow beside an effect dialog's slider, angle or point: a small framed square, as in Paint.NET.
	/// </summary>
	public static Gtk.Button CreateResetButton ()
	{
		EnsureDialogControlStyle ();
		Gtk.Button button = Gtk.Button.NewFromIconName (StandardIcons.EditUndo);
		button.AddCssClass (RESET_BUTTON_CLASS);
		button.TooltipText = Translations.GetString ("Reset");
		button.Valign = Gtk.Align.Center;
		return button;
	}

	/// <summary>
	/// Stacked spin buttons: the field frame moves from the spin button to the box that also holds the arrows.
	/// Reset buttons: framed.
	/// ponytail: loaded from code to keep style.css untouched by this package; it can move there.
	/// </summary>
	private static void EnsureDialogControlStyle ()
	{
		if (dialog_control_style_loaded)
			return;

		Gdk.Display? display = Gdk.Display.GetDefault ();
		if (display is null)
			return;

		Gtk.CssProvider provider = Gtk.CssProvider.New ();
		provider.LoadFromString ($$"""
			.{{STACKED_SPIN_CLASS}} {
				min-height: 21px;
				background-color: @view_bg_color;
				box-shadow: inset 0 0 0 1px alpha(@view_fg_color, 0.4);
			}
			.{{STACKED_SPIN_CLASS}}:hover { box-shadow: inset 0 0 0 1px alpha(@view_fg_color, 0.7); }
			.{{STACKED_SPIN_CLASS}}:focus-within { box-shadow: inset 0 0 0 1px #0078d7; }
			.{{STACKED_SPIN_CLASS}}:disabled {
				background-color: mix(@view_bg_color, @view_fg_color, 0.04);
				box-shadow: inset 0 0 0 1px alpha(@view_fg_color, 0.2);
			}
			.{{STACKED_SPIN_CLASS}} > spinbutton,
			.{{STACKED_SPIN_CLASS}} > spinbutton:hover,
			.{{STACKED_SPIN_CLASS}} > spinbutton:focus-within,
			.{{STACKED_SPIN_CLASS}} > spinbutton:disabled {
				min-height: 21px;
				border-radius: 0;
				background: none;
				box-shadow: none;
				outline: none;
			}
			.{{STACKED_SPIN_CLASS}} > box { margin: 1px 1px 1px 0; }
			.{{STACKED_SPIN_CLASS}} image.{{STACKED_STEP_CLASS}} {
				min-width: 15px;
				min-height: 0;
				color: alpha(@view_fg_color, 0.8);
			}
			.{{STACKED_SPIN_CLASS}} image.{{STACKED_STEP_CLASS}}:hover { background-color: alpha(#0078d7, 0.15); }
			.{{STACKED_SPIN_CLASS}} image.{{STACKED_STEP_PRESSED_CLASS}} { background-color: alpha(#0078d7, 0.3); }
			.{{STACKED_SPIN_CLASS}}:disabled image.{{STACKED_STEP_CLASS}} { opacity: 0.35; }
			button.image-button.{{RESET_BUTTON_CLASS}} {
				min-width: 21px;
				min-height: 21px;
				padding: 0;
				border-radius: 0;
				background-color: mix(@view_bg_color, @view_fg_color, 0.08);
				box-shadow: inset 0 0 0 1px alpha(@view_fg_color, 0.35);
			}
			button.image-button.{{RESET_BUTTON_CLASS}}:hover {
				background-color: alpha(#0078d7, 0.1);
				box-shadow: inset 0 0 0 1px #0078d7;
			}
			button.image-button.{{RESET_BUTTON_CLASS}}:active { background-color: alpha(#0078d7, 0.22); }
			button.image-button.{{RESET_BUTTON_CLASS}}:focus-visible { box-shadow: inset 0 0 0 2px #0078d7; }
			button.image-button.{{RESET_BUTTON_CLASS}} > image { color: #2a6fc9; }
			""");
		// One above style.css, so these win over its "window.dialog spinbutton" rules.
		Gtk.StyleContext.AddProviderForDisplay (display, provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
		dialog_control_style_loaded = true;
	}

	public static void Toggle (this Gtk.ToggleButton button)
	{
		button.Active = !button.Active;
	}


	/// <summary>
	/// Adds the OK / Cancel button pair, with OK first on every platform (as in Paint.NET).
	/// This can be used with the Gtk.Dialog constructor.
	/// </summary>
	public static void AddCancelOkButtons (this Gtk.Dialog dialog)
	{
		Gtk.Widget ok_button = dialog.AddButton (Translations.GetString ("_OK"), (int) Gtk.ResponseType.Ok);
		dialog.AddButton (Translations.GetString ("_Cancel"), (int) Gtk.ResponseType.Cancel);

		ok_button.AddCssClass (AdwaitaStyles.SuggestedAction);
	}

	/// <summary>A Win32-style group caption: the text, then a rule to the right edge.</summary>
	public static Gtk.Widget SectionHeader (Gtk.Label label)
	{
		label.Xalign = 0;
		Gtk.Separator rule = Gtk.Separator.New (Gtk.Orientation.Horizontal);
		rule.Hexpand = true;
		rule.Valign = Gtk.Align.Center;
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		box.Append (label);
		box.Append (rule);
		return box;
	}

	public static Gtk.Widget SectionHeader (string text)
		=> SectionHeader (Gtk.Label.New (text));

	/// <summary>
	/// Helper function to avoid repeated casts. The dialog's content area is always a Box.
	/// </summary>
	public static Gtk.Box GetContentAreaBox (this Gtk.Dialog dialog)
		=> (Gtk.Box) dialog.GetContentArea ();

	/// <summary>
	/// Set all four margins of the widget to the same value.
	/// </summary>
	/// <param name="w"></param>
	/// <param name="margin"></param>
	public static void SetAllMargins (this Gtk.Widget w, int margin)
	{
		w.MarginTop = w.MarginBottom = w.MarginStart = w.MarginEnd = margin;
	}

	/// <summary>
	/// For a combo box that has an entry, provides easy access to the child entry widget.
	/// </summary>
	public static Gtk.Entry GetEntry (this Gtk.ComboBox box)
	{
		if (!box.HasEntry)
			throw new InvalidOperationException ("Combobox does not have an entry");

		return (Gtk.Entry) box.Child!;
	}

	/// <summary>
	/// Helper function to return whether the text field of a Gtk.Entry has focus (i.e. is
	/// currently being edited). Gtk.Entry.HasFocus does not match this.
	/// </summary>
	public static bool IsEditingText (this Gtk.Entry entry)
	{
		Gtk.Widget? entryText = entry.GetFirstChild ();

		if (entryText is null) {
			Console.Error.WriteLine ("Failed to find child text widget for Gtk.Entry");
			return false;
		}

		return entryText.HasFocus;
	}

	/// <summary>
	/// Configures a spin button to immediately activate the default widget after pressing Enter,
	/// by configuring the editable text field.
	/// In GTK4, Gtk.SpinButton.SetActivateDefault() requires a second Enter to activate.
	/// </summary>
	public static void SetActivatesDefaultImmediate (
		this Gtk.SpinButton spin_button,
		bool activates)
	{
		Gtk.Editable? editable = spin_button.GetDelegate ();

		if (editable is null)
			return;

		// TODO-GTK4 (bindings, unsubmitted) - should be able to cast to a Gtk.Text from Gtk.Editable
		Gtk.Text text = (Gtk.Text) GObject.Internal.InstanceWrapper.WrapHandle<Gtk.Text> (editable.Handle.DangerousGetHandle (), ownedRef: false);
		text.SetActivatesDefault (activates);
	}

	/// <summary>
	/// Remove the widget if it is a child of the box.
	/// Calling Remove() produces warnings from GTK if the child isn't found.
	/// </summary>
	public static void RemoveIfChild (
		this Gtk.Box box,
		Gtk.Widget to_remove)
	{
		Gtk.Widget? child = box.GetFirstChild ();
		while (child != null) {

			if (child == to_remove) {
				box.Remove (child);
				return;
			}

			child = child.GetNextSibling ();
		}
	}

	/// Wrapper around TranslateCoordinates which uses PointD instead of separate x/y parameters.
	public static bool TranslateCoordinates (
		this Gtk.Widget src,
		Gtk.Widget dest,
		PointD src_pos,
		out PointD dest_pos)
	{
		bool result = src.TranslateCoordinates (
			dest,
			src_pos.X,
			src_pos.Y,
			out double x,
			out double y);

		dest_pos = new (x, y);

		return result;
	}

	/// <summary>
	/// Checks whether the mousePos (which is relative to topwidget) is within the area and returns its relative position to the area.
	/// </summary>
	/// <param name="widget">Drawing area where returns true if mouse inside.</param>
	/// <param name="topWidget">The top widget. This is what the mouse position is relative to.</param>
	/// <param name="mousePos">Position of the mouse relative to the top widget, usually obtained from Gtk.GestureClick</param>
	/// <param name="relPos">Position of the mouse relative to the drawing area.</param>
	/// <returns>Whether or not mouse position is within the drawing area.</returns>
	public static bool IsMouseInDrawingArea (this Gtk.Widget widget, Gtk.Widget topWidget, PointD mousePos, out PointD relPos)
	{
		widget.TranslateCoordinates (topWidget, 0, 0, out double x, out double y);
		relPos = new PointD ((mousePos.X - x), (mousePos.Y - y));
		if (relPos.X >= 0 && relPos.X <= widget.GetWidth () && relPos.Y >= 0 && relPos.Y <= widget.GetHeight ())
			return true;
		return false;
	}

	/// <summary>
	/// Helper method for scrolling to the selected item in a list view.
	/// When a new item is added, immediately scrolling to the end doesn't scroll far enough (likely
	/// because the widget's size hasn't updated). Performing the scroll after UI events have
	/// been processed works well.
	/// </summary>
	public static void ScrollToSelectedItem (this Gtk.ListView view, Gtk.SingleSelection selection)
	{
		GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_LOW, (() => {
			view.ScrollTo (selection.Selected, Gtk.ListScrollFlags.None, null);
			return false;
		}));
	}

	/// <summary>
	/// Helper method for displaying a repeated tiling texture.
	/// </summary>
	public static void AppendRepeatingTexture (this Gtk.Snapshot snapshot, Gdk.Texture texture, Graphene.Rect bounds)
	{
		snapshot.PushRepeat (bounds, childBounds: null);

		Graphene.Rect patternBounds = Graphene.Rect.Alloc ();
		patternBounds.Init (0, 0, texture.Width, texture.Height);
		snapshot.AppendTexture (texture, patternBounds);

		snapshot.Pop ();
	}
}

