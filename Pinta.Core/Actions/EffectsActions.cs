//
// EffectsActions.cs
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

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Pinta.Core;

public sealed class EffectsActions
{
	public Dictionary<string, Gio.Menu> Menus { get; } = [];
	public Collection<Command> Actions { get; } = [];

	/// <summary>
	/// Re-applies the last applied effect with its last settings, without showing its dialog.
	/// </summary>
	public Command RepeatEffect { get; }

	private readonly ChromeManager chrome;

	// The Effects menu has a section holding the Repeat item, followed by a section with the categories.
	private Gio.Menu? repeat_section;
	private Gio.Menu? categories_section;
	private bool has_repeatable_effect;

	public EffectsActions (ChromeManager chrome)
	{
		this.chrome = chrome;

		RepeatEffect = new Command (
			"repeateffect",
			Translations.GetString ("Repeat"),
			null,
			null,
			shortcuts: ["<Primary>F"]) {
			Sensitive = false,
		};
	}

	#region Initialization
	private Gio.Menu GetCategoriesSection ()
	{
		if (categories_section is not null)
			return categories_section;

		chrome.Application.AddCommand (RepeatEffect);

		repeat_section = Gio.Menu.New ();
		repeat_section.AppendItem (RepeatEffect.CreateMenuItem ());
		chrome.EffectsMenu.AppendSection (null, repeat_section);

		categories_section = Gio.Menu.New ();
		chrome.EffectsMenu.AppendSection (null, categories_section);

		return categories_section;
	}

	/// <summary>
	/// Adds an effect to the submenu for <paramref name="category"/>, or directly to the Effects menu when the category is empty
	/// (as Paint.NET does for plugins without a submenu).
	/// </summary>
	public void AddEffect (string category, Command action)
	{
		var effects_menu = GetCategoriesSection ();

		if (string.IsNullOrEmpty (category)) {
			Actions.Add (action);
			effects_menu.AppendMenuItemSorted (action.CreateMenuItem ());
			return;
		}

		if (!Menus.ContainsKey (category)) {
			var category_menu = Gio.Menu.New ();
			effects_menu.AppendMenuItemSorted (Gio.MenuItem.NewSubmenu (category, category_menu));
			Menus.Add (category, category_menu);
		}

		Actions.Add (action);

		Gio.Menu m = Menus[category];
		m.AppendMenuItemSorted (action.CreateMenuItem ());
	}

	// TODO: Remove menu category if empty
	internal void RemoveEffect (string category, Command action)
	{
		if (string.IsNullOrEmpty (category)) {
			categories_section?.Remove (action);
			return;
		}

		if (!Menus.ContainsKey (category))
			return;

		var menu = Menus[category];
		menu.Remove (action);
	}
	#endregion

	#region Public Methods
	public void ToggleActionsSensitive (bool sensitive)
	{
		foreach (Command a in Actions)
			a.Sensitive = sensitive;

		RepeatEffect.Sensitive = sensitive && has_repeatable_effect;
	}

	/// <summary>
	/// Updates the Repeat menu item to name the last applied effect, and enables it.
	/// </summary>
	public void SetRepeatableEffect (string effectName)
		=> UpdateRepeatItem (true, Translations.GetString ("Repeat {0}", effectName));

	/// <summary>
	/// Resets the Repeat menu item to its initial disabled state, e.g. when the repeated effect is removed.
	/// </summary>
	public void ClearRepeatableEffect ()
		=> UpdateRepeatItem (false, RepeatEffect.Label);

	private void UpdateRepeatItem (bool repeatable, string label)
	{
		has_repeatable_effect = repeatable;
		RepeatEffect.Sensitive = repeatable;

		if (repeat_section is null)
			return;

		// Menu items are immutable once added, so replace the item to change its label.
		repeat_section.RemoveAll ();
		repeat_section.AppendItem (Gio.MenuItem.New (label, RepeatEffect.FullName));
	}
	#endregion
}
