using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// A compact Colors window in the style of Paint.NET 5: primary/secondary squares with an
/// active-slot notch, a live colour wheel, a "More >>" section with
/// RGB/hex/HSV/alpha sliders, and the palette strip with its menu.
/// Left clicks on the wheel or palette set the active slot, right clicks the inactive one.
/// </summary>
[GObject.Subclass<Gtk.Box>]
public sealed partial class ColorsPanel
{
	private const string EXPANDED_SETTING = "colors-panel-expanded";

	private const int PALETTE_COLUMNS = 16;
	private const int CELL = 12;
	private const int COLLAPSED_ROWS = 2;
	private const int WHEEL_SIZE = 144; // A 140px wheel, as in Paint.NET.
	private const int WHEEL_RADIUS = WHEEL_SIZE / 2 - 2;
	private const int WHEEL_TOP = 30;
	private const int SLIDER_WIDTH = 110;

	private static readonly RectangleD primary_rect = new (2, 2, 32, 32);
	private static readonly RectangleD secondary_rect = new (19, 19, 32, 32);
	private static readonly RectangleD swap_rect = new (37, 0, 16, 16);
	private static readonly RectangleD reset_rect = new (0, 37, 16, 16);

	private static readonly Color swap_blue = new (0.24, 0.47, 0.85);

	private IPaletteService palette = null!; // NRT - set by New()
	private ISettingsService settings = null!;
	private IChromeService chrome = null!;

	private Gtk.DrawingArea swatches = null!;
	private Gtk.DrawingArea wheel = null!;
	private Gtk.DrawingArea palette_area = null!;
	private Gtk.Button more_button = null!;
	private Gtk.Box details = null!;
	private Gtk.ToggleButton add_color = null!;
	private Gtk.Entry hex_entry = null!;
	private ColorPickerSlider[] sliders = [];

	private ImageSurface? wheel_cache;
	private bool primary_active = true;
	private bool expanded;
	private bool updating;
	private bool dialog_open;
	private bool drag_targets_active;

	public static ColorsPanel New ()
	{
		ColorsPanel panel = NewWithProperties ([]);
		panel.Build (PintaCore.Palette, PintaCore.Settings, PintaCore.Chrome);
		return panel;
	}

	/// <summary>
	/// Whether the primary slot (rather than the secondary) is the one with the notch,
	/// i.e. the one the wheel, sliders and left clicks edit.
	/// </summary>
	public bool PrimaryActive {
		get => primary_active;
		set {
			primary_active = value;
			UpdateView ();
		}
	}

	/// <summary>Moves the notch to the other slot (Paint.NET's C key).</summary>
	public void ToggleActiveSlot () => PrimaryActive = !PrimaryActive;

	private Color ActiveColor => primary_active ? palette.PrimaryColor : palette.SecondaryColor;

	/// <param name="active">True for the slot with the notch, false for the other one.</param>
	private void SetSlotColor (bool active, Color color, bool addToRecent)
		=> palette.SetColor (active == primary_active, color, addToRecent);

	private void Build (IPaletteService palette, ISettingsService settings, IChromeService chrome)
	{
		this.palette = palette;
		this.settings = settings;
		this.chrome = chrome;

		expanded = settings.GetSetting (EXPANDED_SETTING, false);

		SetOrientation (Gtk.Orientation.Horizontal);
		Spacing = 10;
		this.SetAllMargins (6);

		Append (BuildMainColumn ());
		Append (BuildDetails ());

		palette.PrimaryColorChanged += (_, _) => UpdateView ();
		palette.SecondaryColorChanged += (_, _) => UpdateView ();
		palette.CurrentPalette.PaletteChanged += (_, _) => UpdatePaletteSize ();

		ApplyExpanded ();
		UpdateView ();
	}

