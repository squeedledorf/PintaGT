using System;
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;
using Pinta.Resources;

namespace Pinta.Docking;

/// <summary>
/// A Paint.NET-style utility window that floats over the canvas inside a <see cref="PanelArea"/>:
/// a small title bar (title and close button), the content, and optionally a footer
/// button row with a resize grip at its right end.
/// </summary>
[GObject.Subclass<Gtk.Box>]
public sealed partial class FloatingPanel
{
	private Gtk.Box title_bar;
	private Gtk.Label title_label;
	private Gtk.Box? footer;

	/// <summary>Key used for the panel's settings, e.g. "history".</summary>
	public string Id { get; private set; } = string.Empty;

	/// <summary>The title bar, which the panel area uses as the drag handle.</summary>
	public Gtk.Widget TitleBar => title_bar;

	/// <summary>The resize grip, for panels that can be resized.</summary>
	public Gtk.Widget? Grip { get; private set; }

	/// <summary>Raised when the close button is clicked.</summary>
	public event EventHandler? CloseClicked;

	[MemberNotNull (nameof (title_bar), nameof (title_label))]
	partial void Initialize ()
	{
		Gtk.Label titleLabel = Gtk.Label.New (null);
		titleLabel.AddCssClass (Styles.PdnPanelTitle);
		titleLabel.Xalign = 0;
		titleLabel.Hexpand = true;
		titleLabel.Ellipsize = Pango.EllipsizeMode.End;
		titleLabel.WidthChars = 1; // Narrow panels (Tools) show "To..." like Paint.NET.

		Gtk.Button closeButton = Gtk.Button.NewFromIconName (StandardIcons.WindowClose);
		closeButton.AddCssClass (Styles.PdnPanelClose);
		closeButton.FocusOnClick = false;
		closeButton.Valign = Gtk.Align.Center;
		closeButton.TooltipText = Translations.GetString ("Close");
		closeButton.OnClicked += (_, _) => CloseClicked?.Invoke (this, EventArgs.Empty);

		Gtk.Box titleBar = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		titleBar.AddCssClass (Styles.PdnPanelHeader);
		titleBar.Append (titleLabel);
		titleBar.Append (closeButton);
		titleBar.Cursor = Gdk.Cursor.NewFromName (StandardCursors.Default, null);

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Vertical);
		AddCssClass (Styles.PdnPanel);
		Append (titleBar);

		// --- References to keep

		title_bar = titleBar;
		title_label = titleLabel;
	}

	public static FloatingPanel New (string id, string title, Gtk.Widget content, bool resizable)
	{
		FloatingPanel panel = NewWithProperties ([]);
		panel.Id = id;
		panel.title_label.SetLabel (title);
		panel.Name = $"panel-{id}";

		content.Vexpand = resizable;
		content.Valign = Gtk.Align.Fill;
		panel.Append (content);

		if (resizable)
			panel.Grip = panel.CreateGrip ();

		return panel;
	}

	/// <summary>
	/// The footer button row (History's Undo/Redo, the Layers buttons). The resize grip sits at its right end.
	/// </summary>
	public Gtk.Box Footer {
		get {
			if (footer is not null)
				return footer;

			footer = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
			footer.AddCssClass (Styles.PdnPanelFooter);
			footer.Hexpand = true;

			Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
			row.Append (footer);
			if (Grip is not null) {
				Remove (Grip);
				row.Append (Grip);
			}
			Append (row);
			return footer;
		}
	}

	private Gtk.DrawingArea CreateGrip ()
	{
		// Paint.NET's size grip: a triangle of dots in the bottom-right corner.
		const int SIZE = 12;
		Gtk.DrawingArea grip = Gtk.DrawingArea.New ();
		grip.SetSizeRequest (SIZE, SIZE);
		grip.Halign = Gtk.Align.End;
		grip.Valign = Gtk.Align.End;
		grip.Cursor = Gdk.Cursor.NewFromName (StandardCursors.ResizeSE, null);
		grip.TooltipText = Translations.GetString ("Resize");
		grip.SetDrawFunc ((area, g, width, height) => {
			area.GetColor (out Gdk.RGBA fg);
			g.SetSourceRgba (fg.Red, fg.Green, fg.Blue, 0.45);
			for (int row = 0; row < 3; row++)
				for (int col = 2 - row; col < 3; col++)
					g.Rectangle (width - 4 * (3 - col) + 1, height - 4 * (3 - row) + 1, 2, 2);
			g.Fill ();
		});
		Append (grip);
		return grip;
	}
}
