// 
// EffectsManager.cs
//  
// Author:
//	Jonathan Pobst <monkey@jpobst.com>
// 
// Copyright (c) 2011 Jonathan Pobst
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

namespace Pinta.Core;

/// <summary>
/// Provides methods for registering and unregistering effects and adjustments.
/// </summary>
public sealed class EffectsManager
{
	// Keyed by runtime type and name, so one wrapper type (such as the Paint.NET plugin adapter) can register many effects.
	private readonly record struct EffectKey (Type Type, string Name);

	private readonly Dictionary<EffectKey, Command> adjustments;

	private readonly Dictionary<EffectKey, Command> effects;
	private readonly Dictionary<EffectKey, string> effects_categories;

	private readonly ActionManager action_manager;
	private readonly ChromeManager chrome_manager;
	private readonly LivePreviewManager live_preview_manager;

	private BaseEffect? last_effect;

	internal EffectsManager (
		ActionManager actionManager,
		ChromeManager chromeManager,
		LivePreviewManager livePreviewManager)
	{
		adjustments = [];
		effects = [];
		effects_categories = [];

		action_manager = actionManager;
		chrome_manager = chromeManager;
		live_preview_manager = livePreviewManager;

		action_manager.Effects.RepeatEffect.Activated += (o, args) => RepeatLastEffect ();
	}

	private async void RepeatLastEffect ()
	{
		if (last_effect is null || live_preview_manager.IsEnabled)
			return;

		await live_preview_manager.Start (last_effect, skipDialog: true);
	}

	private async void ApplyEffect (BaseEffect effect)
	{
		if (live_preview_manager.IsEnabled)
			return;

		if (!await live_preview_manager.Start (effect))
			return;

		last_effect = effect;
		action_manager.Effects.SetRepeatableEffect (effect.Name);
	}

	/// <summary>
	/// Register a new adjustment with Pinta, causing it to be added to the Adjustments menu.
	/// </summary>
	/// <param name="adjustment">The adjustment to register</param>
	/// <returns>The action created for this adjustment</returns>
	public void RegisterAdjustment<T> (T adjustment) where T : BaseEffect
	{
#if false // For testing purposes to detect any missing icons. This implies more disk accesses on startup so we may not want this on by default.
		if (!GtkExtensions.GetDefaultIconTheme ().HasIcon (adjustment.Icon))
			Console.Error.WriteLine ($"Icon {adjustment.Icon} for adjustment {adjustment.Name} not found");
#endif
		EffectKey key = new (adjustment.GetType (), adjustment.Name);

		if (adjustments.ContainsKey (key))
			throw new Exception ($"An adjustment of type {key.Type} named {key.Name} is already registered");

		// Create a gtk action for each adjustment
		Command action = new (
			ActionName (adjustments, key),
			adjustment.Name + (adjustment.IsConfigurable ? Translations.GetString ("...") : ""),
			string.Empty,
			adjustment.Icon,
			shortcuts:
				adjustment.AdjustmentMenuKey is null
				? [] // If no key is specified, don't use an accelerated menu item
				: [adjustment.AdjustmentMenuKeyModifiers + adjustment.AdjustmentMenuKey]);

		action.Activated += async (o, args) => { await live_preview_manager.Start (adjustment); };

		action_manager.Adjustments.Actions.Add (action);

		chrome_manager.Application.AddCommand (action);

		chrome_manager.AdjustmentsMenu.AppendMenuItemSorted (action.CreateMenuItem ());

		adjustments.Add (key, action);
	}

	/// <summary>
	/// The action name: the type name for the first effect of a type, plus a hash of the effect name for any others.
	/// </summary>
	private static string ActionName (Dictionary<EffectKey, Command> registered, EffectKey key)
	{
		if (!registered.Keys.Any (k => k.Type == key.Type))
			return key.Type.Name;

		uint hash = 2166136261;
		foreach (char c in key.Name)
			hash = (hash ^ c) * 16777619;
		return $"{key.Type.Name}-{hash:x8}";
	}

	/// <summary>
	/// Register a new effect with Pinta, causing it to be added to the Effects menu.
	/// </summary>
	/// <param name="effect">The effect to register</param>
	/// <returns>The action created for this effect</returns>
	public void RegisterEffect<T> (T effect) where T : BaseEffect
	{
#if false // For testing purposes to detect any missing icons. This implies more disk accesses on startup so we may not want this on by default.
		if (!GtkExtensions.GetDefaultIconTheme ().HasIcon (effect.Icon))
			Console.Error.WriteLine ($"Icon {effect.Icon} for effect {effect.Name} not found");
#endif
		EffectKey key = new (effect.GetType (), effect.Name);

		if (effects.ContainsKey (key))
			throw new Exception ($"An effect of type {key.Type} named {key.Name} is already registered");

		// Create a gtk action and menu item for each effect
		Command action = new (
			ActionName (effects, key),
			effect.Name + (effect.IsConfigurable ? Translations.GetString ("...") : ""),
			string.Empty,
			effect.Icon);

		chrome_manager.Application.AddCommand (action);
		action.Activated += (o, args) => ApplyEffect (effect);

		action_manager.Effects.AddEffect (effect.EffectMenuCategory, action);

		effects.Add (key, action);
		effects_categories.Add (key, effect.EffectMenuCategory);
	}

	/// <summary>
	/// Unregister an effect with Pinta, causing it to be removed from the Effects menu.
	/// </summary>
	/// <param name="effect_type">The type of the effect to unregister</param>
	public void UnregisterInstanceOfEffect<T> () where T : BaseEffect
	{
		foreach (EffectKey key in effects.Keys.Where (k => k.Type == typeof (T)).ToArray ()) {
			Command action = effects[key];
			string category = effects_categories[key];

			if (last_effect?.GetType () == key.Type && last_effect.Name == key.Name) {
				last_effect = null;
				action_manager.Effects.ClearRepeatableEffect ();
			}

			effects.Remove (key);
			action_manager.Effects.RemoveEffect (category, action);
			effects_categories.Remove (key);
		}
	}

	/// <summary>
	/// Unregister an effect with Pinta, causing it to be removed from the Adjustments menu.
	/// </summary>
	/// <param name="adjustment_type">The type of the adjustment to unregister</param>
	public void UnregisterInstanceOfAdjustment<T> () where T : BaseEffect
	{
		foreach (EffectKey key in adjustments.Keys.Where (k => k.Type == typeof (T)).ToArray ()) {
			Command action = adjustments[key];
			adjustments.Remove (key);
			action_manager.Adjustments.Actions.Remove (action);
			chrome_manager.AdjustmentsMenu.Remove (action);
		}
	}
}
