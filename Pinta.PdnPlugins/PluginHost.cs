using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using PaintDotNet;
using PaintDotNet.Effects;
using Pinta.Core;

namespace Pinta.PdnPlugins;

/// <summary>
/// Finds Paint.NET effect plugins, loads the ones Pinta can run, and registers them in the
/// Effects and Adjustments menus. Nothing a plugin does here may take Pinta down: every
/// failure is recorded in <see cref="PluginRegistry"/> instead.
/// </summary>
internal static class PluginHost
{
	private static readonly List<PdnEffectAdapter> registered = [];
	// A collectible context starts unloading once its object is unreachable, even while its types are in use.
	private static readonly List<PluginLoadContext> contexts = [];
	private static readonly HashSet<string> loaded_types = [];

	/// <summary>
	/// Plugin folders: ~/.config/PintaGT/PdnPlugins/Effects and Effects/ next to Pinta.
	/// Loose DLLs in a folder share one load context; each subfolder gets its own.
	/// </summary>
	public static IEnumerable<string> PluginDirectories => [
		Path.Combine (PintaCore.Settings.GetUserSettingsDirectory (), "PdnPlugins", "Effects"),
		Path.Combine (AppContext.BaseDirectory, "Effects"),
	];

	public static void LoadAll ()
	{
		Stopwatch timer = Stopwatch.StartNew ();
		foreach (string dir in PluginDirectories.Distinct ()) {
			if (!Directory.Exists (dir))
				continue;
			LoadDirectory (dir);
			foreach (string sub in Directory.EnumerateDirectories (dir).Order ())
				LoadDirectory (sub);
		}
		Console.Error.WriteLine ($"Paint.NET plugins: {registered.Count} effects loaded in {timer.ElapsedMilliseconds} ms");
	}

	public static void UnregisterAll ()
	{
		PintaCore.Effects.UnregisterInstanceOfEffect<PdnEffectAdapter> ();
		PintaCore.Effects.UnregisterInstanceOfAdjustment<PdnEffectAdapter> ();
		registered.Clear ();
		loaded_types.Clear ();
		foreach (PluginLoadContext c in contexts)
			c.Unload ();
		contexts.Clear ();
	}

	private static void LoadDirectory (string dir)
	{
		PluginLoadContext? context = null;
		foreach (string file in Directory.EnumerateFiles (dir, "*.dll").Order (StringComparer.OrdinalIgnoreCase)) {
			ScannedAssembly scan;
			try {
				scan = PluginScanner.Scan (file);
			} catch (Exception ex) {
				PluginRegistry.Add (new PluginReport (file, string.Empty, null, PluginStatus.Failed, $"could not be read: {ex.Message}"));
				continue;
			}
			if (!scan.ReferencesPaintDotNet || scan.Classes.Count == 0)
				continue; // a dependency, or not a plugin

			foreach (ScannedClass c in scan.Classes.Where (c => c.UnsupportedReason is not null))
				PluginRegistry.Add (new PluginReport (file, c.FullName, null, PluginStatus.Unsupported, c.UnsupportedReason));

			List<ScannedClass> runnable = scan.Classes.Where (c => c.UnsupportedReason is null).ToList ();
			if (runnable.Count == 0)
				continue;

			Assembly assembly;
			try {
				if (context is null) {
					context = new PluginLoadContext (dir);
					contexts.Add (context);
				}
				assembly = context.LoadFromAssemblyPath (file);
			} catch (Exception ex) {
				PluginRegistry.Add (new PluginReport (file, string.Empty, null, PluginStatus.Failed, PluginRegistry.Describe (ex)));
				continue;
			}

			foreach (ScannedClass c in runnable)
				LoadClass (file, assembly, c.FullName);
		}
	}

