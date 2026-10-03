//
// LineCurveEditEngine.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2014 Andrew Davis, GSoC 2014
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
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// The Line/Curve tool's editing: four nubs, Straight / Spline / Bézier curve types, start and end
/// caps, a move icon off the end, and right-drag to rotate about the centre.
/// </summary>
public sealed class LineCurveEditEngine : BaseEditEngine
{
	private const string CURVE_TYPE_SETTING_SUFFIX = "-curve-type";
	private const string START_CAP_SETTING_SUFFIX = "-start-cap";
	private const string END_CAP_SETTING_SUFFIX = "-end-cap";

	private static readonly LineCapStyle[] caps = Enum.GetValues<LineCapStyle> ();

	private readonly Gtk.ToggleButton[] curve_buttons = new Gtk.ToggleButton[3];
	private bool curve_buttons_created = false;
	private CurveType curve_type = CurveType.Spline;
	private Gtk.Separator? curve_sep;
	private GlyphPicker? start_cap_picker;
	private GlyphPicker? end_cap_picker;

	public LineCurveEditEngine (IServiceProvider services, ShapeTool owner)
		: base (services, owner, LineShape.NUB_COUNT)
	{ }

	// Window pixels around a thin line that still count as on it.
	private const double LINE_GRAB_MARGIN = 6;

	/// <summary>Only the line itself (not the empty space around it) can be dragged; elsewhere a drag starts a new line.</summary>
	protected override bool IsOnShape (EditableShape shape, PointD[] outline, PointD windowPoint)
	{
		PointD[] path = CurveGeometry.Flatten (CurveGeometry.GetSegments (((LineShape) shape).Points, curve_type))
			.Select (workspace.CanvasPointToView).ToArray ();
		double reach = Math.Max (LINE_GRAB_MARGIN, BrushWidth * workspace.GetScale () / 2 + 2);

		for (int i = 0; i + 1 < path.Length; i++)
			if (TransformFrame.DistanceToSegment (windowPoint, path[i], path[i + 1]) <= reach)
				return true;
		return false;
	}

	private LineCapStyle StartCap => caps[start_cap_picker?.SelectedIndex ?? 0];
	private LineCapStyle EndCap => caps[end_cap_picker?.SelectedIndex ?? 0];

	public override void BuildToolBar (Gtk.Box tb, ISettingsService settings, string toolPrefix)
	{
		if (!curve_buttons_created) {
			curve_type = (CurveType) Math.Clamp (settings.GetSetting (toolPrefix + CURVE_TYPE_SETTING_SUFFIX, (int) CurveType.Spline), 0, 2);

			(CurveType Type, string Name)[] types = [
				(CurveType.Straight, Translations.GetString ("Straight")),
				(CurveType.Spline, Translations.GetString ("Spline")),
				(CurveType.Bezier, Translations.GetString ("Bézier")),
			];

			foreach ((CurveType type, string name) in types) {
				Gtk.ToggleButton button = Gtk.ToggleButton.New ();
				button.Child = Gtk.Image.NewFromPaintable (CreateCurveTypeGlyph (type));
				button.TooltipText = name;
				button.HasFrame = false;
				button.CanFocus = false;
				button.FocusOnClick = false;
				button.Active = type == curve_type;
				button.OnClicked += (_, _) => SetCurveType (type);
				curve_buttons[(int) type] = button;
			}

			curve_buttons_created = true;
		}

		foreach (Gtk.ToggleButton button in curve_buttons)
			tb.Append (button);
		AppendSeparator (tb, ref curve_sep);

		AppendBrushWidth (tb, settings, toolPrefix, Translations.GetString ("Brush width"));

		AppendStyleLabel (tb);
		start_cap_picker ??= CreateCapPicker (settings, toolPrefix + START_CAP_SETTING_SUFFIX, start: true);
		end_cap_picker ??= CreateCapPicker (settings, toolPrefix + END_CAP_SETTING_SUFFIX, start: false);
		tb.Append (start_cap_picker.Button);
		AppendDashPicker (tb, settings, toolPrefix);
		tb.Append (end_cap_picker.Button);
	}

