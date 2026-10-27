/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Roll control drawing and dragging ported from Paint.NET 3.36's RollControl. //
/////////////////////////////////////////////////////////////////////////////////

using System;
using Cairo;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.Effects;

/// <summary>
/// Paint.NET's Rotate / Zoom window: the roll ball with its angle, roll direction and roll amount sliders,
/// the pan box with X and Y sliders, then Zoom, Quality, Tiling Mode and Sampling.
/// </summary>
[GObject.Subclass<Gtk.Dialog>]
public sealed partial class RotateZoomDialog
{
	private RotateZoomData data = new ();
	private bool syncing;

	private RollControl roll = null!;
	private PanControl pan = null!;
	private ValueRow angle_row = null!, direction_row = null!, amount_row = null!;
	private ValueRow pan_x_row = null!, pan_y_row = null!, zoom_row = null!, quality_row = null!;

	partial void Initialize ()
	{
		Modal = true;
		Resizable = false;

		this.AddCancelOkButtons ();
		this.SetDefaultResponse (Gtk.ResponseType.Ok);
		this.PressOkOnEnter ();
	}

	public static RotateZoomDialog New (Gtk.Window parent, string title, string iconName, RotateZoomData data, IWorkspaceService workspace)
	{
		RotateZoomDialog dialog = NewWithProperties ([]);
		dialog.TransientFor = parent;
		dialog.Title = title;
		dialog.IconName = iconName;
		dialog.data = data;
		dialog.Build (workspace);
		return dialog;
	}

	private void Build (IWorkspaceService workspace)
	{
		Gtk.Box content = this.GetContentAreaBox ();
		content.WidthRequest = 330;
		content.SetAllMargins (6);
		content.Spacing = 4;

		// --- Roll / Rotate

		roll = RollControl.New ();
		roll.Angle = data.Angle;
		roll.RollDirection = data.RollDirection;
		roll.RollAmount = data.RollAmount;
		roll.ValueChanged += (_, _) => {
			if (syncing) return;
			data.Angle = roll.Angle;
			data.RollDirection = roll.RollDirection;
			data.RollAmount = roll.RollAmount;
			Changed ();
		};

		angle_row = new ValueRow (-180, 180, 0, data.Angle, 2, 1);
		direction_row = new ValueRow (-180, 180, 0, data.RollDirection, 2, 1);
		amount_row = new ValueRow (0, 90, 0, data.RollAmount, 2, 1);
		angle_row.Changed += v => { data.Angle = v; Changed (); };
		direction_row.Changed += v => { data.RollDirection = v; Changed (); };
		amount_row.Changed += v => { data.RollAmount = v; Changed (); };

		content.Append (Header (Translations.GetString ("Roll / Rotate")));
		content.Append (Beside (roll, angle_row.Widget, direction_row.Widget, amount_row.Widget));

		// --- Pan

		pan = PanControl.New (workspace);
		pan.Position = data.Pan;
		pan.PositionChanged += (_, _) => {
			if (syncing) return;
			data.Pan = pan.Position;
			Changed ();
		};

		pan_x_row = new ValueRow (-RotateZoomData.MaxPan, RotateZoomData.MaxPan, 0, data.Pan.X, 2, 0.01);
		pan_y_row = new ValueRow (-RotateZoomData.MaxPan, RotateZoomData.MaxPan, 0, data.Pan.Y, 2, 0.01);
		pan_x_row.Changed += v => { data.Pan = data.Pan with { X = v }; Changed (); };
		pan_y_row.Changed += v => { data.Pan = data.Pan with { Y = v }; Changed (); };

		content.Append (Header (Translations.GetString ("Pan")));
		content.Append (Beside (pan, pan_x_row.Widget, pan_y_row.Widget));

		// --- Zoom: the slider runs on a square scale, so 1 sits near the left as in Paint.NET.

		zoom_row = new ValueRow (0, RotateZoomData.MaxZoom, 1, data.Zoom, 2, 0.01,
			toSlider: z => Math.Sqrt (z / RotateZoomData.MaxZoom),
			fromSlider: p => RotateZoomData.MaxZoom * p * p);
		zoom_row.Changed += v => { data.Zoom = v; Changed (); };
		Gtk.Widget zoomHeader = Header (Translations.GetString ("Zoom"));
		zoomHeader.MarginTop = 8;
		content.Append (zoomHeader);
		content.Append (zoom_row.Widget);

		// --- Quality

		quality_row = new ValueRow (1, RotateZoomData.MaxQuality, 2, data.Quality, 0, 1);
		quality_row.Changed += v => { data.Quality = (int) Math.Round (v); Changed (); };
		content.Append (Header (Translations.GetString ("Quality")));
		content.Append (quality_row.Widget);

		// --- Tiling Mode, Sampling

		content.Append (LabeledDropDown (
			Translations.GetString ("Tiling Mode:"),
			[Translations.GetString ("None"), Translations.GetString ("Repeat"), Translations.GetString ("Mirror")],
			(int) data.TilingMode,
			68,
			i => { data.TilingMode = (RotateZoomTiling) i; Changed (); }));
		content.Append (LabeledDropDown (
			Translations.GetString ("Sampling:"),
			[Translations.GetString ("Bilinear"), Translations.GetString ("Nearest Neighbor")],
			(int) data.Sampling,
			125,
			i => { data.Sampling = (RotateZoomSampling) i; Changed (); }));

		// Like Paint.NET, open with the angle field focused and selected, so typing replaces it.
		OnMap += (_, _) => angle_row.FocusSpin ();
	}

