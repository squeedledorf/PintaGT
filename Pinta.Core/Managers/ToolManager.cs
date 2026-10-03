//
// ToolManager.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Pinta.Core;

public interface IToolService
{
	/// <summary>
	/// Adds a new tool to the tool box.
	/// </summary>
	void AddTool (BaseTool tool);

	/// <summary>
	/// Instructs the current tool to commit any work that is in a temporary state.
	/// </summary>
	void Commit ();

	/// <summary>
	/// Gets the currently selected tool.
	/// </summary>
	BaseTool? CurrentTool { get; }

	/// <summary>
	/// Performs the mouse down event for the currently selected tool.
	/// </summary>
	void DoMouseDown (Document document, ToolMouseEventArgs e);

	/// <summary>
	/// Gets the previously selected tool.
	/// </summary>
	BaseTool? PreviousTool { get; }

	/// <summary>
	/// Removes the first found tool of the specified type from tool box.
	/// </summary>
	void RemoveInstanceOfTool<T> () where T : BaseTool;

	/// <summary>
	/// Sets the current tool to the specified tool.
	/// </summary>
	void SetCurrentTool (BaseTool tool);

	/// <summary>
	/// Sets the current tool to the first tool with the specified tool type name, like
	/// 'PencilTool'. Returns a value indicating if tool was successfully changed.
	/// </summary>
	bool SetCurrentTool (string tool);

	/// <summary>
	/// Sets the current tool to the next tool with the specified shortcut,
	/// or the previous one if <paramref name="reverse"/> is set.
	/// </summary>
	bool SetCurrentTool (Gdk.Key shortcut, bool reverse = false);
}

public sealed class ToolManager : IEnumerable<BaseTool>, IToolService
{
	private readonly SortedSet<BaseTool> tools = new (new ToolSorter ());

	private readonly WorkspaceManager workspace_manager;
	private readonly ChromeManager chrome_manager;
	public ToolManager (WorkspaceManager workspaceManager, ChromeManager chromeManager)
	{
		workspace_manager = workspaceManager;
		chrome_manager = chromeManager;

		// Before the active document has changed, the current tool should commit unfinished changes.
		workspace_manager.PreActiveDocumentChanged += (_, _) => Commit ();
	}

	private bool is_panning;
	private bool space_held;
	private MouseButton pan_button;

	public event EventHandler<ToolEventArgs>? ToolAdded;
	public event EventHandler<ToolEventArgs>? ToolRemoved;
	public event EventHandler<ToolEventArgs>? ToolActivated;

	public BaseTool? CurrentTool { get; private set; }

	public BaseTool? PreviousTool { get; private set; }

	public void AddTool (BaseTool tool)
	{
		if (!tools.Add (tool))
			throw new Exception ("Attempted to add a duplicate tool");

		ToolAdded?.Invoke (this, new ToolEventArgs (tool));

		if (CurrentTool is null)
			SetCurrentTool (tool);
	}

	public void RemoveInstanceOfTool<T> () where T : BaseTool
	{
		T? tool =
			tools.OfType<T> ()
			.FirstOrDefault ();

		if (tool is null)
			return;

		if (!tools.Remove (tool))
			throw new Exception ("Attempted to remove a tool that wasn't registered");

		// Are we trying to remove the current tool?
		if (CurrentTool == tool) {
			// Can we set it back to the previous tool?
			if (PreviousTool is not null && PreviousTool != CurrentTool)
				SetCurrentTool (PreviousTool);
			else if (tools.Count != 0)  // Any tool?
				SetCurrentTool (tools.First ());
			else {
				// There are no tools left.
				DeactivateTool (tool, null);
				PreviousTool = null;
				CurrentTool = null;
			}
		}

		ToolRemoved?.Invoke (this, new ToolEventArgs (tool));
	}

	private BaseTool? FindTool (string name)
	{
		return tools.FirstOrDefault (t => string.Compare (name, t.GetType ().Name, true) == 0);
	}