	private static void LoadClass (string file, Assembly assembly, string typeName)
	{
		PluginSupportInfoAttribute? support = null;
		IPluginSupportInfo? supportInfo = null;
		try {
			Type type = assembly.GetType (typeName, throwOnError: true)!;
			if (!loaded_types.Add (type.AssemblyQualifiedName ?? typeName)) {
				PluginRegistry.Add (new PluginReport (file, typeName, null, PluginStatus.Unsupported, "is already loaded from another folder"));
				return;
			}

			support = SafeGet (() => type.GetCustomAttribute<PluginSupportInfoAttribute> (true) ?? assembly.GetCustomAttribute<PluginSupportInfoAttribute> ());
			supportInfo = support?.PluginSupportInfoType is Type st ? SafeGet (() => Activator.CreateInstance (st) as IPluginSupportInfo) : support;

			object instance = Activator.CreateInstance (type)!;
			using IDisposable _ = (IDisposable) instance;

			PdnPluginInfo info;
			switch (instance) {
				case Effect effect:
					if (effect is not PropertyBasedEffect && (effect.Options.Flags & EffectFlags.Configurable) != 0) {
						Report (PluginStatus.Unsupported, effect.Name, "uses its own Windows Forms settings dialog");
						return;
					}
					info = new () {
						EffectType = type,
						File = file,
						Name = string.IsNullOrWhiteSpace (effect.Name) ? type.Name : effect.Name,
						MenuCategory = MenuCategory (effect.SubMenuName),
						IconName = InstallIcon (effect.Image),
						Category = effect.Category,
						ClassicOptions = effect.Options with { },
					};
					break;
				case BitmapEffect bitmapEffect:
					bool configurable = bitmapEffect.OptionsBase.IsConfigurable;
					if (bitmapEffect is not PropertyBasedBitmapEffect && configurable) {
						Report (PluginStatus.Unsupported, bitmapEffect.Name, "uses its own Windows Forms settings dialog");
						return;
					}
					info = new () {
						EffectType = type,
						File = file,
						Name = string.IsNullOrWhiteSpace (bitmapEffect.Name) ? type.Name : bitmapEffect.Name,
						MenuCategory = MenuCategory (bitmapEffect.SubMenuName),
						IconName = InstallIcon (bitmapEffect.Image),
						Category = bitmapEffect.Category,
						BitmapEffectConfigurable = configurable,
					};
					break;
				default:
					Report (PluginStatus.Unsupported, null, "is not an effect type Pinta knows");
					return;
			}

			if (registered.Any (r => r.Info.Name == info.Name && r.Info.Category == info.Category)) {
				Report (PluginStatus.Unsupported, info.Name, "a plugin with the same name is already loaded");
				return;
			}

			PdnEffectAdapter adapter = new (info);
			switch (info.Category) {
				case EffectCategory.DoNotDisplay:
					break;
				case EffectCategory.Adjustment:
					PintaCore.Effects.RegisterAdjustment (adapter);
					break;
				default:
					PintaCore.Effects.RegisterEffect (adapter);
					break;
			}
			registered.Add (adapter);
			Report (PluginStatus.Loaded, info.Name, info.Category == EffectCategory.DoNotDisplay ? "hidden (DoNotDisplay)" : null);
		} catch (Exception ex) {
			Report (PluginStatus.Failed, null, PluginRegistry.Describe (ex));
		}

		void Report (PluginStatus status, string? name, string? message)
			=> PluginRegistry.Add (new PluginReport (file, typeName, name ?? supportInfo?.DisplayName, status, message,
				SafeGet (() => supportInfo?.Author), SafeGet (() => supportInfo?.Version?.ToString ()), SafeGet (() => supportInfo?.WebsiteUri?.ToString ())));
	}

	private static T? SafeGet<T> (Func<T?> get)
	{
		try {
			return get ();
		} catch (Exception) {
			return default;
		}
	}

	/// <summary>Paint.NET's standard submenus map onto Pinta's (translated) categories; no submenu means the menu itself.</summary>
	private static string MenuCategory (string? subMenuName)
	{
		if (string.IsNullOrWhiteSpace (subMenuName))
			return string.Empty;
		return subMenuName switch {
			"Artistic" or "Blurs" or "Distort" or "Noise" or "Object" or "Photo" or "Render" or "Stylize" or "Color" => Translations.GetString (subMenuName),
			_ => subMenuName,
		};
	}

	private static string? icon_dir;

	/// <summary>Writes the plugin's menu icon where GTK's icon theme finds it, and returns its icon name.</summary>
	private static string InstallIcon (System.Drawing.Image? image)
	{
		try {
			byte[]? png = image is System.Drawing.Bitmap b ? b.EncodePng () : null;
			if (png is null)
				return Resources.Icons.EffectsDefault;
			if (icon_dir is null) {
				icon_dir = Path.Combine (GLib.Functions.GetUserCacheDir (), "Pinta", "pdn-icons");
				Directory.CreateDirectory (icon_dir);
				GtkExtensions.GetDefaultIconTheme ().AddSearchPath (icon_dir);
			}
			string name = "pdn-plugin-" + Convert.ToHexString (SHA1.HashData (png))[..12].ToLowerInvariant ();
			string path = Path.Combine (icon_dir, name + ".png");
			if (!File.Exists (path))
				File.WriteAllBytes (path, png);
			return name;
		} catch (Exception) {
			return Resources.Icons.EffectsDefault;
		}
	}
}
