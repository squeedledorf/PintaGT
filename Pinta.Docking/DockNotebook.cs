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

[GObject.Subclass<Gtk.Box>]
public sealed partial class DockNotebook
{
	private Adw.TabView tab_view;
	private Adw.TabBar tab_bar;
	private readonly HashSet<IDockNotebookItem> items = [];

	// Tab context menu (Paint.NET's image list menu).
	private Gio.Menu tab_menu;
	private Gio.SimpleAction copy_path_action;
	private Gio.SimpleAction open_folder_action;
	private Gtk.PopoverMenu? keyboard_menu;

	[MemberNotNull (nameof (tab_view))]
	[MemberNotNull (nameof (tab_bar))]
	[MemberNotNull (nameof (tab_menu), nameof (copy_path_action), nameof (open_folder_action))]
	partial void Initialize ()
	{
		Adw.TabView tabView = Adw.TabView.New ();
		tabView.Vexpand = true;
		tabView.Valign = Gtk.Align.Fill;
		tabView.OnClosePage += TabView_OnClosePage;
		tabView.OnSetupMenu += TabView_OnSetupMenu;

		// Like Paint.NET's image list, the strip stays visible with a single image.
		// (Interim until a thumbnail image list replaces the text tabs.)
		Adw.TabBar tabBar = Adw.TabBar.New ();
		tabBar.SetView (tabView);
		tabBar.Autohide = false;
		tabBar.AddCssClass (AdwaitaStyles.Inline);
		tabBar.ExpandTabs = false;

		// --- Initialization (Gtk.Box)

		SetOrientation (Gtk.Orientation.Vertical);

		Append (tabBar);
		Append (tabView);

		// --- References to keep

		tab_view = tabView;
		tab_bar = tabBar;

		// --- Further initialization

		// Emit an event when the current tab is changed.
		Adw.TabView.SelectedPagePropertyDefinition.Notify (tabView, TabView_TabChanged);

		// The tab context menu. The page is made active before the menu shows (see TabView_OnSetupMenu),
		// so the app-level Save / Save As / Close actions act on the right-clicked image.
		Gio.SimpleAction copyPathAction = Gio.SimpleAction.New ("copy-path", null);
		copyPathAction.OnActivate += (_, _) => CopyActivePath ();

		Gio.SimpleAction openFolderAction = Gio.SimpleAction.New ("open-folder", null);
		openFolderAction.OnActivate += (_, _) => OpenActiveContainingFolder ();

		Gio.SimpleAction menuAction = Gio.SimpleAction.New ("menu", null);
		menuAction.OnActivate += (_, _) => PopupMenuForActiveTab ();

		Gio.SimpleActionGroup tabActions = Gio.SimpleActionGroup.New ();
		tabActions.AddAction (copyPathAction);
		tabActions.AddAction (openFolderAction);
		tabActions.AddAction (menuAction);
		InsertActionGroup ("tab", tabActions);

		Gio.Menu tabMenu = Gio.Menu.New ();
		tabView.SetMenuModel (tabMenu);

		// Alt+minus opens the menu for the current image, as in Paint.NET.
		Gtk.ShortcutController shortcuts = Gtk.ShortcutController.New ();
		shortcuts.SetScope (Gtk.ShortcutScope.Global);
		shortcuts.AddShortcut (Gtk.Shortcut.New (
			Gtk.ShortcutTrigger.ParseString ("<Alt>minus"),
			Gtk.NamedAction.New ("tab.menu")));
		AddController (shortcuts);

		copy_path_action = copyPathAction;
		open_folder_action = openFolderAction;
		tab_menu = tabMenu;

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

	private void TabView_OnSetupMenu (Adw.TabView _, Adw.TabView.SetupMenuSignalArgs args)
	{
		// The page is null when the menu closes.
		if (args.Page is not Adw.TabPage page)
			return;

		tab_view.SelectedPage = page;
		RebuildTabMenu ();
	}

	/// <summary>
	/// Rebuild the menu for the active image: its name as the header, then Copy Path and
	/// Open Containing Folder (disabled until the image is saved), Save, Save As, Close and Close All.
	/// </summary>
	private void RebuildTabMenu ()
	{
		Document? document = PintaCore.Workspace.ActiveDocumentOrDefault;
		bool hasFile = document?.HasFile ?? false;
		copy_path_action.SetEnabled (hasFile);
		open_folder_action.SetEnabled (hasFile);

		Gio.Menu fileSection = Gio.Menu.New ();
		fileSection.Append (Translations.GetString ("Copy Path"), "tab.copy-path");
		fileSection.Append (Translations.GetString ("Open Containing Folder"), "tab.open-folder");

		FileActions file = PintaCore.Actions.File;
		Gio.Menu saveSection = Gio.Menu.New ();
		saveSection.AppendItem (file.Save.CreateMenuItem ());
		saveSection.AppendItem (file.SaveAs.CreateMenuItem ());

		Gio.Menu closeSection = Gio.Menu.New ();
		closeSection.AppendItem (file.Close.CreateMenuItem ());
		closeSection.AppendItem (PintaCore.Actions.Window.CloseAll.CreateMenuItem ());

		tab_menu.RemoveAll ();
		tab_menu.AppendSection (document?.DisplayName, fileSection);
		tab_menu.AppendSection (null, saveSection);
		tab_menu.AppendSection (null, closeSection);
	}

	private void PopupMenuForActiveTab ()
	{
		if (tab_view.SelectedPage is null)
			return;

		RebuildTabMenu ();

		if (keyboard_menu is null) {
			keyboard_menu = Gtk.PopoverMenu.NewFromModel (tab_menu);
			keyboard_menu.SetParent (this);
			keyboard_menu.Position = Gtk.PositionType.Bottom;
			keyboard_menu.Halign = Gtk.Align.Start;
		}

		// Point just below the left end of the tab strip.
		int y = tab_bar.IsVisible () ? tab_bar.GetHeight () : 0;
		keyboard_menu.SetPointingTo (new Gdk.Rectangle { X = 16, Y = y, Width = 1, Height = 1 });
		keyboard_menu.Popup ();
	}

	private static void CopyActivePath ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault?.File is not Gio.File file)
			return;