	public void Commit ()
	{
		CurrentTool?.DoCommit (workspace_manager.ActiveDocumentOrDefault);
	}

	public void SetCurrentTool (BaseTool tool)
	{
		// Bail if this is already the current tool
		if (CurrentTool == tool)
			return;

		// Unload previous tool if needed
		if (CurrentTool is not null) {
			PreviousTool = CurrentTool;
			DeactivateTool (PreviousTool, tool);
		}

		// Load new tool
		CurrentTool = tool;

		tool.DoActivated (workspace_manager.ActiveDocumentOrDefault);

		ToolImage.SetFromIconName (tool.Icon);

		chrome_manager.ToolToolBar.Append (ToolMenuButton);
		chrome_manager.ToolToolBar.Append (ToolSeparator);

		chrome_manager.ToolToolBar.Append (ToolWidgetsScroll);
		tool.DoBuildToolBar (ToolWidgetsBox);

		workspace_manager.Invalidate ();
		chrome_manager.SetStatusBarText ($" {tool.Name}: {tool.StatusBarText}");

		ToolActivated?.Invoke (this, new ToolEventArgs (tool));
	}

	public bool SetCurrentTool (string tool)
	{
		if (FindTool (tool) is not BaseTool t)
			return false;

		SetCurrentTool (t);
		return true;
	}

	public bool SetCurrentTool (Gdk.Key shortcut, bool reverse = false)
	{
		if (FindNextTool (shortcut, reverse) is not BaseTool tool)
			return false;

		SetCurrentTool (tool);
		return true;
	}

	private BaseTool? FindNextTool (Gdk.Key shortcut, bool reverse)
	{
		// Find all tools with this shortcut
		var shortcut_tools =
			tools
			.Where (t => t.ShortcutKey.ToUpper () == shortcut.ToUpper ())
			.ToImmutableArray ();

		// No tools with this shortcut, bail
		if (shortcut_tools.Length == 0)
			return null;

		// Only one option, return it
		if (shortcut_tools.Length == 1 || CurrentTool is null)
			return shortcut_tools.First ();

		// Get the tool after (or before) the currently selected tool, wrapping around.
		// IndexOf is -1 when the current tool has another shortcut, so forward picks the first tool.
		int current = shortcut_tools.IndexOf (CurrentTool);
		int n = shortcut_tools.Length;
		int next_index = reverse
			? (current < 0 ? n - 1 : (current - 1 + n) % n)
			: (current + 1) % n;

		return shortcut_tools[next_index];
	}

	private void DeactivateTool (BaseTool tool, BaseTool? newTool)
	{
		ToolWidgetsBox.RemoveAll ();
		chrome_manager.ToolToolBar.RemoveAll ();

		tool.DoDeactivated (workspace_manager.ActiveDocumentOrDefault, newTool);
	}

	public void DoMouseDown (Document document, ToolMouseEventArgs args)
	{
		if (!TryMouseDownPanOverride (document, args))
			CurrentTool?.DoMouseDown (document, args);
	}

	public void DoMouseMove (Document document, ToolMouseEventArgs args)
	{
		if (!TryMouseMovePanOverride (document, args))
			CurrentTool?.DoMouseMove (document, args);
	}

	public void DoMouseUp (Document document, ToolMouseEventArgs args)
	{
		if (!TryMouseUpPanOverride (document, args))
			CurrentTool?.DoMouseUp (document, args);
	}

	public bool DoKeyDown (Document document, ToolKeyEventArgs args)
	{
		bool is_space = args.Key.Value == Gdk.Constants.KEY_space;

		// Swallow auto-repeat while Space is held for panning.
		if (is_space && space_held)
			return true;

		// The tool gets the first shot, so e.g. the Text tool can still type a space
		// and the shape tools can still add a control point.
		if (CurrentTool?.DoKeyDown (document, args) == true)
			return true;

		if (!is_space || !TryGetPanTool (out BaseTool? pan))
			return false;

		// Hold Space to pan with the left mouse button, as in Paint.NET.
		space_held = true;
		WatchSpaceRelease ();

		document.Workspace.Canvas.Cursor = pan.DefaultCursor;

		return true;
	}