	public override void OnSaveSettings (ISettingsService settings, string toolPrefix)
	{
		base.OnSaveSettings (settings, toolPrefix);

		settings.PutSetting (toolPrefix + CURVE_TYPE_SETTING_SUFFIX, (int) curve_type);

		if (start_cap_picker is not null)
			settings.PutSetting (toolPrefix + START_CAP_SETTING_SUFFIX, start_cap_picker.SelectedIndex);

		if (end_cap_picker is not null)
			settings.PutSetting (toolPrefix + END_CAP_SETTING_SUFFIX, end_cap_picker.SelectedIndex);
	}

	private void SetCurveType (CurveType type)
	{
		// The buttons act as a radio group; clicking the active one keeps it active.
		curve_type = type;
		for (int i = 0; i < curve_buttons.Length; i++)
			if (curve_buttons[i].Active != (i == (int) type))
				curve_buttons[i].Active = i == (int) type;

		Redraw ();
	}

	private GlyphPicker CreateCapPicker (ISettingsService settings, string key, bool start)
	{
		(LineCapStyle Cap, string Name)[] names = [
			(LineCapStyle.Flat, Translations.GetString ("Flat")),
			(LineCapStyle.Arrow, Translations.GetString ("Arrow")),
			(LineCapStyle.FilledArrow, Translations.GetString ("Filled Arrow")),
			(LineCapStyle.Rounded, Translations.GetString ("Rounded")),
		];

		GlyphPicker picker = new (
			names.Select (n => new GlyphPicker.Item (n.Name, CreateCapGlyph (n.Cap, start))).ToArray (),
			columns: 1, showNameOnButton: false, showNamesInList: true);
		picker.SelectedIndex = settings.GetSetting (key, 0);
		picker.Changed += (_, _) => Redraw ();
		return picker;
	}

	/// <summary>Comma, period and slash cycle the start cap, the dash style and the end cap (Shift goes back).</summary>
	protected override bool HandleToolKey (ToolKeyEventArgs e)
	{
		if (e.IsControlPressed || e.IsAltPressed)
			return false;

		GlyphPicker? picker = e.Key.Value switch {
			Gdk.Constants.KEY_comma or Gdk.Constants.KEY_less => start_cap_picker,
			Gdk.Constants.KEY_period or Gdk.Constants.KEY_greater => DashPicker,
			Gdk.Constants.KEY_slash or Gdk.Constants.KEY_question => end_cap_picker,
			_ => null,
		};

		if (picker is null)
			return false;

		picker.Cycle (reverse: e.IsShiftPressed);
		return true;
	}

	protected override EditableShape CreateShape (PointD start, bool useSecondaryColor)
		=> LineShape.FromEnds (start, start, useSecondaryColor);

	protected override void UpdateCreate (EditableShape shape, PointD origin, PointD current, bool shift, bool alt)
	{
		PointD d = new (current.X - origin.X, current.Y - origin.Y);

		// Shift snaps to 15 degree steps.
		if (shift) {
			double length = Math.Sqrt (d.X * d.X + d.Y * d.Y);
			double angle = Math.Round (Math.Atan2 (d.Y, d.X) / (Math.PI / 12)) * (Math.PI / 12);
			d = new (length * Math.Cos (angle), length * Math.Sin (angle));
		}

		// Alt draws the line centred on the starting point.
		PointD start = alt ? new (origin.X - d.X, origin.Y - d.Y) : origin;
		((LineShape) shape).SetEnds (start, new (origin.X + d.X, origin.Y + d.Y));
	}

	protected override bool IsDegenerate (EditableShape shape)
	{
		PointD[] p = ((LineShape) shape).Points;
		return p[0].DistanceSquared (p[^1]) < 1;
	}

	protected override void DragNub (EditableShape shape, int nub, PointD current, bool shift, bool alt)
		=> ((LineShape) shape).Points[nub] = current;

	protected override Matrix ComputeRotation (EditableShape start, PointD from, PointD to, bool snap)
		=> RotationAbout (start.Pivot, from, to, snap);

	protected override PointD MoveIconAnchor (EditableShape shape)
		=> ((LineShape) shape).Points[^1];