	/// <summary>Pushes the data into every control, then tells the live preview.</summary>
	private void Changed ()
	{
		if (syncing) return;
		syncing = true;
		roll.Angle = data.Angle;
		roll.RollDirection = data.RollDirection;
		roll.RollAmount = data.RollAmount;
		pan.Position = data.Pan;
		angle_row.Value = data.Angle;
		direction_row.Value = data.RollDirection;
		amount_row.Value = data.RollAmount;
		pan_x_row.Value = data.Pan.X;
		pan_y_row.Value = data.Pan.Y;
		zoom_row.Value = data.Zoom;
		quality_row.Value = data.Quality;
		syncing = false;
		data.FirePropertyChanged ("_all_");
	}

	private static Gtk.Widget Header (string text)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.Xalign = 0;
		label.AddCssClass (AdwaitaStyles.Title4);
		return label;
	}

	private static Gtk.Widget Beside (Gtk.Widget picture, params Gtk.Widget[] rows)
	{
		Gtk.Box column = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		column.Hexpand = true;
		column.Valign = Gtk.Align.Center;
		foreach (Gtk.Widget row in rows)
			column.Append (row);

		picture.Valign = Gtk.Align.Start;
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 8);
		box.Append (picture);
		box.Append (column);
		return box;
	}

	private static Gtk.Widget LabeledDropDown (string label, string[] items, int active, int width, Action<int> changed)
	{
		Gtk.DropDown dropDown = Gtk.DropDown.NewFromStrings (items);
		dropDown.WidthRequest = width;
		dropDown.Selected = (uint) active;
		dropDown.OnNotify += (_, args) => {
			if (args.Pspec.GetName () == "selected")
				changed ((int) dropDown.Selected);
		};

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		box.MarginTop = 6;
		box.Append (Gtk.Label.New (label));
		box.Append (dropDown);
		return box;
	}

	/// <summary>
	/// A Paint.NET slider row with no caption: slider, stacked spin field and reset arrow.
	/// The slider can run on its own scale (0..1) when toSlider/fromSlider are given.
	/// </summary>
	private sealed class ValueRow
	{
		public Gtk.Widget Widget { get; }
		public event Action<double>? Changed;

		private readonly Gtk.Scale scale;
		private readonly Gtk.SpinButton spin;
		private readonly Func<double, double> to_slider;
		private readonly Func<double, double> from_slider;
		private bool updating;

		public ValueRow (
			double min, double max, double defaultValue, double value, int digits, double step,
			Func<double, double>? toSlider = null, Func<double, double>? fromSlider = null)
		{
			to_slider = toSlider ?? (v => v);
			from_slider = fromSlider ?? (v => v);
			bool mapped = toSlider is not null;

			scale = mapped
				? Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, 0, 1, 0.001)
				: Gtk.Scale.NewWithRange (Gtk.Orientation.Horizontal, min, max, step);
			scale.DrawValue = false;
			scale.Hexpand = true;
			scale.CanFocus = true;
			scale.AddMark (to_slider (defaultValue), Gtk.PositionType.Top, null);

			spin = Gtk.SpinButton.NewWithRange (min, max, step);
			spin.Digits = (uint) digits;
			spin.Numeric = true;
			spin.ClimbRate = 1;
			spin.WidthChars = 6;
			spin.SetActivatesDefaultImmediate (true);

			Gtk.Button reset = GtkExtensions.CreateResetButton ();
			reset.OnClicked += (_, _) => SetAndNotify (defaultValue);

			Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
			box.Append (scale);
			box.Append (spin.WithStackedStepButtons ());
			box.Append (reset);
			Widget = box;

			Value = value;

			scale.OnValueChanged += (_, _) => {
				if (!updating)
					SetAndNotify (Math.Clamp (from_slider (scale.GetValue ()), min, max));
			};
			spin.OnValueChanged += (_, _) => {
				if (!updating)
					SetAndNotify (spin.Value);
			};
		}

		public double Value {
			get => spin.Value;
			set {
				updating = true;
				if (spin.Value != value)
					spin.Value = value;
				scale.SetValue (to_slider (value));
				updating = false;
			}
		}

		private void SetAndNotify (double value)
		{
			Value = value;
			Changed?.Invoke (spin.Value);
		}

		public void FocusSpin ()
		{
			spin.GrabFocus ();
			spin.SelectRegion (0, -1);
		}
	}
}