	public bool DoKeyUp (Document document, ToolKeyEventArgs args)
	{
		if (args.Key.Value == Gdk.Constants.KEY_space && space_held) {
			ReleaseSpace (document);
			return true;
		}

		return CurrentTool?.DoKeyUp (document, args) ?? false;
	}

	private void ReleaseSpace (Document? document)
	{
		space_held = false;

		// If a Space-pan drag is still in progress, the cursor is restored on mouse up.
		if (!is_panning && document is not null)
			document.Workspace.Canvas.Cursor = CurrentTool?.CurrentCursor;
	}

	private bool watching_space;

	// The canvas only gets key releases while the pointer is over it (and not when a toolbar
	// widget has focus), and none at all while another window has focus. Watch the main
	// window directly so the Space-pan state can't get stuck.
	private void WatchSpaceRelease ()
	{
		if (watching_space)
			return;

		watching_space = true;
		Gtk.Window window = chrome_manager.MainWindow;

		window.OnNotify += (_, e) => {
			if (e.Pspec.GetName () == "is-active" && !window.IsActive && space_held)
				ReleaseSpace (workspace_manager.ActiveDocumentOrDefault);
		};

		Gtk.EventControllerKey key_controller = Gtk.EventControllerKey.New ();
		key_controller.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		key_controller.OnKeyReleased += (_, e) => {
			if (e.Keyval == Gdk.Constants.KEY_space && space_held)
				ReleaseSpace (workspace_manager.ActiveDocumentOrDefault);
		};
		window.AddController (key_controller);
	}

	public void DoAfterSave (Document document)
		=> CurrentTool?.DoAfterSave (document);

	public Task<bool> DoHandlePaste (Document document, Gdk.Clipboard clipboard)
		=> CurrentTool?.DoHandlePaste (document, clipboard) ?? Task.FromResult (false);

	public IEnumerator<BaseTool> GetEnumerator ()
		=> tools.GetEnumerator ();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
		=> tools.GetEnumerator ();

	private bool TryMouseDownPanOverride (Document document, ToolMouseEventArgs args)
	{
		if (is_panning)
			return true;

		bool starts_pan = args.MouseButton == MouseButton.Middle || (space_held && args.MouseButton == MouseButton.Left);
		if (!starts_pan || !TryGetPanTool (out BaseTool? pan))
			return false;

		is_panning = true;
		pan_button = args.MouseButton;
		document.Workspace.Canvas.Cursor = pan.DefaultCursor;
		pan.DoMouseDown (document, args);
		return true;
	}

	private bool TryMouseMovePanOverride (Document document, ToolMouseEventArgs args)
	{
		// While Space is held the tool doesn't see mouse moves, so it can't replace the pan cursor.
		if (!(is_panning || space_held) || !TryGetPanTool (out var pan))
			return false;

		if (is_panning)
			pan.DoMouseMove (document, args);

		return true;
	}

	private bool TryMouseUpPanOverride (Document document, ToolMouseEventArgs args)
	{
		if (!is_panning || !TryGetPanTool (out var pan))
			return false;

		// Ignore releases of any button other than the one that started the pan
		if (args.MouseButton != pan_button)
			return true;

		is_panning = false;
		pan.DoMouseUp (document, args);
		document.Workspace.Canvas.Cursor = space_held ? pan.DefaultCursor : CurrentTool?.CurrentCursor;
		return true;
	}

	private bool TryGetPanTool ([NotNullWhen (true)] out BaseTool? tool)
	{
		tool = FindTool ("PanTool");

		return tool is not null;
	}

	private sealed class ToolSorter : Comparer<BaseTool>
	{
		public override int Compare (BaseTool? x, BaseTool? y)
		{
			int result = (x?.Priority ?? 0) - (y?.Priority ?? 0);

			if (result != 0)
				return result;

			// If two tools have the same priority, sort by type name so that both tools can still
			// be inserted into the set.
			string x_type = x?.GetType ().AssemblyQualifiedName ?? string.Empty;
			string y_type = y?.GetType ().AssemblyQualifiedName ?? string.Empty;
			return x_type.CompareTo (y_type);
		}
	}

