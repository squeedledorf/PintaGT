using System;
using System.Globalization;
using Pinta.Core;

namespace Pinta;

/// <summary>
/// The size block shared by Paint.NET's New, Resize and Canvas Size dialogs:
/// "New size: X MB", optionally By percentage / By absolute size, Maintain aspect ratio,
/// then Pixel size (Width, Height, Resolution) and Print size (Width, Height).
/// Resolution converts between the two groups; changing it keeps the print size.
/// </summary>
internal sealed class ImageSizeFields
{
	private const int SPACING = 6;
	private const int INDENT = 16;

	private readonly Size original_size;
	private readonly Gtk.Label new_size_label;
	private readonly Gtk.CheckButton? percentage_radio;
	private readonly Gtk.CheckButton? absolute_radio;
	private readonly Gtk.SpinButton? percentage_spinner;
	private readonly Gtk.CheckButton aspect_checkbox;
	private readonly Gtk.SpinButton width_spinner;
	private readonly Gtk.SpinButton height_spinner;
	private readonly Gtk.SpinButton resolution_spinner;
	private readonly Gtk.DropDown resolution_units;
	private readonly Gtk.SpinButton print_width_spinner;
	private readonly Gtk.SpinButton print_height_spinner;
	private readonly Gtk.DropDown print_units;
	private readonly Gtk.Label print_height_units;

	private bool updating;
	private double dpi;
	private Size aspect_size; // The ratio "Maintain aspect ratio" holds.
	private (double Width, double Height) print_inches; // The print size a resolution change keeps.

	/// <summary>The block, ready to append to a dialog's content area.</summary>
	public Gtk.Box Widget { get; }

	/// <summary>Raised when the pixel size or the resolution changes.</summary>
	public event EventHandler? Changed;