	private Gtk.Box BuildMainColumn ()
	{
		// The wheel, with the swatches and More button floating over its top corners.
		swatches = Gtk.DrawingArea.New ();
		swatches.SetSizeRequest (54, 54);
		swatches.Halign = Gtk.Align.Start;
		swatches.Valign = Gtk.Align.Start;
		swatches.SetDrawFunc ((_, g, _, _) => DrawSwatches (g));
		swatches.HasTooltip = true;
		swatches.OnQueryTooltip += HandleSwatchTooltip;
		Gtk.GestureClick swatchClick = Gtk.GestureClick.New ();
		swatchClick.OnReleased += (_, e) => HandleSwatchClick (new PointD (e.X, e.Y));
		swatches.AddController (swatchClick);

		more_button = Gtk.Button.New ();
		more_button.Halign = Gtk.Align.End;
		more_button.Valign = Gtk.Align.Start;
		more_button.FocusOnClick = false;
		more_button.AddCssClass ("pdn-push-button");
		more_button.OnClicked += (_, _) => {
			expanded = !expanded;
			settings.PutSetting (EXPANDED_SETTING, expanded);
			ApplyExpanded ();
		};

		wheel = Gtk.DrawingArea.New ();
		wheel.SetSizeRequest (WHEEL_SIZE, WHEEL_SIZE);
		wheel.SetDrawFunc ((_, g, _, _) => DrawWheel (g));
		wheel.TooltipText = Translations.GetString ("Left click to set the active color, right click to set the other one. Ctrl: hue only. Alt: saturation only. Shift: snap the hue.");
		AddDrag (wheel, PickFromWheel);

		Gtk.Box wheelRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		wheelRow.Halign = Gtk.Align.End;
		wheelRow.MarginTop = WHEEL_TOP;
		wheelRow.Append (wheel);

		Gtk.Overlay top = Gtk.Overlay.New ();
		top.SetChild (wheelRow);
		top.AddOverlay (swatches);
		top.AddOverlay (more_button);

		// Palette controls and swatch strip.
		add_color = Gtk.ToggleButton.New ();
		add_color.SetChild (IconArea (DrawAddColorIcon));
		add_color.AddCssClass (AdwaitaStyles.Flat);
		add_color.AddCssClass ("pdn-flat-button");
		add_color.FocusOnClick = false;
		add_color.TooltipText = Translations.GetString ("Add Color: click a palette swatch to replace it with the active color");
		add_color.OnToggled += (_, _) => HandleAddColorToggled ();

		// Paint.NET's palette menu: the palettes in the palettes folder first, then the commands.
		Gio.SimpleAction loadAction = Gio.SimpleAction.New ("load-palette", GLib.VariantType.String);
		loadAction.OnActivate += (_, e) => LoadPaletteFile (e.Parameter!.GetString (out nuint _));
		Gio.SimpleAction openFolderAction = Gio.SimpleAction.New ("open-palettes-folder", null);
		openFolderAction.OnActivate += (_, _) => OpenPalettesFolder ();
		Gio.SimpleActionGroup paletteActions = Gio.SimpleActionGroup.New ();
		paletteActions.AddAction (loadAction);
		paletteActions.AddAction (openFolderAction);
		InsertActionGroup ("colorspanel", paletteActions);

		Gio.Menu paletteMenu = Gio.Menu.New ();

		Gtk.MenuButton paletteButton = Gtk.MenuButton.New ();
		paletteButton.SetChild (IconArea (DrawPaletteIcon));
		paletteButton.AlwaysShowArrow = true;
		paletteButton.AddCssClass (AdwaitaStyles.Flat);
		paletteButton.AddCssClass ("pdn-flat-button");
		paletteButton.MenuModel = paletteMenu;
		// Rebuilt on every open so palettes saved or copied into the folder show up.
		paletteButton.SetCreatePopupFunc (_ => FillPaletteMenu (paletteMenu));
		paletteButton.TooltipText = Translations.GetString ("Palettes");

		Gtk.Box paletteRow = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
		add_color.Valign = Gtk.Align.Center;
		paletteButton.Valign = Gtk.Align.Center;
		paletteRow.Append (add_color);
		paletteRow.Append (paletteButton);

		palette_area = Gtk.DrawingArea.New ();
		palette_area.Halign = Gtk.Align.Start;
		palette_area.SetDrawFunc ((_, g, _, _) => DrawPalette (g));
		palette_area.HasTooltip = true;
		palette_area.OnQueryTooltip += HandlePaletteTooltip;
		Gtk.GestureClick paletteClick = Gtk.GestureClick.New ();
		paletteClick.SetButton (0);
		paletteClick.OnPressed += (_, e) => HandlePaletteClick (new PointD (e.X, e.Y), paletteClick.GetCurrentButton ());
		palette_area.AddController (paletteClick);

		Gtk.Box column = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		column.WidthRequest = PALETTE_COLUMNS * CELL;
		column.Append (top);
		column.Append (paletteRow);
		column.Append (palette_area);
		return column;
	}