		string path = file.GetPath () ?? file.GetUri ();
		GdkExtensions.GetDefaultClipboard ().SetText (path);
	}

	private static async void OpenActiveContainingFolder ()
	{
		if (PintaCore.Workspace.ActiveDocumentOrDefault?.File is not Gio.File file)
			return;

		try {
			await Gtk.FileLauncher.New (file).OpenContainingFolderAsync (PintaCore.Chrome.MainWindow);
		} catch (Exception e) {
			Console.Error.WriteLine ($"Failed to open containing folder: {e.Message}");
		}
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
	/// Emitted when a tab is closed by the user.
	/// </summary>
	public event EventHandler<TabClosedEventArgs>? TabClosed;

	/// <summary>
	/// Emitted when switching to a different tab.
	/// </summary>
	public event EventHandler<TabEventArgs>? ActiveTabChanged;

	/// <summary>
	/// The items currently in the notebook.
	/// </summary>
	public IEnumerable<IDockNotebookItem> Items => items;

	/// <summary>
	/// Whether to show the tab bar.
	/// </summary>
	public bool EnableTabs {
		get => tab_bar.IsVisible ();
		set {
			if (value)
				tab_bar.Show ();
			else
				tab_bar.Hide ();
		}
	}

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

	public void InsertTab (IDockNotebookItem item, int position)
	{
		items.Add (item);

		Adw.TabPage page = tab_view.Insert (item.Widget, position);
		page.Title = item.Label;
		// Update the tab's label when the document's title changes.
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
