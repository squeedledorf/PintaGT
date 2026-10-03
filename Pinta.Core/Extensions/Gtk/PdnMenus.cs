using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinta.Core;

/// <summary>
/// Paint.NET-style menus on top of GTK's own popover menus: a 16px icon gutter on every item,
/// "Ctrl+Shift+X" / "PgUp" / "Del" shortcut text and an access letter on every item.
/// The items stay GtkModelButtons, so keyboard navigation, submenus, sensitivity and check
/// state are GTK's own; this only restyles their parts each time a menu opens.
/// </summary>
public static class PdnMenus
{
	private const string MENU_CLASS = "pdn-menu";
	private const string ICON_CLASS = "pdn-menu-icon";
	private const string CHECKED_CLASS = "pdn-menu-checked";
	private const string TRACKED_CLASS = "pdn-menu-tracked";

	/// <summary>
	/// Restyles every popover menu under the widget (a menu bar's menus, a menu button's popover)
	/// and their submenus whenever they open.
	/// </summary>
	public static void Attach (Gtk.Widget root)
	{
		foreach (Gtk.PopoverMenu popover in FindPopovers (root))
			Hook (popover);
	}

	private static void Hook (Gtk.PopoverMenu popover)
	{
		if (popover.HasCssClass (MENU_CLASS))
			return;

		popover.AddCssClass (MENU_CLASS);
		// Items are rebuilt when the model changes (effects loading, "Repeat <Effect>"), so redo it on every open.
		popover.OnMap += (_, _) => Decorate (popover);
	}