	private Gtk.Box BuildDetails ()
	{
		ColorPickerSlider Slider (ColorPickerSlider.Component component, string label)
		{
			ColorPickerSlider slider = ColorPickerSlider.New (component, SLIDER_WIDTH);
			slider.SetLabel (label, 20);
			slider.OnColorChanged += (_, _) => {
				if (!updating)
					SetSlotColor (true, slider.Color, addToRecent: false);
			};
			return slider;
		}

		static Gtk.Label Heading (string text)
		{
			Gtk.Label label = Gtk.Label.New (text);
			label.Xalign = 0;
			label.AddCssClass (AdwaitaStyles.Heading);
			return label;
		}

		ColorPickerSlider red = Slider (ColorPickerSlider.Component.Red, Translations.GetString ("R:"));
		ColorPickerSlider green = Slider (ColorPickerSlider.Component.Green, Translations.GetString ("G:"));
		ColorPickerSlider blue = Slider (ColorPickerSlider.Component.Blue, Translations.GetString ("B:"));
		ColorPickerSlider hue = Slider (ColorPickerSlider.Component.Hue, Translations.GetString ("H:"));
		ColorPickerSlider sat = Slider (ColorPickerSlider.Component.Saturation, Translations.GetString ("S:"));
		ColorPickerSlider val = Slider (ColorPickerSlider.Component.Value, Translations.GetString ("V:"));
		ColorPickerSlider alpha = Slider (ColorPickerSlider.Component.Alpha, string.Empty);
		sliders = [red, green, blue, hue, sat, val, alpha];

		hex_entry = Gtk.Entry.New ();
		hex_entry.MaxWidthChars = 8;
		hex_entry.WidthChars = 8;
		hex_entry.OnChanged += (_, _) => {
			if (updating)
				return;
			string text = hex_entry.GetText ().TrimStart ('#');
			if (Color.FromHex (text) is not Color c)
				return;
			// The box shows RRGGBB; keep the current opacity unless one was typed.
			if (text.Length is 3 or 6)
				c = c with { A = ActiveColor.A };
			SetSlotColor (true, c, addToRecent: false);
		};

		Gtk.CenterBox hexRow = Gtk.CenterBox.New ();
		hexRow.SetStartWidget (Gtk.Label.New (Translations.GetString ("Hex:")));
		hexRow.SetEndWidget (hex_entry);

		details = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		details.Append (Heading (Translations.GetString ("RGB")));
		details.Append (red);
		details.Append (green);
		details.Append (blue);
		details.Append (hexRow);
		details.Append (Heading (Translations.GetString ("HSV")));
		details.Append (hue);
		details.Append (sat);
		details.Append (val);
		details.Append (Heading (Translations.GetString ("Opacity - Alpha")));
		details.Append (alpha);
		return details;
	}

	private static Gtk.DrawingArea IconArea (Action<Context> draw)
	{
		Gtk.DrawingArea area = Gtk.DrawingArea.New ();
		area.SetSizeRequest (16, 16);
		area.SetDrawFunc ((_, g, _, _) => draw (g));
		return area;
	}

	private void AddDrag (Gtk.DrawingArea area, Action<PointD, Gdk.ModifierType, bool> pick)
	{
		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.SetButton (0);
		drag.OnDragBegin += (_, e) => {
			drag_targets_active = drag.GetCurrentButton () != GtkExtensions.MOUSE_RIGHT_BUTTON;
			pick (new PointD (e.StartX, e.StartY), drag.GetCurrentEventState (), false);
		};
		drag.OnDragUpdate += (_, e) => {
			drag.GetStartPoint (out double x, out double y);
			pick (new PointD (x + e.OffsetX, y + e.OffsetY), drag.GetCurrentEventState (), false);
		};
		// Only the released colour goes into the recent colours, not every step of the drag.
		drag.OnDragEnd += (_, e) => {
			drag.GetStartPoint (out double x, out double y);
			pick (new PointD (x + e.OffsetX, y + e.OffsetY), drag.GetCurrentEventState (), true);
		};
		area.AddController (drag);
	}

