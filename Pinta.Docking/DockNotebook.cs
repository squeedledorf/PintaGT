//
// Author:
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2020 Cameron White
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
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Pinta.Core;

namespace Pinta.Docking;

public sealed class TabClosedEventArgs (IDockNotebookItem item) : CancelEventArgs
{
	public IDockNotebookItem Item { get; } = item;
}

public sealed class TabEventArgs (IDockNotebookItem? item) : EventArgs
{
	public IDockNotebookItem? Item { get; } = item;
}

public sealed class TabReorderedEventArgs (IDockNotebookItem item, int position) : EventArgs
{
	public IDockNotebookItem Item { get; } = item;
	public int Position { get; } = position;
}

/// <summary>
/// Holds one canvas page per open image. It has no tab strip: the image list
/// (thumbnail strip in the menu row) is how the user sees and switches images.
/// </summary>
[GObject.Subclass<Gtk.Box>]
public sealed partial class DockNotebook
{
	private Adw.TabView tab_view;
	private readonly HashSet<IDockNotebookItem> items = [];

	[MemberNotNull (nameof (tab_view))]
	partial void Initialize ()
	{
		Adw.TabView tabView = Adw.TabView.New ();
		tabView.Vexpand = true;
		tabView.Valign = Gtk.Align.Fill;
		tabView.OnClosePage += TabView_OnClosePage;
		tabView.OnPageReordered += (_, args) => TabReordered?.Invoke (this, new TabReorderedEventArgs (FindItemForPage (args.Page)!, args.Position));

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Vertical);

		Append (tabView);

		// --- References to keep

		tab_view = tabView;

		// --- Further initialization

		// Emit an event when the current tab is changed.
		Adw.TabView.SelectedPagePropertyDefinition.Notify (tabView, TabView_TabChanged);

		// The grey surround of the canvas has a separate dark-theme colour in style.css.
		Adw.StyleManager styleManager = Adw.StyleManager.GetDefault ();
		Adw.StyleManager.DarkPropertyDefinition.Notify (styleManager, (_, _) => UpdateDarkStyle (styleManager));
		UpdateDarkStyle (styleManager);
	}

	private void UpdateDarkStyle (Adw.StyleManager styleManager)
	{
		if (styleManager.Dark)
			AddCssClass ("pinta-dark");
		else
			RemoveCssClass ("pinta-dark");
	}

	public static DockNotebook New () => NewWithProperties ([]);

	private void TabView_TabChanged (GObject.Object _, NotifySignalArgs __)
	{
		Adw.TabPage? page = tab_view.SelectedPage;
		IDockNotebookItem? item = FindItemForPage (page);
		ActiveTabChanged?.Invoke (this, new TabEventArgs (item));
	}

	private bool TabView_OnClosePage (Adw.TabView _, Adw.TabView.ClosePageSignalArgs args)
	{
		// Prompt the user to save unsaved changes before closing.

		Adw.TabPage page = args.Page;

		IDockNotebookItem item = FindItemForPage (page)!;

		TabClosedEventArgs close_args = new (item);

		TabClosed?.Invoke (this, close_args);

		tab_view.ClosePageFinish (page, !close_args.Cancel);

		if (!close_args.Cancel)
			items.Remove (item);

		// Prevent the default close handler from running.
		return Gdk.Constants.EVENT_STOP;
	}

	/// <summary>
	/// Emitted when a page is closed through the tab view (e.g. its Ctrl+W shortcut).
	/// </summary>
	public event EventHandler<TabClosedEventArgs>? TabClosed;

	/// <summary>
	/// Emitted when a page is moved to a new position (e.g. Ctrl+Shift+PgUp/PgDn).
	/// </summary>
	public event EventHandler<TabReorderedEventArgs>? TabReordered;

	/// <summary>
	/// Emitted when switching to a different page.
	/// </summary>
	public event EventHandler<TabEventArgs>? ActiveTabChanged;

	/// <summary>
	/// The items currently in the notebook.
	/// </summary>
	public IEnumerable<IDockNotebookItem> Items => items;

	/// <summary>
	/// Returns the active notebook item.
	/// </summary>
	public IDockNotebookItem? ActiveItem {
		get => FindItemForPage (tab_view.SelectedPage);
		set => tab_view.SelectedPage = tab_view.GetPage (value!.Widget);
	}

	/// <summary>
	/// Returns the index of the active item.
	/// </summary>
	public int ActiveItemIndex => tab_view.SelectedPage switch {
		null => -1,
		var page => tab_view.GetPagePosition (page)
	};

	/// <summary>
	/// Adds a page at the end, as Paint.NET adds new images.
	/// </summary>
	public void AppendTab (IDockNotebookItem item) => InsertTab (item, tab_view.NPages);

	/// <summary>
	/// Moves an item's page to a new position (raises <see cref="TabReordered"/>).
	/// </summary>
	public void MoveTab (IDockNotebookItem item, int position)
		=> tab_view.ReorderPage (tab_view.GetPage (item.Widget), position);

	public void InsertTab (IDockNotebookItem item, int position)
	{
		items.Add (item);

		Adw.TabPage page = tab_view.Insert (item.Widget, position);
		page.Title = item.Label;
		// Update the page's title when the document's title changes.
		item.LabelChanged += (o, args) => { page.Title = item.Label; };
	}

	public void RemoveTab (IDockNotebookItem item)
	{
		Adw.TabPage page = tab_view.GetPage (item.Widget);
		tab_view.ClosePage (page);
		items.Remove (item);
	}

	private IDockNotebookItem? FindItemForPage (Adw.TabPage? page) => items.Where (i => i.Widget == page?.Child).FirstOrDefault ();
}
