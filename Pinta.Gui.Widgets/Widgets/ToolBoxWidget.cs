using System.Collections.Generic;
using System.Linq;
using Gtk;
using Pinta.Core;

namespace Pinta.Gui.Widgets;

/// <summary>
/// A compact two-column grid of tool buttons, laid out row-major in tool order like Paint.NET's Tools window.
/// </summary>
[GObject.Subclass<Gtk.Grid>]
public sealed partial class ToolBoxWidget
{
	private const int COLUMNS = 2;

	private ToolManager tools = null!; // NRT - set in factory method
	// Stores the button corresponding to each tool. Tools of one group share a button.
	private readonly Dictionary<BaseTool, Gtk.ToggleButton> tool_buttons = new ();
	private readonly Dictionary<ToolBoxGroup, Gtk.ToggleButton> group_buttons = new ();
	// The member a group's button switches to: the one used last.
	private readonly Dictionary<ToolBoxGroup, BaseTool> group_last = new ();
	// Dummy ToggleButton to use for grouping together the tools' buttons.
	private readonly Gtk.ToggleButton toggle_group = Gtk.ToggleButton.New ();

	partial void Initialize ()
	{
		Valign = Gtk.Align.Start;
		Halign = Gtk.Align.Center;
	}

	public static ToolBoxWidget New (ToolManager tools)
	{
		ToolBoxWidget widget = NewWithProperties ([]);
		widget.Configure (tools);
		return widget;
	}

	private void Configure (ToolManager tools)
	{
		tools.ToolAdded += (_, e) => HandleToolAdded (e.Tool);
		tools.ToolRemoved += (_, e) => HandleToolRemoved (e.Tool);
		tools.ToolActivated += (_, e) => HandleToolActivated (e.Tool);

		this.tools = tools;
	}

	// Short tooltips as in Paint.NET: "Paintbrush (B)". The status bar shows the full hint.
	private static string Tooltip (string name, Gdk.Key shortcut)
		=> shortcut == Gdk.Key.Invalid ? name : $"{name} ({shortcut.ToUpper ().Name ()})";

	private Gtk.ToggleButton CreateButton (string icon, string name, string tooltip)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.IconName = icon;
		button.Name = name;
		button.FocusOnClick = false;
		button.TooltipText = tooltip;
		button.Group = toggle_group;
		button.SetCssClasses ([Resources.Styles.ToolBoxButton, AdwaitaStyles.Flat]);
		return button;
	}

	private void HandleToolAdded (BaseTool tool)
	{
		if (tool.ToolBoxGroup is ToolBoxGroup group) {
			if (!group_buttons.TryGetValue (group, out Gtk.ToggleButton? groupButton)) {
				groupButton = CreateButton (group.Icon, group.Name, Tooltip (group.Name, tool.ShortcutKey));
				groupButton.OnClicked += (_, _) => {
					if (group_last.TryGetValue (group, out BaseTool? last) && tool_buttons.ContainsKey (last))
						HandleToolButtonClicked (last);
					else if (tool_buttons.Keys.FirstOrDefault (t => t.ToolBoxGroup == group) is BaseTool first)
						HandleToolButtonClicked (first);
				};
				group_buttons[group] = groupButton;
			}
			tool_buttons[tool] = groupButton;
		} else {
			Gtk.ToggleButton toolButton = CreateButton (tool.Icon, tool.Name, Tooltip (tool.Name, tool.ShortcutKey));
			toolButton.OnClicked += (_, _) => HandleToolButtonClicked (tool);
			tool_buttons[tool] = toolButton;
		}

		Relayout ();
	}

	/// <summary>
	/// Re-attach every button at its row-major cell, since adding or removing a tool shifts the ones after it.
	/// A group's button takes the place of its first member.
	/// </summary>
	private void Relayout ()
	{
		while (GetFirstChild () is Gtk.Widget child)
			Remove (child);

		HashSet<Gtk.ToggleButton> placed = [];
		int index = 0;
		foreach (BaseTool tool in tools.Where (tool_buttons.ContainsKey)) {
			Gtk.ToggleButton button = tool_buttons[tool];
			if (!placed.Add (button))
				continue;
			Attach (button, index % COLUMNS, index / COLUMNS, 1, 1);
			index++;
		}
	}

	private void HandleToolButtonClicked (BaseTool tool)
	{
		tools.SetCurrentTool (tool);
	}

	/// <summary>
	/// If the tool was switched without clicking on the button (e.g. via shortcut key),
	/// ensure the tool's button is active. Note we don't need to deactivate the previous
	/// button since they're all in the same toggle button group.
	/// </summary>
	private void HandleToolActivated (BaseTool tool)
	{
		if (tool.ToolBoxGroup is ToolBoxGroup group)
			group_last[group] = tool;

		// Clicking does not deactivate the previous button, since all are in the same toggle group.
		tool_buttons[tool].Active = true;
	}

	private void HandleToolRemoved (BaseTool tool)
	{
		tool_buttons.Remove (tool);
		if (tool.ToolBoxGroup is ToolBoxGroup group && !tool_buttons.Keys.Any (t => t.ToolBoxGroup == group))
			group_buttons.Remove (group);
		Relayout ();
	}
}