	private Color DragTargetColor => drag_targets_active == primary_active ? palette.PrimaryColor : palette.SecondaryColor;

	private void PickFromWheel (PointD point, Gdk.ModifierType state, bool released)
	{
		Color current = DragTargetColor;
		PointD offset = new (point.X - WHEEL_SIZE / 2.0, point.Y - WHEEL_SIZE / 2.0);
		HsvColor hsv = ColorWheel.OffsetToHsv (
			offset,
			WHEEL_RADIUS,
			current.ToHsv (),
			keepSat: state.IsControlPressed (),
			keepHue: state.IsAltPressed (),
			snapHue: state.IsShiftPressed ());
		SetSlotColor (drag_targets_active, Color.FromHsv (hsv, current.A), released);
	}

	private void ApplyExpanded ()
	{
		more_button.Label = expanded ? Translations.GetString ("<< Less") : Translations.GetString ("More >>");
		details.Visible = expanded;
		UpdatePaletteSize ();
	}

	private void UpdatePaletteSize ()
	{
		int count = palette.CurrentPalette.Colors.Count;
		int rows = (count + PALETTE_COLUMNS - 1) / PALETTE_COLUMNS;
		if (!expanded)
			rows = Math.Min (rows, COLLAPSED_ROWS);
		palette_area.SetSizeRequest (PALETTE_COLUMNS * CELL, Math.Max (rows, 1) * CELL);
		palette_area.QueueDraw ();
	}

	private void UpdateView ()
	{
		Color active = ActiveColor;

		updating = true;
		try {
			foreach (ColorPickerSlider slider in sliders)
				slider.Color = active;
			if (!hex_entry.IsEditingText ())
				hex_entry.SetText (active.ToHex (addAlpha: false));
		} finally {
			updating = false;
		}

		swatches.QueueDraw ();
		wheel.QueueDraw ();
	}

	// --- Swatches

	private void HandleSwatchClick (PointD p)
	{
		if (swap_rect.ContainsPoint (p)) {
			// Swapping should not add to the recently used colors.
			Color primary = palette.PrimaryColor;
			palette.SetColor (true, palette.SecondaryColor, false);
			palette.SetColor (false, primary, false);
		} else if (reset_rect.ContainsPoint (p)) {
			palette.PrimaryColor = new Color (0, 0, 0);
			palette.SecondaryColor = new Color (1, 1, 1);
		} else if (primary_rect.ContainsPoint (p)) {
			SwatchClicked (primary: true);
		} else if (secondary_rect.ContainsPoint (p)) {
			SwatchClicked (primary: false);
		}
	}

	/// <summary>
	/// Clicking the inactive square moves the notch to it; clicking the active one opens the full picker.
	/// </summary>
	private async void SwatchClicked (bool primary)
	{
		if (primary != primary_active) {
			PrimaryActive = primary;
			return;
		}

		if (dialog_open)
			return;

		dialog_open = true;
		try {
			PaletteColors? choices = await RunColorPicker (primary);
			if (choices is null)
				return;
			if (palette.PrimaryColor != choices.Primary)
				palette.PrimaryColor = choices.Primary;
			if (palette.SecondaryColor != choices.Secondary)
				palette.SecondaryColor = choices.Secondary;
		} finally {
			dialog_open = false;
		}
	}

	private async Task<PaletteColors?> RunColorPicker (bool primarySelected)
	{
		using ColorPickerDialog dialog = ColorPickerDialog.New (
			chrome.MainWindow,
			palette,
			new PaletteColors (palette.PrimaryColor, palette.SecondaryColor),
			primarySelected,
			true,
			Translations.GetString ("Choose Colors"));

		Gtk.ResponseType response = await dialog.RunAsync ();
		return response == Gtk.ResponseType.Ok ? (PaletteColors) dialog.Colors : null;
	}