	/// <param name="withPercentage">Show the "By percentage" / "By absolute size" choice (Resize, Canvas Size).</param>
	public ImageSizeFields (Size initialSize, double initialDpi, PrintUnit unit, bool withPercentage)
	{
		original_size = initialSize;
		aspect_size = initialSize;
		dpi = initialDpi;

		Gtk.Grid grid = Gtk.Grid.New ();
		grid.RowSpacing = SPACING;
		grid.ColumnSpacing = SPACING;
		int row = 0;

		new_size_label = Gtk.Label.New (null);
		grid.Attach (SectionHeader (new_size_label), 0, row++, 4, 1);

		int indent = 0;
		if (withPercentage) {
			percentage_radio = Gtk.CheckButton.NewWithMnemonic (Translations.GetString ("By _percentage:"));
			percentage_spinner = CreateSpinner (0.01, 100000, 2);
			percentage_spinner.Value = 100;
			Gtk.Box percentBox = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
			percentBox.Append (percentage_spinner);
			percentBox.Append (Gtk.Label.New ("%"));
			grid.Attach (percentage_radio, 0, row, 1, 1);
			grid.Attach (percentBox, 1, row++, 3, 1);

			absolute_radio = Gtk.CheckButton.NewWithMnemonic (Translations.GetString ("By _absolute size:"));
			absolute_radio.SetGroup (percentage_radio);
			grid.Attach (absolute_radio, 0, row++, 4, 1);
			indent = INDENT;
		}

		aspect_checkbox = Gtk.CheckButton.NewWithMnemonic (Translations.GetString ("_Maintain aspect ratio"));
		aspect_checkbox.MarginStart = indent;
		grid.Attach (aspect_checkbox, 0, row++, 4, 1);

		Gtk.Label pixelHeader = Gtk.Label.New (Translations.GetString ("Pixel size"));
		Gtk.Widget pixelSection = SectionHeader (pixelHeader);
		pixelSection.MarginStart = indent;
		grid.Attach (pixelSection, 0, row++, 4, 1);

		width_spinner = CreateSpinner (1, int.MaxValue, 0);
		height_spinner = CreateSpinner (1, int.MaxValue, 0);
		resolution_spinner = CreateSpinner (0.01, 100000, 2);
		resolution_units = Gtk.DropDown.NewFromStrings ([
			Translations.GetString ("pixels/inch"),
			Translations.GetString ("pixels/cm")]);

		AttachRow (grid, row++, indent + INDENT, Translations.GetString ("Width:"), width_spinner, Gtk.Label.New (Translations.GetString ("pixels")));
		AttachRow (grid, row++, indent + INDENT, Translations.GetString ("Height:"), height_spinner, Gtk.Label.New (Translations.GetString ("pixels")));
		AttachRow (grid, row++, indent + INDENT, Translations.GetString ("Resolution:"), resolution_spinner, resolution_units);

		Gtk.Label printHeader = Gtk.Label.New (Translations.GetString ("Print size"));
		Gtk.Widget printSection = SectionHeader (printHeader);
		printSection.MarginStart = indent;
		grid.Attach (printSection, 0, row++, 4, 1);

		print_width_spinner = CreateSpinner (0.01, 1000000, 2);
		print_height_spinner = CreateSpinner (0.01, 1000000, 2);
		print_units = Gtk.DropDown.NewFromStrings ([
			Translations.GetString ("inches"),
			Translations.GetString ("centimeters")]);
		print_height_units = Gtk.Label.New (null);
		print_height_units.Xalign = 0;

		AttachRow (grid, row++, indent + INDENT, Translations.GetString ("Width:"), print_width_spinner, print_units);
		AttachRow (grid, row++, indent + INDENT, Translations.GetString ("Height:"), print_height_spinner, print_height_units);

		Widget = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		Widget.Append (grid);

		// --- Initial values

		updating = true;
		width_spinner.Value = initialSize.Width;
		height_spinner.Value = initialSize.Height;
		resolution_units.Selected = (uint) unit;
		print_units.Selected = (uint) unit;
		updating = false;
		RefreshFromPixels ();

		// --- Events

		OnEdited (width_spinner, w => SetPixelWidth ((int) Math.Round (w)));
		OnEdited (height_spinner, h => SetPixelHeight ((int) Math.Round (h)));
		OnEdited (resolution_spinner, SetResolution);
		OnEdited (print_width_spinner, pw => SetPixelWidth (PrintSize.PrintToPixels (pw, dpi, Unit)));
		OnEdited (print_height_spinner, ph => SetPixelHeight (PrintSize.PrintToPixels (ph, dpi, Unit)));
		if (percentage_spinner is not null)
			OnEdited (percentage_spinner, ApplyPercentage);

		Gtk.DropDown.SelectedPropertyDefinition.Notify (resolution_units, (_, _) => SyncUnits (resolution_units));
		Gtk.DropDown.SelectedPropertyDefinition.Notify (print_units, (_, _) => SyncUnits (print_units));

		aspect_checkbox.OnToggled += (_, _) => aspect_size = PixelSize;

		if (percentage_radio is not null && absolute_radio is not null && percentage_spinner is not null) {
			absolute_radio.OnToggled += (_, _) => UpdateSensitivity ();
			absolute_radio.Active = true;
			UpdateSensitivity ();
		}
	}

	public Size PixelSize => new (width_spinner.GetValueAsInt (), height_spinner.GetValueAsInt ());

	/// <summary>The resolution in pixels per inch.</summary>
	public double Dpi => dpi;

	public PrintUnit Unit => (PrintUnit) Math.Min (print_units.Selected, 1u);

	public bool MaintainAspectRatio {
		get => aspect_checkbox.Active;
		set => aspect_checkbox.Active = value;
	}

	public bool ByPercentage {
		get => percentage_radio?.Active ?? false;
		set {
			if (percentage_radio is null || absolute_radio is null)
				return;
			if (value)
				percentage_radio.Active = true;
			else
				absolute_radio.Active = true;
		}
	}