/// <summary>
/// Paint.NET's roll ball: drag the outer ring to rotate (Shift snaps to 15 degrees), drag the globe to
/// roll (Shift keeps to one axis), double-click the globe to stand it back up.
/// </summary>
[GObject.Subclass<Gtk.DrawingArea>]
public sealed partial class RollControl
{
	private double angle, roll_direction, roll_amount;
	private bool hover, on_sphere, tracking;
	private PointD start_point;
	private PointD start_roll;
	private Gdk.ModifierType modifiers;

	public event EventHandler? ValueChanged;

	partial void Initialize ()
	{
		WidthRequest = HeightRequest = 79;
		SetDrawFunc ((_, g, w, h) => Draw (g, w, h));

		Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New ();
		motion.OnEnter += (_, a) => { hover = true; on_sphere = IsOnSphere (a.X, a.Y); QueueDraw (); };
		motion.OnMotion += (_, a) => {
			if (tracking) return;
			on_sphere = IsOnSphere (a.X, a.Y);
			QueueDraw ();
		};
		motion.OnLeave += (_, _) => { hover = false; QueueDraw (); };
		AddController (motion);

		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.SetButton (GtkExtensions.MOUSE_LEFT_BUTTON);
		drag.OnDragBegin += (_, a) => {
			tracking = true;
			start_point = new (a.StartX, a.StartY);
			on_sphere = IsOnSphere (a.StartX, a.StartY);
			start_roll = new (
				roll_amount * Math.Cos (roll_direction * Math.PI / 180),
				roll_amount * Math.Sin (roll_direction * Math.PI / 180));
			modifiers = drag.GetCurrentEventState ();
			Track (start_point);
		};
		drag.OnDragUpdate += (_, a) => {
			modifiers = drag.GetCurrentEventState ();
			Track (new (start_point.X + a.OffsetX, start_point.Y + a.OffsetY));
		};
		drag.OnDragEnd += (_, _) => tracking = false;
		AddController (drag);

		Gtk.GestureClick click = Gtk.GestureClick.New ();
		click.OnPressed += (_, a) => {
			if (a.NPress == 2 && IsOnSphere (a.X, a.Y))
				SetRoll (0, 0);
		};
		AddController (click);
	}