	private Gtk.MenuButton? tool_menu_button;
	private Gtk.Image? tool_image;
	private Gtk.Separator? tool_sep;
	private Gtk.Box? tool_widgets_box;
	private Gtk.ScrolledWindow? tool_widgets_scroll;

	private Gtk.Image ToolImage => tool_image ??= Gtk.Image.New ();

	// Paint.NET's "Tool:" dropdown at the start of the tool bar. Alt+T opens it (label mnemonic).
	private Gtk.MenuButton ToolMenuButton {
		get {
			if (tool_menu_button is not null)
				return tool_menu_button;

			Gtk.Label label = Gtk.Label.NewWithMnemonic (Translations.GetString ("_Tool:"));

			Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
			content.Append (label);
			content.Append (ToolImage);

			// A list box gives arrow keys, Home/End and Enter for free.
			Gtk.ListBox list = Gtk.ListBox.New ();
			list.SelectionMode = Gtk.SelectionMode.Browse;
			list.AddCssClass ("navigation-sidebar");

			Gtk.Popover popover = Gtk.Popover.New ();
			popover.Child = list;
			List<BaseTool> listed = [];
			// Rebuild on every show, since add-ins can add or remove tools.
			popover.OnShow += (_, _) => {
				list.RemoveAll ();
				// Priority order, as PDN's Tool dropdown lists the toolbox row by row.
				listed = [.. tools];
				foreach (BaseTool tool in listed) {
					Gtk.ListBoxRow row = CreateToolListItem (tool);
					list.Append (row);
					if (tool == CurrentTool) {
						list.SelectRow (row);
						row.GrabFocus ();
					}
				}
			};
			list.OnRowActivated += (_, args) => {
				popover.Popdown ();
				SetCurrentTool (listed[args.Row.GetIndex ()]);
			};

			tool_menu_button = Gtk.MenuButton.New ();
			tool_menu_button.Child = content;
			tool_menu_button.Popover = popover;
			tool_menu_button.HasFrame = false;
			tool_menu_button.AlwaysShowArrow = true;
			label.MnemonicWidget = tool_menu_button;

			return tool_menu_button;
		}
	}

	private static Gtk.ListBoxRow CreateToolListItem (BaseTool tool)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		box.Append (Gtk.Image.NewFromIconName (tool.Icon));
		box.Append (Gtk.Label.New (tool.Name));

		Gtk.ListBoxRow row = Gtk.ListBoxRow.New ();
		row.Child = box;
		return row;
	}

	private Gtk.Separator ToolSeparator => tool_sep ??= GtkExtensions.CreateToolBarSeparator ();
	private Gtk.Box ToolWidgetsBox => tool_widgets_box ??= Gtk.Box.New (Gtk.Orientation.Horizontal, 0);
	// Scroll the toolbar contents if they are very long (e.g. the line/curve tool).
	private Gtk.ScrolledWindow ToolWidgetsScroll {
		get {
			if (tool_widgets_scroll == null) {
				tool_widgets_scroll = Gtk.ScrolledWindow.New ();
				tool_widgets_scroll.Child = ToolWidgetsBox;
				tool_widgets_scroll.HscrollbarPolicy = Gtk.PolicyType.Automatic;
				tool_widgets_scroll.VscrollbarPolicy = Gtk.PolicyType.Never;
				tool_widgets_scroll.HasFrame = false;
				tool_widgets_scroll.OverlayScrolling = true;
				tool_widgets_scroll.WindowPlacement = Gtk.CornerType.BottomRight;
				tool_widgets_scroll.Hexpand = true;
				tool_widgets_scroll.Halign = Gtk.Align.Fill;
			}

			return tool_widgets_scroll;
		}
	}
}