	/// <summary>Focus the field Paint.NET starts in, with its text selected so typing replaces it.</summary>
	public void FocusFirstField ()
	{
		Gtk.SpinButton field = ByPercentage && percentage_spinner is not null ? percentage_spinner : width_spinner;
		field.GrabFocus ();
		field.SelectRegion (0, -1);
	}

	/// <summary>Back to the size and resolution the dialog opened with.</summary>
	public void Reset (double originalDpi)
	{
		updating = true;
		dpi = originalDpi;
		width_spinner.Value = original_size.Width;
		height_spinner.Value = original_size.Height;
		percentage_spinner?.SetValue (100);
		updating = false;
		aspect_size = original_size;
		RefreshFromPixels ();
	}

	/// <summary>Use a new size, such as a clipboard image's, as the starting point.</summary>
	public void SetPixelSize (Size size)
	{
		updating = true;
		width_spinner.Value = size.Width;
		height_spinner.Value = size.Height;
		updating = false;
		aspect_size = size;
		RefreshFromPixels ();
	}

	/// <summary>Commit any typed text in the fields, as pressing OK does.</summary>
	public void CommitTypedValues ()
	{
		width_spinner.Update ();
		height_spinner.Update ();
	}

	private void UpdateSensitivity ()
	{
		bool absolute = absolute_radio!.Active;
		percentage_spinner!.Sensitive = !absolute;
		aspect_checkbox.Sensitive = absolute;
		width_spinner.Sensitive = absolute;
		height_spinner.Sensitive = absolute;
		resolution_spinner.Sensitive = absolute;
		print_width_spinner.Sensitive = absolute;
		print_height_spinner.Sensitive = absolute;
	}

	private void SetPixelWidth (int width)
	{
		if (width < 1)
			return;
		int height = height_spinner.GetValueAsInt ();
		if (aspect_checkbox.Active && aspect_size.Width > 0)
			height = Math.Max (1, (int) Math.Round ((double) width * aspect_size.Height / aspect_size.Width));
		SetPixels (width, height);
	}

	private void SetPixelHeight (int height)
	{
		if (height < 1)
			return;
		int width = width_spinner.GetValueAsInt ();
		if (aspect_checkbox.Active && aspect_size.Height > 0)
			width = Math.Max (1, (int) Math.Round ((double) height * aspect_size.Width / aspect_size.Height));
		SetPixels (width, height);
	}

	private void ApplyPercentage (double percent)
	{
		if (percent <= 0)
			return;
		SetPixels (
			Math.Max (1, (int) Math.Round (original_size.Width * percent / 100)),
			Math.Max (1, (int) Math.Round (original_size.Height * percent / 100)));
	}

	/// <summary>A new resolution keeps the print size, so the pixel size follows it.</summary>
	private void SetResolution (double resolution)
	{
		if (resolution <= 0)
			return;
		dpi = PrintSize.ResolutionToDpi (resolution, Unit);
		SetPixels (
			Math.Max (1, (int) Math.Round (print_inches.Width * dpi)),
			Math.Max (1, (int) Math.Round (print_inches.Height * dpi)),
			refreshResolution: false);
	}

	private void SetPixels (int width, int height, bool refreshResolution = true)
	{
		updating = true;
		if (width_spinner.GetValueAsInt () != width && !IsEditing (width_spinner))
			width_spinner.Value = width;
		if (height_spinner.GetValueAsInt () != height && !IsEditing (height_spinner))
			height_spinner.Value = height;
		// A field being typed in keeps its text; GTK commits it on focus out or Enter.
		updating = false;
		RefreshFromPixels (refreshResolution, pixelWidth: width, pixelHeight: height);
	}

	private static bool IsEditing (Gtk.SpinButton spinner)
	{
		Gtk.Widget? focus = (spinner.GetRoot () as Gtk.Window)?.GetFocus ();
		return focus is not null && (focus == spinner || focus.IsAncestor (spinner));
	}

