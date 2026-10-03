//
// SelectionModeHandler.cs
//
// Author:
//       Andrew Davis <andrew.3.1415@gmail.com>
//
// Copyright (c) 2013 Andrew Davis, GSoC 2013
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
using ClipperLib;

namespace Pinta.Core;

public sealed class SelectionModeHandler
{
	private Gtk.Box? mode_box;
	private int selected_index;

	private CombineMode selected_mode;

	// Paint.NET's names and icons, in Paint.NET's order.
	private readonly (string Label, string Icon, CombineMode Mode)[] combine_modes;

	// The combo order used to be Replace/Union/Exclude/Xor/Intersect, so the index is now
	// stored under a new key and an index stored under the old key is remapped once.
	private const string COMBINE_MODE_SETTING = "selection-combine-mode-pdn";
	private static readonly int[] legacy_index_map = [0, 1, 2, 4, 3];

	public SelectionModeHandler (SystemManager system)
	{
		combine_modes = [
			(Translations.GetString ("Replace"), Resources.Icons.SelectionModeReplace, CombineMode.Replace),
			(Translations.GetString ("Add (union)"), Resources.Icons.SelectionModeUnion, CombineMode.Union),
			(Translations.GetString ("Subtract"), Resources.Icons.SelectionModeExclude, CombineMode.Exclude),
			(Translations.GetString ("Intersect"), Resources.Icons.SelectionModeIntersect, CombineMode.Intersect),
			(Translations.GetString ("Invert (xor)"), Resources.Icons.SelectionModeXor, CombineMode.Xor),
		];
	}

	/// <summary>
	/// As in Paint.NET, the five selection modes are a row of icon toggle buttons with no label.
	/// </summary>
	public void BuildToolbar (Gtk.Box tb, ISettingsService settings)
	{
		if (mode_box is null) {
			int index = settings.GetSetting (COMBINE_MODE_SETTING, -1);
			if (index < 0) {
				int legacy = settings.GetSetting (SettingNames.SELECTION_COMBINE_MODE, 0);
				index = legacy >= 0 && legacy < legacy_index_map.Length ? legacy_index_map[legacy] : 0;
			}

			selected_index = Math.Clamp (index, 0, combine_modes.Length - 1);
			selected_mode = combine_modes[selected_index].Mode;

			mode_box = Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
			Gtk.ToggleButton? group = null;

			for (int i = 0; i < combine_modes.Length; i++) {
				int mode_index = i;
				Gtk.ToggleButton button = Gtk.ToggleButton.New ();
				button.IconName = combine_modes[i].Icon;
				button.TooltipText = combine_modes[i].Label;
				button.HasFrame = false;
				button.CanFocus = false;
				button.FocusOnClick = false;
				if (group is null)
					group = button;
				else
					button.SetGroup (group);
				button.Active = i == selected_index;
				button.OnToggled += (_, _) => {
					if (!button.Active)
						return;
					selected_index = mode_index;
					selected_mode = combine_modes[mode_index].Mode;
				};
				mode_box.Append (button);
			}
		}

		tb.Append (mode_box);
	}

	/// <summary>
	/// Determine the current combine mode. As in Paint.NET, modifiers override the toolbar mode:
	/// Left: Ctrl = Add (union), Alt = Subtract. Right: Ctrl = Invert (xor), Alt = Intersect.
	/// A plain click with either button uses the toolbar mode.
	/// </summary>
	public CombineMode DetermineCombineMode (ToolMouseEventArgs args)
		=> DetermineCombineMode (args.MouseButton, args.IsControlPressed, args.IsAltPressed, selected_mode);

	public static CombineMode DetermineCombineMode (MouseButton button, bool ctrl, bool alt, CombineMode toolbarMode)
	{
		switch (button) {
			case MouseButton.Left:
				if (ctrl)
					return CombineMode.Union;
				else if (alt)
					return CombineMode.Exclude;
				else
					return toolbarMode;
			case MouseButton.Right:
				if (ctrl)
					return CombineMode.Xor;
				else if (alt)
					return CombineMode.Intersect;
				else
					return toolbarMode;
			default:
				return toolbarMode;
		}
	}

	public static void PerformSelectionMode (Document doc, CombineMode mode, List<List<IntPoint>> polygons)
	{
		doc.Selection = doc.PreviousSelection.Clone ();
		doc.Selection.Visible = true;

		//Make sure time isn't wasted if the CombineMode is Replace - Replace is much simpler than the other 4 selection modes.
		switch (mode) {
			case CombineMode.Replace:
				PerformSelectionReplace (doc, polygons);
				break;
			default:
				PerformSelectionWithMode (doc, mode, polygons);
				break;
		}

		doc.Selection.MarkDirty ();
	}

	private static void PerformSelectionWithMode (Document doc, CombineMode mode, List<List<IntPoint>> polygons)
	{
		List<List<IntPoint>> resultingPolygons = new ();

		//Specify the Clipper Subject (the previous Polygons) and the Clipper Clip (the new Polygons).
		//Note: for Union, ignore the Clipper Library instructions - the new polygon(s) should be Clips, not Subjects!
		doc.Selection.SelectionClipper.AddPaths (doc.Selection.SelectionPolygons, PolyType.ptSubject, true);
		doc.Selection.SelectionClipper.AddPaths (polygons, PolyType.ptClip, true);

		switch (mode) {
			case CombineMode.Xor:
				//Xor means "Combine both Polygon sets, but leave out any areas of intersection between the two."
				doc.Selection.SelectionClipper.Execute (ClipType.ctXor, resultingPolygons);
				break;
			case CombineMode.Exclude:
				//Exclude == Difference

				//Exclude/Difference means "Subtract any overlapping areas of the new Polygon set from the old Polygon set."
				doc.Selection.SelectionClipper.Execute (ClipType.ctDifference, resultingPolygons);
				break;
			case CombineMode.Intersect:
				//Intersect means "Leave only the overlapping areas between the new and old Polygon sets."
				doc.Selection.SelectionClipper.Execute (ClipType.ctIntersection, resultingPolygons);
				break;
			default:
				//Default should only be *CombineMode.Union*, but just in case...

				//Union means "Combine both Polygon sets, and keep any overlapping areas as well."
				doc.Selection.SelectionClipper.Execute (ClipType.ctUnion, resultingPolygons);
				break;
		}

		//After using Clipper, it has to be cleared so there are no conflicts with its next usage.
		doc.Selection.SelectionClipper.Clear ();

		//Set the resulting selection path to the calculated ("clipped") selection path.
		doc.Selection.SelectionPolygons = resultingPolygons;
	}

	private static void PerformSelectionReplace (Document doc, List<List<IntPoint>> polygons)
	{
		//Clear any previously stored Polygons.
		doc.Selection.SelectionPolygons.Clear ();

		//Set the resulting selection path to the new selection path.
		doc.Selection.SelectionPolygons = polygons;
	}

	public void OnSaveSettings (ISettingsService settings)
	{
		if (mode_box is not null)
			settings.PutSetting (COMBINE_MODE_SETTING, selected_index);
	}
}

public enum CombineMode
{
	Union,
	Xor,
	Exclude,
	Replace,
	Intersect,
}
