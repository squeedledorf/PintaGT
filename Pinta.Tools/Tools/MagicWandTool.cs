//
// MagicWandTool.cs
//
// Author:
//       Olivier Dufour <olivier.duff@gmail.com>
//
// Copyright (c) 2010 Olivier Dufour
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
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MagicWandTool : FloodTool
{
	private readonly IWorkspaceService workspace;

	private CombineMode combine_mode;
	// The selection before the live wand click, which every redo of the flood combines with.
	private DocumentSelection? base_selection;

	public MagicWandTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();
		LimitToSelection = false;

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};
	}

	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_S);
	public override string Name => Translations.GetString ("Magic Wand");
	public override string Icon => Pinta.Resources.Icons.ToolSelectMagicWand;
	public override string StatusBarText => Translations.GetString (
		"Click to select region of similar color." +
		"\nHold Shift to switch between Contiguous and Global mode." +
		"\nDrag the nub to move the click point; press Enter or click Finish when done."
	);
	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.MagicWand.png"), 21, 10, null);
	public override int Priority => 13;
	public override bool IsSelectionTool => true;
	protected override bool ShowSelectionQualityButton => true;
	protected override bool ShowFinishButton => true;
	protected override IWorkspaceService Workspace => workspace;

	// Paint.NET order: selection mode first, then the flood controls.
	protected override void AppendFloodControls (Gtk.Box tb)
	{
		workspace.SelectionHandler.BuildToolbar (tb, Settings);

		tb.Append (SelectionSeparator);

		base.AppendFloodControls (tb);
	}

	// Enter finishes a live selection first; the next Enter deselects.
	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
		=> base.OnKeyDown (document, e) || SelectTool.TryDeselectOnKey (e);


	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// As in Paint.NET, clicking off-canvas with a selection tool deselects.
		if (!document.Workspace.PointInCanvas ((PointD) e.Point)) {
			FinishLiveFill ();
			Command deselect = PintaCore.Actions.Edit.Deselect;
			if (deselect.Sensitive)
				deselect.Activate ();
			return;
		}

		base.OnMouseDown (document, e);
	}

	protected override BaseHistoryItem BeginLiveFill (Document document, ToolMouseEventArgs e)
	{
		combine_mode = workspace.SelectionHandler.DetermineCombineMode (e);
		base_selection = document.Selection.Clone ();

		var undoAction = new SelectionHistoryItem (workspace, Icon, Name);
		undoAction.TakeSnapshot ();
		return undoAction;
	}

	protected override void EndLiveFill ()
		=> base_selection = null;

	protected override void OnFillRegionComputed (Document document, IReadOnlyList<IReadOnlyList<PointI>> polygonSet)
	{
		document.PreviousSelection = (base_selection ?? document.Selection).Clone ();

		SelectionModeHandler.PerformSelectionMode (document, combine_mode, DocumentSelection.ConvertToPolygons (polygonSet));
		// Only reveal the selection once a fill happened; an off-canvas click must not expose a hidden one.
		document.Selection.Visible = true;
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		workspace.SelectionHandler.OnSaveSettings (settings);
	}

	private Separator? selection_sep;
	private Separator SelectionSeparator => selection_sep ??= GtkExtensions.CreateToolBarSeparator ();
}