	private void SyncUnits (Gtk.DropDown source)
	{
		if (updating)
			return;
		updating = true;
		resolution_units.Selected = source.Selected;
		print_units.Selected = source.Selected;
		updating = false;
		RefreshFromPixels ();
	}

	/// <summary>Recompute the resolution, print size and memory estimate from the pixel size.</summary>
	private void RefreshFromPixels (bool refreshResolution = true, int? pixelWidth = null, int? pixelHeight = null)
	{
		int width = pixelWidth ?? width_spinner.GetValueAsInt ();
		int height = pixelHeight ?? height_spinner.GetValueAsInt ();

		// Typing a resolution digit by digit must not wear away the print size it scales.
		if (refreshResolution)
			print_inches = (width / dpi, height / dpi);

		updating = true;
		if (refreshResolution && !IsEditing (resolution_spinner))
			resolution_spinner.Value = PrintSize.DpiToResolution (dpi, Unit);
		if (!IsEditing (print_width_spinner))
			print_width_spinner.Value = PrintSize.PixelsToPrint (width, dpi, Unit);
		if (!IsEditing (print_height_spinner))
			print_height_spinner.Value = PrintSize.PixelsToPrint (height, dpi, Unit);
		updating = false;

		print_height_units.SetText (Unit == PrintUnit.Inches ? Translations.GetString ("inches") : Translations.GetString ("centimeters"));
		new_size_label.SetText (Translations.GetString ("New size: {0}", PrintSize.FormatImageMemory (new Size (width, height))));

		Changed?.Invoke (this, EventArgs.Empty);
	}

	/// <summary>
	/// Call <paramref name="apply"/> when the value is committed and also while it is typed,
	/// so the other fields follow each keystroke as in Paint.NET.
	/// </summary>
	private void OnEdited (Gtk.SpinButton spinner, Action<double> apply)
	{
		spinner.OnValueChanged += (_, _) => {
			if (!updating)
				apply (spinner.Value);
		};
		spinner.OnChanged += (_, _) => {
			if (!updating && IsEditing (spinner) && double.TryParse (spinner.GetText (), NumberStyles.Float, CultureInfo.CurrentCulture, out double typed))
				apply (typed);
		};
	}

	private static Gtk.SpinButton CreateSpinner (double min, double max, uint digits)
	{
		Gtk.SpinButton spinner = Gtk.SpinButton.NewWithRange (min, max, 1);
		spinner.Digits = digits;
		spinner.WidthChars = 7;
		spinner.Xalign = 1;
		spinner.SetActivatesDefaultImmediate (true);
		return spinner;
	}

	private static void AttachRow (Gtk.Grid grid, int row, int indent, string label, Gtk.Widget field, Gtk.Widget units)
	{
		Gtk.Label caption = Gtk.Label.New (label);
		caption.Xalign = 0;
		caption.MarginStart = indent;
		caption.Hexpand = true;
		units.Halign = Gtk.Align.Fill;
		if (units is Gtk.Label l)
			l.Xalign = 0;
		grid.Attach (caption, 0, row, 1, 1);
		grid.Attach (field, 1, row, 1, 1);
		grid.Attach (units, 2, row, 2, 1);
	}

	/// <summary>A Win32-style group caption: the text, then a rule to the right edge.</summary>
	public static Gtk.Widget SectionHeader (Gtk.Label label)
	{
		label.Xalign = 0;
		Gtk.Separator rule = Gtk.Separator.New (Gtk.Orientation.Horizontal);
		rule.Hexpand = true;
		rule.Valign = Gtk.Align.Center;
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, SPACING);
		box.Append (label);
		box.Append (rule);
		return box;
	}

	public static Gtk.Widget SectionHeader (string text)
		=> SectionHeader (Gtk.Label.New (text));
}
