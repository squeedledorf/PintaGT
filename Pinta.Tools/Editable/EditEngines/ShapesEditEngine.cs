using System;
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// The Shapes tool's editing: a preset drawn in a box with 8 nubs that resize it (Shift keeps the
/// aspect ratio, Alt resizes about the centre), a movable centre of rotation, a move icon, and
/// rotation by dragging just outside the box or right-dragging on it.
/// </summary>
public sealed class ShapesEditEngine : BaseEditEngine
{
	private const string PRESET_SETTING_SUFFIX = "-preset";

	private GlyphPicker? preset_picker;
	private Gtk.Separator? style_sep;
	private Gtk.Label? radius_label;
	private Gtk.SpinButton? radius_spin;

	public ShapesEditEngine (IServiceProvider services, ShapeTool owner)
		: base (services, owner, TransformFrame.NUB_COUNT)
	{ }

	private ShapePreset Preset => ShapePresets.All[preset_picker?.SelectedIndex ?? 0];

	private double CornerRadius => radius_spin?.Value ?? ShapePresets.DefaultCornerRadius;

	protected override bool HasRotateCorridor => true;
	protected override bool HasPivotHandle => true;

	public override void BuildToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (preset_picker is null) {
			preset_picker = new GlyphPicker (
				ShapePresets.All.Select (p => new GlyphPicker.Item (ShapePresets.GetName (p), CreatePresetGlyph (p), GroupOf (p))).ToArray (),
				columns: 8, showNameOnButton: true, showNamesInList: false);
			preset_picker.SelectedIndex = settings.GetSetting (toolPrefix + PRESET_SETTING_SUFFIX, 0);
			preset_picker.Changed += (_, _) => {
				UpdateRadiusVisibility ();
				Redraw ();
			};
		}

		tb.Append (preset_picker.Button);
		AppendFillMode (tb, settings, toolPrefix);
		AppendBrushWidth (tb, settings, toolPrefix, Translations.GetString ("Brush size"));
		AppendStyleLabel (tb);
		AppendDashPicker (tb, settings, toolPrefix);
		AppendSeparator (tb, ref style_sep);

