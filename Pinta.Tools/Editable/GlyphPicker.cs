using System;
using System.Collections.Generic;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// A tool bar dropdown of small drawn pictures, like Paint.NET's shape, line style and cap pickers.
/// The button shows the chosen picture (and optionally its name); the popup lays the pictures out in
/// a grid, under group headings if any are given.
/// </summary>
public sealed class GlyphPicker
{
	public readonly record struct Item (string Name, Gdk.Texture Glyph, string? Group = null);

	private readonly IReadOnlyList<Item> items;
	private readonly Gtk.Image button_image = Gtk.Image.New ();
	private readonly Gtk.Label? button_label;
	private readonly List<Gtk.ToggleButton> buttons = [];
	private readonly Gtk.Popover popover = Gtk.Popover.New ();
	private int selected = -1;
	private bool updating = false;

	public Gtk.MenuButton Button { get; } = Gtk.MenuButton.New ();

	public event EventHandler? Changed;

	/// <param name="columns">Pictures per row in the popup.</param>
	/// <param name="showNameOnButton">Show the chosen item's name next to its picture on the button.</param>
	/// <param name="showNamesInList">Show each item's name next to its picture in the popup.</param>
	public GlyphPicker (IReadOnlyList<Item> items, int columns, bool showNameOnButton, bool showNamesInList)
	{
		this.items = items;

		Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		content.Append (button_image);
		if (showNameOnButton) {
			// A fixed width, so that the rest of the tool bar doesn't shift as the choice changes.
			button_label = Gtk.Label.New (null);
			button_label.WidthChars = button_label.MaxWidthChars = 13;
			button_label.Ellipsize = Pango.EllipsizeMode.End;
			button_label.Xalign = 0;
			content.Append (button_label);
		}

		Gtk.Box list = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		Gtk.Grid? grid = null;
		string? group = null;
		int column = 0, row = 0;

		for (int i = 0; i < items.Count; i++) {
			Item item = items[i];

			if (grid is null || item.Group != group) {
				group = item.Group;
				if (group is not null) {
					Gtk.Label heading = Gtk.Label.New (group);
					heading.Xalign = 0;
					heading.AddCssClass ("heading");
					list.Append (heading);
				}
				grid = Gtk.Grid.New ();
				list.Append (grid);
				column = row = 0;
			}

			Gtk.Box child = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
			child.Append (Gtk.Image.NewFromPaintable (item.Glyph));
			if (showNamesInList)
				child.Append (Gtk.Label.New (item.Name));

			Gtk.ToggleButton button = Gtk.ToggleButton.New ();
			button.Child = child;
			button.HasFrame = false;
			button.CanFocus = false;
			button.FocusOnClick = false;
			button.TooltipText = item.Name;
			int index = i;
			button.OnClicked += (_, _) => {
				if (updating)
					return;
				popover.Popdown ();
				SelectedIndex = index;
			};
			buttons.Add (button);
			grid.Attach (button, column, row, 1, 1);

			if (++column >= columns) {
				column = 0;
				row++;
			}
		}

		popover.Child = list;
		// Hand focus back to the canvas, so the next keys reach the tool.
		popover.OnClosed += (_, _) => GLib.Functions.IdleAdd (0, () => {
			if (PintaCore.Workspace.HasOpenDocuments)
				PintaCore.Workspace.ActiveWorkspace.GrabFocusToCanvas ();
			return false;
		});

		Button.Child = content;
		Button.Popover = popover;
		Button.HasFrame = false;
		Button.AlwaysShowArrow = true;
		Button.FocusOnClick = false;
		Button.CanFocus = false;

		SelectedIndex = 0;
	}

	public int Count => items.Count;

	public int SelectedIndex {
		get => selected;
		set {
			int index = Math.Clamp (value, 0, items.Count - 1);
			bool changed = index != selected;
			selected = index;

			updating = true;
			for (int i = 0; i < buttons.Count; i++)
				buttons[i].Active = i == index;
			updating = false;

			if (!changed)
				return;

			button_image.SetFromPaintable (items[index].Glyph);
			button_label?.SetText (items[index].Name);
			Button.TooltipText = items[index].Name;

			Changed?.Invoke (this, EventArgs.Empty);
		}
	}

	/// <summary>Moves the selection forward or back one item, wrapping around.</summary>
	public void Cycle (bool reverse)
		=> SelectedIndex = (selected + (reverse ? items.Count - 1 : 1)) % items.Count;

	/// <summary>Draws a picture for a picker item in a <paramref name="width"/> by <paramref name="height"/> box.</summary>
	public static Gdk.Texture CreateGlyph (int width, int height, Action<Context> draw)
	{
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, width, height);
		using (Context g = new (surface)) {
			g.Antialias = Antialias.Subpixel;
			draw (g);
		}
		return surface.ToTexture ();
	}

	/// <summary>Paint.NET-like glyph colours: a mid blue fill with a darker blue outline.</summary>
	public static readonly Color GlyphFill = new (0.29, 0.47, 0.71);
	public static readonly Color GlyphStroke = new (0.13, 0.27, 0.48);
}