	private static void Decorate (Gtk.PopoverMenu popover)
	{
		// A submenu opened from the keyboard underlines its access letters too, as on Windows.
		// GTK clears the flag while showing the popover, so set it once that is done.
		if (popover.GetParent ()?.GetAncestor (Gtk.Popover.GetGType ()) is Gtk.Popover parentMenu && parentMenu.MnemonicsVisible)
			GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
				popover.MnemonicsVisible = true;
				return false;
			});

		List<Gtk.Widget> buttons = [];
		CollectModelButtons (popover, buttons);

		List<Gtk.Label> labels = [];
		foreach (Gtk.Widget button in buttons) {
			Gtk.Widget? startBox = null;
			Gtk.Widget? image = null;
			Gtk.Widget? indicator = null;
			for (Gtk.Widget? child = button.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
				switch (child.GetCssName ()) {
					case "box":
						startBox = child;
						if (child.GetFirstChild () is Gtk.Widget mark && mark.GetCssName () is "check" or "radio")
							indicator = mark;
						break;
					case "image":
						image = child;
						break;
					case "label":
						if (child is Gtk.Label label)
							labels.Add (label);
						break;
					case "accelerator":
						if (child is Gtk.Label accel)
							accel.SetText (FormatAccelerator (accel.GetText ()));
						break;
				}
			}

			// The gutter holds the icon (framed while a toggle item is on, as in Paint.NET), else the
			// check mark; an item with neither keeps GTK's empty indicator box, styled to the same width.
			bool showIcon = image is not null;
			if (image is not null)
				image.Visible = true;
			if (startBox is not null)
				startBox.Visible = !showIcon;
			if (showIcon)
				button.AddCssClass (ICON_CLASS);
			else
				button.RemoveCssClass (ICON_CLASS);

			if (showIcon && indicator is not null)
				TrackChecked (button, indicator);
		}

		string[] mnemonics = AssignMnemonics (labels.Select (MnemonicText).ToArray ());
		for (int i = 0; i < labels.Count; i++) {
			if (labels[i].UseUnderline && labels[i].GetLabel () == mnemonics[i])
				continue;
			labels[i].SetTextWithMnemonic (mnemonics[i]);
		}

		// Submenus are popovers parented to their items.
		foreach (Gtk.PopoverMenu submenu in FindPopovers (popover))
			Hook (submenu);
	}

	// The hidden check indicator still follows the action's state; mirror it on the item for the icon frame.
	private static void TrackChecked (Gtk.Widget button, Gtk.Widget indicator)
	{
		void Sync ()
		{
			if (indicator.GetStateFlags ().HasFlag (Gtk.StateFlags.Checked))
				button.AddCssClass (CHECKED_CLASS);
			else
				button.RemoveCssClass (CHECKED_CLASS);
		}

		Sync ();
		if (indicator.HasCssClass (TRACKED_CLASS))
			return;
		indicator.AddCssClass (TRACKED_CLASS);
		indicator.OnStateFlagsChanged += (_, _) => Sync ();
	}

	private static string MnemonicText (Gtk.Label label)
		=> label.UseUnderline ? label.GetLabel () : label.GetText ().Replace ("_", "__");

	// The menu's own items, not those of its submenus.
	private static void CollectModelButtons (Gtk.Widget widget, List<Gtk.Widget> buttons)
	{
		for (Gtk.Widget? child = widget.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
			if (child is Gtk.Popover)
				continue;
			if (child.GetCssName () == "modelbutton")
				buttons.Add (child);
			else
				CollectModelButtons (child, buttons);
		}
	}

	private static IEnumerable<Gtk.PopoverMenu> FindPopovers (Gtk.Widget widget)
	{
		for (Gtk.Widget? child = widget.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
			if (child is Gtk.PopoverMenu popover) {
				yield return popover;
				continue;
			}
			foreach (Gtk.PopoverMenu nested in FindPopovers (child))
				yield return nested;
		}

		if (widget is Gtk.MenuButton { Popover: Gtk.PopoverMenu menuButtonPopover })
			yield return menuButtonPopover;
	}

	/// <summary>
	/// Rewrites GTK's accelerator label ("Shift+Ctrl+X", "Page Up", "Delete") the way Paint.NET
	/// writes it ("Ctrl+Shift+X", "PgUp", "Del"): modifiers in Ctrl, Alt, Shift order, short key names.
	/// </summary>
	public static string FormatAccelerator (string gtkLabel)
	{
		if (gtkLabel.Length == 0)
			return gtkLabel;

		// The key is whatever follows the last separator; a "+" key leaves the label ending in "++".
		string key;
		string modifiers;
		if (gtkLabel == "+") {
			key = "+";
			modifiers = "";
		} else if (gtkLabel.EndsWith ("++")) {
			key = "+";
			modifiers = gtkLabel[..^2];
		} else {
			int split = gtkLabel.LastIndexOf ('+');
			key = gtkLabel[(split + 1)..];
			modifiers = split < 0 ? "" : gtkLabel[..split];
		}

		IEnumerable<string> ordered =
			modifiers
			.Split ('+', StringSplitOptions.RemoveEmptyEntries)
			.OrderBy (ModifierRank); // Stable, so unknown modifiers keep GTK's order after these.

		key = key switch {
			"Page Up" => "PgUp",
			"Page Down" => "PgDn",
			"Delete" => "Del",
			"Insert" => "Ins",
			"Escape" => "Esc",
			"Return" => "Enter",
			_ => key,
		};

		return string.Join ("+", ordered.Append (key));
	}

	private static int ModifierRank (string modifier) => modifier switch {
		"Ctrl" => 0,
		"Alt" => 1,
		"Shift" => 2,
		_ => 3,
	};

	/// <summary>
	/// Gives each label (in GTK mnemonic syntax, "__" for a literal underscore) an access letter that
	/// is unique within its menu, keeping letters already chosen. Each label's first letter is tried first,
	/// then the first letters of its later words, then any letter or digit. Labels left without a free letter
	/// are unchanged.
	/// </summary>
	public static string[] AssignMnemonics (IReadOnlyList<string> labels)
	{
		HashSet<char> used = [];
		foreach (string label in labels) {
			int index = MnemonicIndex (label);
			if (index >= 0)
				used.Add (char.ToUpperInvariant (label[index + 1]));
		}

		string[] result = [.. labels];
		// First letters go first across the whole menu, so a later "Select All" is not robbed of
		// its S by an earlier label that had to fall back to a later word.
		foreach (bool firstLetterOnly in new[] { true, false }) {
			for (int i = 0; i < result.Length; i++) {
				if (MnemonicIndex (result[i]) >= 0)
					continue;

				string text = result[i].Replace ("__", "_");
				IEnumerable<int> candidates = Candidates (text);
				if (firstLetterOnly)
					candidates = candidates.Take (1);
				int pick = candidates.FirstOrDefault (c => used.Add (char.ToUpperInvariant (text[c])), -1);
				if (pick >= 0)
					result[i] = $"{text[..pick].Replace ("_", "__")}_{text[pick..].Replace ("_", "__")}";
			}
		}

		return result;
	}

	// Positions to try, in order of preference: word starts, then every other letter or digit.
	private static IEnumerable<int> Candidates (string text)
	{
		IEnumerable<int> letters = Enumerable.Range (0, text.Length).Where (i => char.IsLetterOrDigit (text[i]));
		IEnumerable<int> wordStarts = letters.Where (i => i == 0 || !char.IsLetterOrDigit (text[i - 1]));
		return wordStarts.Concat (letters).Distinct ();
	}

	// The index of the mnemonic's underscore, or -1. "__" is a literal underscore.
	private static int MnemonicIndex (string label)
	{
		for (int i = 0; i < label.Length - 1; i++) {
			if (label[i] != '_')
				continue;
			if (label[i + 1] != '_')
				return i;
			i++;
		}
		return -1;
	}
}