		// Paint.NET's Corner Size, shown for the rounded presets only.
		radius_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Corner size")));
		if (radius_spin is null) {
			radius_spin = GtkExtensions.CreateToolBarSpinButton (0, 1e4, 1, settings.GetSetting (SettingNames.Radius (toolPrefix), (int) ShapePresets.DefaultCornerRadius));
			radius_spin.OnValueChanged += (_, _) => Redraw ();
		}
		tb.Append (radius_label);
		tb.Append (radius_spin);
		UpdateRadiusVisibility ();
	}

	public override void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		base.OnSaveSettings (settings, toolPrefix);

		if (preset_picker is not null)
			settings.PutSetting (toolPrefix + PRESET_SETTING_SUFFIX, preset_picker.SelectedIndex);

		if (radius_spin is not null)
			settings.PutSetting (SettingNames.Radius (toolPrefix), radius_spin.GetValueAsInt ());
	}

	private void UpdateRadiusVisibility ()
	{
		bool visible = ShapePresets.HasCornerRadius (Preset);
		if (radius_label is not null)
			radius_label.Visible = visible;
		if (radius_spin is not null)
			radius_spin.Visible = visible;
		if (style_sep is not null)
			style_sep.Visible = visible;
	}

	private static string? GroupOf (ShapePreset preset)
		=> ShapePresets.Groups.Last (g => g.First <= preset).Name;

	/// <summary>A / Shift+A cycle the presets.</summary>
	protected override bool HandleToolKey (ToolKeyEventArgs e)
	{
		if (e.Key.ToUpper ().Value != Gdk.Constants.KEY_A || e.IsControlPressed || e.IsAltPressed || preset_picker is null)
			return false;

		preset_picker.Cycle (reverse: e.IsShiftPressed);
		return true;
	}

	protected override EditableShape CreateShape (PointD start, bool useSecondaryColor)
	{
		BoxShape shape = new () { UseSecondaryColor = useSecondaryColor };
		shape.Frame.Reset (new RectangleD (start, 0, 0));
		return shape;
	}

	protected override void UpdateCreate (EditableShape shape, PointD origin, PointD current, bool shift, bool alt)
	{
		double dx = current.X - origin.X;
		double dy = current.Y - origin.Y;

		// Shift draws a square box; Alt draws it about the starting point.
		if (shift) {
			double side = Math.Max (Math.Abs (dx), Math.Abs (dy));
			dx = side * (dx < 0 ? -1 : 1);
			dy = side * (dy < 0 ? -1 : 1);
		}

		PointD a = alt ? new (origin.X - dx, origin.Y - dy) : origin;
		PointD b = new (origin.X + dx, origin.Y + dy);

		((BoxShape) shape).Frame.Reset (RectangleD.FromPoints (a, b));
	}

	protected override bool IsDegenerate (EditableShape shape)
	{
		RectangleD r = ((BoxShape) shape).Frame.Rect;
		return r.Width < 1 || r.Height < 1;
	}

	protected override void DragNub (EditableShape shape, int nub, PointD current, bool shift, bool alt)
	{
		BoxShape box = (BoxShape) shape;
		shape.Transform (box.Frame.ComputeScale (nub, current, keepAspect: shift, fromCenter: alt));
	}

	protected override Matrix ComputeRotation (EditableShape start, PointD from, PointD to, bool snap)
		=> ((BoxShape) start).Frame.ComputeRotation (from, to, snap);

	protected override PointD MoveIconAnchor (EditableShape shape)
		=> ((BoxShape) shape).Frame.GetNub (4);

	protected override RectangleD DrawShape (Context g, EditableShape shape, Color outline, Color fill)
	{
		BoxShape box = (BoxShape) shape;

		foreach (PointD[] contour in box.GetContours (Preset, CornerRadius)) {
			if (contour.Length == 0)
				continue;
			g.MoveTo (contour[0].X, contour[0].Y);
			for (int i = 1; i < contour.Length; i++)
				g.LineTo (contour[i].X, contour[i].Y);
			g.ClosePath ();
		}

		g.FillRule = FillRule.EvenOdd;
		g.LineWidth = BrushWidth;
		g.LineJoin = LineJoin.Miter;
		bool dashed = g.SetDashFromString (DashPattern, BrushWidth, LineCap.Square);
		g.LineCap = dashed ? LineCap.Square : LineCap.Butt;

		// Extents are taken before the path is used up; the stroke's covers the fill too.
		RectangleD dirty = StrokeShape ? g.StrokeExtents () : g.PathExtents ();

		if (FillShape) {
			g.SetSourceColor (fill);
			g.FillPreserve ();
		}

		if (StrokeShape) {
			g.SetSourceColor (outline);
			g.StrokePreserve ();
		}

		g.NewPath ();
		g.SetDash ([], 0);
		return dirty;
	}

	private static Gdk.Texture CreatePresetGlyph (ShapePreset preset)
		=> GlyphPicker.CreateGlyph (20, 20, g => {
			foreach (PointD[] contour in ShapePresets.GetContours (preset, 16, 16, 4)) {
				g.MoveTo (contour[0].X + 2, contour[0].Y + 2);
				foreach (PointD p in contour.Skip (1))
					g.LineTo (p.X + 2, p.Y + 2);
				g.ClosePath ();
			}
			g.FillRule = FillRule.EvenOdd;
			g.LineJoin = LineJoin.Round;
			g.LineWidth = 1;
			g.SetSourceColor (GlyphPicker.GlyphFill);
			g.FillPreserve ();
			g.SetSourceColor (GlyphPicker.GlyphStroke);
			g.Stroke ();
		});
}
