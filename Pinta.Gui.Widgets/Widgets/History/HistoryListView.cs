//
// HistoryTreeView.cs
//
// Author:
//       Anirudh Sanjeev <anirudh@anirudhsanjeev.org>
//	Joe Hillenbrand <joehillen@gmail.com>
//
// Copyright (c) 2010 Jonathan Pobst
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
using System.Diagnostics.CodeAnalysis;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

[GObject.Subclass<Gtk.ScrolledWindow>]
public sealed partial class HistoryListView
{
	private Gio.ListStore model;
	private Gtk.SingleSelection selection_model;
	private Gtk.ListView list_view;

	private Document? active_document;

	public static new HistoryListView New ()
		=> NewWithProperties ([]);

	[MemberNotNull (nameof (model))]
	[MemberNotNull (nameof (selection_model))]
	[MemberNotNull (nameof (list_view))]
	partial void Initialize ()
	{
		Gio.ListStore listModel = Gio.ListStore.New (HistoryListViewItem.GetGType ());

		Gtk.SingleSelection selectionModel = Gtk.SingleSelection.New (listModel);
		selectionModel.OnSelectionChanged += HandleSelectionChanged;

		Gtk.SignalListItemFactory signalFactory = Gtk.SignalListItemFactory.New ();
		signalFactory.OnSetup += (factory, args) => {
			var item = (Gtk.ListItem) args.Object;
			HistoryItemWidget widget = HistoryItemWidget.New ();

			// Clicking the current row again undoes it, so repeated clicks flip between before and after (as in Paint.NET).
			// The row's own click handler runs after this one and selects the clicked row, so remember the state
			// at press time and undo once that handler is done.
			bool wasCurrent = false;
			Gtk.GestureClick click = Gtk.GestureClick.New ();
			click.OnPressed += (_, _) => wasCurrent = IsCurrentHistoryRow (item.Position);
			click.OnReleased += (_, _) => {
				if (!wasCurrent)
					return;
				wasCurrent = false;
				uint position = item.Position;
				GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
					if (IsCurrentHistoryRow (position) && active_document!.History.CanUndo)
						active_document.History.Undo ();
					return false;
				});
			};
			widget.AddController (click);

			item.SetChild (widget);
		};
		signalFactory.OnBind += (factory, args) => {
			var list_item = (Gtk.ListItem) args.Object;
			var model_item = (HistoryListViewItem) list_item.GetItem ()!;
			var widget = (HistoryItemWidget) list_item.GetChild ()!;
			widget.Update (model_item);
		};

		Gtk.ListView listView = Gtk.ListView.New (selectionModel, signalFactory);
		listView.CanFocus = false;
		listView.AddCssClass (UNDONE_STYLE_CLASS);
		EnsureUndoneRowStyle ();

		// --- Initialization (Gtk.Widget)

		CanFocus = false;

		// --- Initialization (Gtk.ScrolledWindow)

		SetPolicy (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);
		SetChild (listView);

		// ScrollTo runs before the ListView has measured a newly added row, so it stops one row short.
		// While the newest item is current, keep the view pinned to the bottom whenever the content grows.
		Vadjustment!.OnChanged += (adjustment, _) => {
			if (listModel.NItems > 0 && selectionModel.Selected == listModel.NItems - 1)
				adjustment.Value = adjustment.Upper - adjustment.PageSize;
		};

		// --- References to keep

		model = listModel;
		selection_model = selectionModel;
		list_view = listView;

		// --- Event handlers for the application
		// TODO: Move handlers out of this constructor

		PintaCore.Workspace.ActiveDocumentChanged += OnActiveDocumentChanged;
	}

	private const string UNDONE_STYLE_CLASS = "history-list";
	private static bool undone_row_style_loaded;

	/// <summary>
	/// Undone history items are the rows after the selected one; give them a grey background.
	/// </summary>
	private static void EnsureUndoneRowStyle ()
	{
		if (undone_row_style_loaded)
			return;

		Gdk.Display? display = Gdk.Display.GetDefault ();
		if (display is null)
			return;

		Gtk.CssProvider provider = Gtk.CssProvider.New ();
		provider.LoadFromString ($"listview.{UNDONE_STYLE_CLASS} > row:selected ~ row {{ background-color: alpha(gray, 0.25); }}");
		Gtk.StyleContext.AddProviderForDisplay (display, provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		undone_row_style_loaded = true;
	}

	private bool IsCurrentHistoryRow (uint position)
		=> active_document is not null
		&& position == selection_model.Selected
		&& position == active_document.History.Pointer;

	private void HandleSelectionChanged (Gtk.SelectionModel sender, EventArgs e)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		int index = (int) selection_model.Selected;

		while (active_document.History.Pointer < index)
			active_document.History.Redo ();

		while (active_document.History.Pointer > index)
			active_document.History.Undo ();
	}

	private void OnActiveDocumentChanged (object? sender, EventArgs e)
	{
		var doc =
			PintaCore.Workspace.HasOpenDocuments
			? PintaCore.Workspace.ActiveDocument
			: null;

		if (active_document == doc)
			return;

		if (active_document is not null) {
			active_document.History.HistoryItemAdded -= OnHistoryItemAdded;
			active_document.History.ActionUndone -= OnUndoOrRedo;
			active_document.History.ActionRedone -= OnUndoOrRedo;
		}

		// Clear out old items and rebuild.
		model.RemoveMultiple (0, model.GetNItems ());

		active_document = doc;

		if (doc is null)
			return;

		foreach (BaseHistoryItem item in doc.History.Items)
			model.Append (HistoryListViewItem.New (item));

		// Move selection to the document's current history item.
		if (model.NItems > 0) {
			uint selectedIdx = (uint) doc.History.Pointer;
			selection_model.SetSelected (selectedIdx);
			list_view.ScrollToSelectedItem (selection_model);
		}

		doc.History.HistoryItemAdded += OnHistoryItemAdded;
		doc.History.ActionUndone += OnUndoOrRedo;
		doc.History.ActionRedone += OnUndoOrRedo;
	}

	private void OnHistoryItemAdded (object? sender, HistoryItemAddedEventArgs args)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		uint idx = (uint) active_document.History.Pointer;

		// Remove any stale (previously undone) items before adding the new item.
		model.RemoveMultiple (idx, model.GetNItems () - idx);

		model.Append (HistoryListViewItem.New (args.Item));
		selection_model.SetSelected (idx);
		list_view.ScrollToSelectedItem (selection_model);
	}

	private void OnUndoOrRedo (object? sender, EventArgs args)
	{
		ArgumentNullException.ThrowIfNull (active_document);

		// Update the selected history item.
		uint selectedIdx = (uint) active_document.History.Pointer;
		selection_model.SetSelected (selectedIdx);
		list_view.ScrollToSelectedItem (selection_model);
	}
}