	protected override RectangleD DrawShape (Context g, EditableShape shape, Color outline, Color fill)
	{
		IReadOnlyList<CubicSegment> segments = CurveGeometry.GetSegments (((LineShape) shape).Points, curve_type);
		double width = BrushWidth;

		g.MoveTo (segments[0].Start.X, segments[0].Start.Y);
		foreach (CubicSegment s in segments)
			g.CurveTo (s.Control1.X, s.Control1.Y, s.Control2.X, s.Control2.Y, s.End.X, s.End.Y);

		g.LineWidth = width;
		g.LineJoin = LineJoin.Round;
		bool dashed = g.SetDashFromString (DashPattern, width, LineCap.Square);
		g.LineCap = dashed ? LineCap.Square : LineCap.Butt;
		g.SetSourceColor (outline);

		RectangleD dirty = g.StrokeExtents ();
		g.Stroke ();
		g.SetDash ([], 0);

		dirty = dirty.Union (DrawCap (g, StartCap, CurveGeometry.Reverse (segments[0]), width, outline));
		dirty = dirty.Union (DrawCap (g, EndCap, segments[^1], width, outline));
		return dirty;
	}

	/// <summary>Draws a cap at the end of <paramref name="segment"/>, pointing the way the curve leaves it.</summary>
	private static RectangleD DrawCap (Context g, LineCapStyle cap, CubicSegment segment, double width, Color color)
	{
		PointD tip = segment.End;
		PointD dir = CurveGeometry.EndDirection (segment);
		PointD normal = new (-dir.Y, dir.X);

		g.SetSourceColor (color);

		switch (cap) {
			case LineCapStyle.Rounded: {
					g.Arc (tip.X, tip.Y, width / 2, 0, 2 * Math.PI);
					RectangleD dirty = g.PathExtents ();
					g.Fill ();
					return dirty;
				}
			case LineCapStyle.Arrow:
			case LineCapStyle.FilledArrow: {
					// The head grows with the brush. A filled head reaches a brush width past the end,
					// so it covers the square end of a thick line.
					double length = 3 + width * 3;
					double half = 2 + width * 2;
					if (cap == LineCapStyle.FilledArrow)
						tip = new (tip.X + dir.X * width, tip.Y + dir.Y * width);
					PointD back = new (tip.X - dir.X * length, tip.Y - dir.Y * length);
					PointD left = new (back.X + normal.X * half, back.Y + normal.Y * half);
					PointD right = new (back.X - normal.X * half, back.Y - normal.Y * half);

					g.MoveTo (left.X, left.Y);
					g.LineTo (tip.X, tip.Y);
					g.LineTo (right.X, right.Y);

					RectangleD dirty;
					if (cap == LineCapStyle.FilledArrow) {
						g.ClosePath ();
						dirty = g.PathExtents ();
						g.Fill ();
					} else {
						g.LineWidth = width;
						g.LineJoin = LineJoin.Miter;
						g.LineCap = LineCap.Butt;
						dirty = g.StrokeExtents ();
						g.Stroke ();
					}
					return dirty;
				}
			default:
				return new RectangleD (tip, 0, 0);
		}
	}

	private static Gdk.Texture CreateCurveTypeGlyph (CurveType type)
		=> GlyphPicker.CreateGlyph (20, 20, g => {
			// The same four nubs drawn each way: a zigzag, a wave through them, and a curve pulled towards them.
			PointD[] nubs = [new (2, 16), new (6, 3), new (14, 17), new (18, 4)];
			IReadOnlyList<CubicSegment> segments = CurveGeometry.GetSegments (nubs, type);
			g.MoveTo (segments[0].Start.X, segments[0].Start.Y);
			foreach (CubicSegment s in segments)
				g.CurveTo (s.Control1.X, s.Control1.Y, s.Control2.X, s.Control2.Y, s.End.X, s.End.Y);
			g.SetSourceColor (GlyphPicker.GlyphStroke);
			g.LineWidth = 2;
			g.LineJoin = LineJoin.Round;
			g.Stroke ();
		});

	private static Gdk.Texture CreateCapGlyph (LineCapStyle cap, bool start)
		=> GlyphPicker.CreateGlyph (28, 16, g => {
			// The line runs towards the cap's end of the glyph: left for a start cap, right for an end cap.
			PointD tip = start ? new (4, 8) : new (24, 8);
			PointD other = start ? new (27, 8) : new (1, 8);
			g.MoveTo (other.X, other.Y);
			g.LineTo (tip.X, tip.Y);
			g.LineWidth = 3;
			g.SetSourceColor (GlyphPicker.GlyphStroke);
			g.Stroke ();
			DrawCap (g, cap, new CubicSegment (other, other, tip, tip), 3, GlyphPicker.GlyphStroke);
		});
}