	public static new RollControl New () => NewWithProperties ([]);

	public double Angle {
		get => angle;
		set => Set (ref angle, Math.IEEERemainder (value, 360));
	}

	public double RollDirection {
		get => roll_direction;
		set => Set (ref roll_direction, Math.IEEERemainder (value, 360));
	}

	public double RollAmount {
		get => roll_amount;
		set => Set (ref roll_amount, Math.Clamp (value, 0, 90));
	}

	private void Set (ref double field, double value)
	{
		if (field == value) return;
		field = value;
		QueueDraw ();
		ValueChanged?.Invoke (this, EventArgs.Empty);
	}

	private void SetRoll (double direction, double amount)
	{
		roll_direction = direction;
		roll_amount = amount;
		QueueDraw ();
		ValueChanged?.Invoke (this, EventArgs.Empty);
	}

	private (PointD Center, double Diameter) Geometry ()
	{
		double diameter = Math.Min (GetWidth (), GetHeight ()) - 4;
		return (new (2 + diameter / 2, 2 + diameter / 2), diameter);
	}

	private bool IsOnSphere (double x, double y)
	{
		var (c, d) = Geometry ();
		return Math.Sqrt ((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y)) <= d / 3;
	}

	private void Track (PointD p)
	{
		var (c, d) = Geometry ();
		bool shift = modifiers.HasFlag (Gdk.ModifierType.ShiftMask);

		if (on_sphere) {
			double mx = start_roll.X / 89.9 + 3.0 * (p.X - start_point.X) / (d - 4);
			double my = start_roll.Y / 89.9 + 3.0 * (p.Y - start_point.Y) / (d - 4);
			double dist = Math.Sqrt (mx * mx + my * my);
			if (dist == 0) {
				mx = my = 0;
			} else if (dist > 1) {
				mx /= dist;
				my /= dist;
			}
			if (shift) {
				if (mx * mx > my * my) my = 0;
				else mx = 0;
			}
			SetRoll (180 * Math.Atan2 (my, mx) / Math.PI, 89.94 * Math.Sqrt (mx * mx + my * my));
		} else {
			double theta = Math.Atan2 (-(p.Y - c.Y), p.X - c.X) * 180 / Math.PI;
			Angle = shift ? Math.Round (theta / 15) * 15 : theta;
		}
	}