	private bool HandleSwatchTooltip (Gtk.Widget _, Gtk.Widget.QueryTooltipSignalArgs args)
	{
		PointD p = new (args.X, args.Y);
		string? text = null;
		if (swap_rect.ContainsPoint (p))
			text = Translations.GetString ("Swap primary and secondary colors") + " (X)";
		else if (reset_rect.ContainsPoint (p))
			text = Translations.GetString ("Reset to black and white");
		else if (primary_rect.ContainsPoint (p))
			text = SwatchTooltip (Translations.GetString ("Primary color"), palette.PrimaryColor, primary_active);
		else if (secondary_rect.ContainsPoint (p))
			text = SwatchTooltip (Translations.GetString ("Secondary color"), palette.SecondaryColor, !primary_active);

		args.Tooltip.SetText (text);
		return text is not null;
	}

	private static string SwatchTooltip (string name, Color color, bool isActive)
	{
		string action = isActive
			? Translations.GetString ("Click to open the color picker.")
			: Translations.GetString ("Click to make this the active color (C).");
		return $"{name}: #{color.ToHex ()}\n{action}";
	}

	private void DrawSwatches (Context g)
	{
		using Pattern checker = CairoExtensions.CreateTransparentBackgroundPattern (8);

		DrawSwatch (g, secondary_rect, palette.SecondaryColor, checker, !primary_active);
		DrawSwatch (g, primary_rect, palette.PrimaryColor, checker, primary_active);

		DrawSwapIcon (g);

		// Reset: a small black square over a white one.
		RectangleD white = new (reset_rect.X + 6, reset_rect.Y + 6, 9, 9);
		RectangleD black = new (reset_rect.X + 1, reset_rect.Y + 1, 9, 9);
		g.FillRectangle (white, new Color (1, 1, 1));
		g.DrawRectangle (white, new Color (0.4, 0.4, 0.4), 1);
		g.FillRectangle (black, new Color (0, 0, 0));
		g.DrawRectangle (black, new Color (0.4, 0.4, 0.4), 1);

		g.Dispose ();
	}

	private static void DrawSwatch (Context g, RectangleD r, Color color, Pattern checker, bool isActive)
	{
		if (color.A < 1)
			g.FillRectangle (r, checker);
		g.FillRectangle (r, color);
		g.DrawRectangle (r.Inflated (-1, -1), new Color (1, 1, 1), 1);
		g.DrawRectangle (r, new Color (0, 0, 0), 1);

		if (!isActive)
			return;

		// The notch: a small white triangle cut into the bottom edge.
		double cx = r.X + r.Width / 2;
		double bottom = r.Bottom - 1;
		ReadOnlySpan<PointD> notch = [new (cx - 5, bottom), new (cx, bottom - 6), new (cx + 5, bottom)];
		g.FillPolygonal (notch, new Color (1, 1, 1));
	}

	private static void DrawSwapIcon (Context g)
	{
		// A quarter arc with an arrowhead at each end, in Paint.NET's blue.
		double x0 = swap_rect.X + 3, y0 = swap_rect.Y + 4;
		double x1 = swap_rect.Right - 4, y1 = swap_rect.Bottom - 3;

		g.Save ();
		g.SetSourceColor (swap_blue);
		g.LineWidth = 1.5;
		g.MoveTo (x0, y0);
		g.CurveTo (x1, y0, x1, y0, x1, y1);
		g.Stroke ();
		g.MoveTo (x0 + 4, y0 - 3.5);
		g.LineTo (x0, y0);
		g.LineTo (x0 + 4, y0 + 3.5);
		g.MoveTo (x1 - 3.5, y1 - 4);
		g.LineTo (x1, y1);
		g.LineTo (x1 + 3.5, y1 - 4);
		g.Stroke ();
		g.Restore ();
	}

	// --- Wheel

	private void DrawWheel (Context g)
	{
		wheel_cache ??= RenderWheel ();
		g.SetSourceSurface (wheel_cache, 0, 0);
		g.Paint ();

		// Cursor: a small ring at the active colour's hue/saturation.
		PointD c = ColorWheel.HsvToOffset (ActiveColor.ToHsv (), WHEEL_RADIUS);
		RectangleD ring = new (WHEEL_SIZE / 2.0 + c.X - 4, WHEEL_SIZE / 2.0 + c.Y - 4, 8, 8);
		g.DrawEllipse (ring, new Color (0, 0, 0), 2);
		g.DrawEllipse (ring.Inflated (1, 1), new Color (1, 1, 1), 1);

		g.Dispose ();
	}

	private static ImageSurface RenderWheel ()
	{
		ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, WHEEL_SIZE, WHEEL_SIZE);
		Span<ColorBgra> data = surface.GetPixelData ();
		double centre = WHEEL_SIZE / 2.0;

		for (int y = 0; y < WHEEL_SIZE; y++) {
			for (int x = 0; x < WHEEL_SIZE; x++) {
				PointD offset = new (x + 0.5 - centre, y + 0.5 - centre);
				double distance = Math.Sqrt (offset.X * offset.X + offset.Y * offset.Y);
				double coverage = Math.Clamp (WHEEL_RADIUS - distance + 0.5, 0, 1);
				if (coverage <= 0)
					continue;
				HsvColor hsv = ColorWheel.OffsetToHsv (offset, WHEEL_RADIUS, HsvColor.White);
				data[y * WHEEL_SIZE + x] = Color.FromHsv (hsv, coverage).ToColorBgra ();
			}
		}

		surface.MarkDirty ();
		return surface;
	}

	// --- Palette

	private string PalettesFolder => System.IO.Path.Combine (settings.GetUserSettingsDirectory (), "Palettes");

	private void FillPaletteMenu (Gio.Menu menu)
	{
		menu.RemoveAll ();

		Gio.Menu palettes = Gio.Menu.New ();
		if (System.IO.Directory.Exists (PalettesFolder)) {
			IEnumerable<string> files = System.IO.Directory.EnumerateFiles (PalettesFolder)
				.Where (f => PintaCore.PaletteFormats.GetFormatByFilename (f) is PaletteDescriptor d && !d.IsWriteOnly ())
				.Order (StringComparer.CurrentCultureIgnoreCase);
			foreach (string file in files)
				palettes.Append (System.IO.Path.GetFileNameWithoutExtension (file), $"colorspanel.load-palette({GLib.Variant.NewString (file).Print (false)})");
		}
		menu.AppendSection (null, palettes);

		// Paint.NET's labels and sections; Pinta's Open... and Set Number of Colors go in a last section.
		EditActions edit = PintaCore.Actions.Edit;
		Gio.Menu fileCommands = Gio.Menu.New ();
		fileCommands.Append (Translations.GetString ("Save Current Palette As..."), edit.SavePalette.FullName);
		fileCommands.Append (Translations.GetString ("Open Palettes Folder"), "colorspanel.open-palettes-folder");
		menu.AppendSection (null, fileCommands);

		Gio.Menu reset = Gio.Menu.New ();
		reset.Append (Translations.GetString ("Reset to Default Palette"), edit.ResetPalette.FullName);
		menu.AppendSection (null, reset);

		Gio.Menu extras = Gio.Menu.New ();
		extras.AppendItem (edit.LoadPalette.CreateMenuItem ());
		extras.AppendItem (edit.ResizePalette.CreateMenuItem ());
		menu.AppendSection (null, extras);
	}

	private void LoadPaletteFile (string path)
	{
		try {
			palette.CurrentPalette.Load (PintaCore.PaletteFormats, Gio.FileHelper.NewForPath (path));
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to load palette {path}: {e.Message}");
		}
	}

	private async void OpenPalettesFolder ()
	{
		// Like Paint.NET, create the folder if it does not exist yet.
		try {
			System.IO.Directory.CreateDirectory (PalettesFolder);
			await Gtk.FileLauncher.New (Gio.FileHelper.NewForPath (PalettesFolder)).LaunchAsync (PintaCore.Chrome.MainWindow);
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to open the palettes folder: {e.Message}");
		}
	}

	private void HandleAddColorToggled ()
	{
		// Paint.NET highlights the palette while it waits for the click.
		palette_area.QueueDraw ();

		if (add_color.Active)
			PintaCore.Chrome.SetStatusBarText (" " + Translations.GetString ("Add Color: click a palette swatch to replace it with the active color. Click the button again to cancel."));
		else if (PintaCore.Tools.CurrentTool is BaseTool tool)
			PintaCore.Chrome.SetStatusBarText ($" {tool.Name}: {tool.StatusBarText}");
	}

	private int PaletteIndexAt (PointD p)
	{
		int col = (int) (p.X / CELL);
		int row = (int) (p.Y / CELL);
		int index = row * PALETTE_COLUMNS + col;
		return p.X >= 0 && p.Y >= 0 && col < PALETTE_COLUMNS && index < palette.CurrentPalette.Colors.Count ? index : -1;
	}

	private void HandlePaletteClick (PointD p, uint button)
	{
		int index = PaletteIndexAt (p);
		if (index < 0)
			return;

		Color color = palette.CurrentPalette.Colors[index];

		if (button == GtkExtensions.MOUSE_RIGHT_BUTTON) {
			SetSlotColor (false, color, addToRecent: true);
		} else if (button == GtkExtensions.MOUSE_LEFT_BUTTON) {
			if (add_color.Active) {
				palette.CurrentPalette.SetColor (index, ActiveColor);
				add_color.Active = false;
			} else {
				SetSlotColor (true, color, addToRecent: true);
			}
		}
	}

	private bool HandlePaletteTooltip (Gtk.Widget _, Gtk.Widget.QueryTooltipSignalArgs args)
	{
		int index = PaletteIndexAt (new PointD (args.X, args.Y));
		if (index < 0)
			return false;

		args.Tooltip.SetText ($"#{palette.CurrentPalette.Colors[index].ToHex ()}\n"
			+ Translations.GetString ("Left click to set the active color, right click to set the other one."));
		return true;
	}

	private void DrawPalette (Context g)
	{
		using Pattern checker = CairoExtensions.CreateTransparentBackgroundPattern (CELL / 2);
		var colors = palette.CurrentPalette.Colors;
		int visible = Math.Min (colors.Count, (palette_area.GetHeight () / CELL) * PALETTE_COLUMNS);

		for (int i = 0; i < visible; i++) {
			RectangleD r = new ((i % PALETTE_COLUMNS) * CELL, (i / PALETTE_COLUMNS) * CELL, CELL, CELL);
			if (colors[i].A < 1)
				g.FillRectangle (r, checker);
			g.FillRectangle (r, colors[i]);
		}

		if (add_color.Active && visible > 0) {
			int rows = (visible + PALETTE_COLUMNS - 1) / PALETTE_COLUMNS;
			g.DrawRectangle (new RectangleD (1, 1, PALETTE_COLUMNS * CELL - 2, rows * CELL - 2), new Color (0, 0.47, 0.84), 2);
		}

		g.Dispose ();
	}

	// --- Icons for the palette buttons

	// Paint.NET draws both as small framed tiles.
	private static void DrawTile (Context g, Color fill)
	{
		g.FillRectangle (new RectangleD (0, 0, 16, 16), new Color (0.62, 0.62, 0.62));
		g.FillRectangle (new RectangleD (1, 1, 14, 14), fill);
	}

	private static void DrawAddColorIcon (Context g)
	{
		// A black tile with a white plus at its bottom right.
		DrawTile (g, new Color (0, 0, 0));
		Color edge = new (0.25, 0.55, 0.5);
		g.FillRectangle (new RectangleD (9, 5, 4, 10), edge);
		g.FillRectangle (new RectangleD (6, 8, 10, 4), edge);
		g.FillRectangle (new RectangleD (10, 6, 2, 8), new Color (1, 1, 1));
		g.FillRectangle (new RectangleD (7, 9, 8, 2), new Color (1, 1, 1));
		g.Dispose ();
	}

	private static void DrawPaletteIcon (Context g)
	{
		// A white tile holding a 3x3 grid of colours.
		DrawTile (g, new Color (1, 1, 1));
		Color[] cells = [
			new (0.45, 0.25, 0.65), new (0.35, 0.3, 0.75), new (0.25, 0.45, 0.9),
			new (0.9, 0.35, 0.15), new (1, 1, 1), new (0.3, 0.55, 0.95),
			new (0.98, 0.75, 0.1), new (0.4, 0.7, 0.25), new (0.15, 0.6, 0.45),
		];
		for (int i = 0; i < cells.Length; i++)
			g.FillRectangle (new RectangleD (2 + (i % 3) * 4, 2 + (i / 3) * 4, 4, 4), cells[i]);
		g.Dispose ();
	}
}