	private void Draw (Context g, int width, int height)
	{
		var (c, d) = Geometry ();
		double r1 = d / 3; // globe
		double r2 = d / 2; // ring
		Color accent = new (0, 0.47, 0.84);

		g.Antialias = Antialias.Best;
		g.LineWidth = 1;

		// Outer ring.
		g.Arc (c.X, c.Y, r2 - 0.5, 0, 2 * Math.PI);
		g.SetSourceColor (new Color (0.66, 0.66, 0.66));
		g.Stroke ();

		// Shaded globe.
		using (RadialGradient shade = new (c.X - r1 * 0.35, c.Y - r1 * 0.35, 0, c.X, c.Y, r1)) {
			shade.AddColorStop (0, new Color (0.98, 0.98, 0.98));
			shade.AddColorStop (0.7, new Color (0.86, 0.86, 0.86));
			shade.AddColorStop (1, new Color (0.62, 0.62, 0.62));
			g.Arc (c.X, c.Y, r1, 0, 2 * Math.PI);
			g.SetSource (shade);
			g.Fill ();
		}

		// Meridians and parallels of the front hemisphere, tipped by the roll.
		double rx = roll_amount * Math.Cos (roll_direction * Math.PI / 180) / 90;
		double ry = roll_amount * Math.Sin (roll_direction * Math.PI / 180) / 90;
		double a = angle * Math.PI / 180;
		g.Save ();
		g.Translate (c.X, c.Y);
		g.LineWidth = 0.8;
		g.SetSourceColor (hover && on_sphere ? accent : new Color (0.55, 0.55, 0.55, 0.7));

		const int MERIDIANS = 8, PARALLELS = 10, STEPS = 32;
		for (int i = 0; i < MERIDIANS; i++) {
			double u = -a + i * 2 * Math.PI / MERIDIANS;
			for (int j = 0; j < STEPS; j++) {
				double v0 = -Math.PI / 2 + j * Math.PI / 2 / STEPS;
				double v1 = v0 + Math.PI / 2 / STEPS;
				Line3D (g, rx, -ry, r1, Sphere (u, v0), Sphere (u, v1));
			}
		}
		for (int j = 1; j <= PARALLELS; j++) {
			double v = -Math.PI / 2 + j * Math.PI / 2 / PARALLELS;
			for (int i = 0; i < STEPS * 2; i++) {
				double u0 = -a + i * Math.PI / STEPS;
				Line3D (g, rx, -ry, r1, Sphere (u0, v), Sphere (u0 + Math.PI / STEPS, v));
			}
		}
		g.Stroke ();
		g.Restore ();

		// Globe outline.
		g.Arc (c.X, c.Y, r1, 0, 2 * Math.PI);
		g.SetSourceColor (new Color (0.42, 0.42, 0.42));
		g.LineWidth = 1.2;
		g.Stroke ();

		// The angle line across the ring.
		double t = -angle * Math.PI / 180;
		g.MoveTo (c.X + r1 * Math.Cos (t), c.Y + r1 * Math.Sin (t));
		g.LineTo (c.X + r2 * Math.Cos (t), c.Y + r2 * Math.Sin (t));
		g.SetSourceColor (hover && !on_sphere || tracking && !on_sphere ? accent : new Color (0.5, 0.5, 0.5));
		g.LineWidth = hover && !on_sphere ? 2 : 1;
		g.Stroke ();
	}

	private static (double X, double Y, double Z) Sphere (double u, double v)
		=> (Math.Cos (u) * Math.Cos (v), Math.Sin (u) * Math.Cos (v), Math.Sin (v));

	/// <summary>Adds the segment to the path when both ends face the viewer.</summary>
	private static void Line3D (Context g, double rx, double ry, double scale, (double X, double Y, double Z) s, (double X, double Y, double Z) e)
	{
		double dist = Math.Sqrt (rx * rx + ry * ry);
		if (dist != 0) {
			double ang = Math.Atan2 (ry, rx);
			double sa = Math.Sin (ang), ca = Math.Cos (ang);
			double sm = Math.Min (dist, 1), cm = Math.Sqrt (1 - sm * sm);
			s = Roll (s, sa, ca, sm, cm);
			e = Roll (e, sa, ca, sm, cm);
		}
		if (s.Z >= 0.03 || e.Z >= 0.03)
			return;
		g.MoveTo (s.X * scale, s.Y * scale);
		g.LineTo (e.X * scale, e.Y * scale);
	}

	private static (double X, double Y, double Z) Roll ((double X, double Y, double Z) p, double sinAngle, double cosAngle, double sinAmount, double cosAmount)
	{
		double x = cosAngle * p.X - sinAngle * p.Y;
		double y = sinAngle * p.X + cosAngle * p.Y;
		double x2 = cosAmount * x - sinAmount * p.Z;
		double z = sinAmount * x + cosAmount * p.Z;
		return (cosAngle * x2 + sinAngle * y, -sinAngle * x2 + cosAngle * y, z);
	}
}

/// <summary>
/// Paint.NET's pan box: a thumbnail of the layer with the move cross at the image centre's new place.
/// The box spans -1..1 on each axis; dragging beyond it pans further.
/// </summary>
[GObject.Subclass<Gtk.DrawingArea>]
public sealed partial class PanControl
{
	private const int BOX = 78;

	private PointD position;
	private PointD drag_start;
	private ImageSurface? thumbnail;
	private IWorkspaceService workspace = null!; // NRT - set by New

	public event EventHandler? PositionChanged;

	partial void Initialize ()
	{
		SetDrawFunc ((_, g, w, h) => Draw (g, w, h));
		SetCursorFromName ("grab");

		Gtk.GestureDrag drag = Gtk.GestureDrag.New ();
		drag.SetButton (GtkExtensions.MOUSE_LEFT_BUTTON);
		drag.OnDragBegin += (_, a) => {
			drag_start = new (a.StartX, a.StartY);
			SetCursorFromName ("grabbing");
			Position = FromClient (drag_start);
		};
		drag.OnDragUpdate += (_, a) => Position = FromClient (new (drag_start.X + a.OffsetX, drag_start.Y + a.OffsetY));
		drag.OnDragEnd += (_, _) => SetCursorFromName ("grab");
		AddController (drag);
	}

	public static PanControl New (IWorkspaceService workspace)
	{
		PanControl control = NewWithProperties ([]);
		control.workspace = workspace;
		Size image = workspace.ImageSize;
		double fit = Math.Min ((double) BOX / image.Width, (double) BOX / image.Height);
		control.WidthRequest = Math.Max (8, (int) Math.Round (image.Width * fit)) + 2;
		control.HeightRequest = Math.Max (8, (int) Math.Round (image.Height * fit)) + 2;
		return control;
	}

	public PointD Position {
		get => position;
		set {
			if (position == value) return;
			position = value;
			QueueDraw ();
			PositionChanged?.Invoke (this, EventArgs.Empty);
		}
	}

	private PointD FromClient (PointD p)
	{
		double w = GetWidth () - 2, h = GetHeight () - 2;
		return new (Math.Round (2 * (p.X - 1) / w - 1, 2), Math.Round (2 * (p.Y - 1) / h - 1, 2));
	}

	private void Draw (Context g, int width, int height)
	{
		int w = width - 2, h = height - 2;

		if (thumbnail is null || thumbnail.Width != w || thumbnail.Height != h) {
			thumbnail?.Dispose ();
			thumbnail = CairoExtensions.CreateImageSurface (Format.Argb32, w, h);
			using Context t = new (thumbnail);
			t.SetSource (CairoExtensions.CreateTransparentBackgroundPattern (4));
			t.Paint ();
			ImageSurface layer = workspace.ActiveDocument.Layers.CurrentUserLayer.Surface;
			t.Scale ((double) w / layer.Width, (double) h / layer.Height);
			t.SetSourceSurface (layer, 0, 0);
			t.Paint ();
		}

		g.SetSourceSurface (thumbnail, 1, 1);
		g.Paint ();

		g.Rectangle (0.5, 0.5, width - 1, height - 1);
		g.SetSourceColor (new Color (0.6, 0.6, 0.6));
		g.LineWidth = 1;
		g.Stroke ();

		// The move cross: a plus with arrow heads, black over a white outline so it shows on any image.
		double cx = Math.Round (1 + (1 + position.X) * w / 2) + 0.5;
		double cy = Math.Round (1 + (1 + position.Y) * h / 2) + 0.5;
		g.Save ();
		g.Rectangle (1, 1, w, h);
		g.Clip ();
		MoveCross (g, cx, cy, new Color (1, 1, 1), 3);
		MoveCross (g, cx, cy, new Color (0, 0, 0), 1.4);
		g.Restore ();
	}

	private static void MoveCross (Context g, double x, double y, Color color, double width)
	{
		const double ARM = 6, HEAD = 2.5;
		g.SetSourceColor (color);
		g.LineWidth = width;
		g.LineCap = LineCap.Square;
		g.MoveTo (x - ARM, y); g.LineTo (x + ARM, y);
		g.MoveTo (x, y - ARM); g.LineTo (x, y + ARM);
		foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
			double tx = x + dx * ARM, ty = y + dy * ARM;
			g.MoveTo (tx - dx * HEAD + dy * HEAD, ty - dy * HEAD + dx * HEAD);
			g.LineTo (tx, ty);
			g.LineTo (tx - dx * HEAD - dy * HEAD, ty - dy * HEAD - dx * HEAD);
		}
		g.Stroke ();
	}
}
