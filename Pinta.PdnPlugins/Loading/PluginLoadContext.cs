using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Pinta.PdnPlugins;

/// <summary>
/// One load context per plugin folder. Paint.NET assembly names, System.Drawing(.Common) and the
/// one WinForms type the API needs are answered with facades over Pinta.PdnShim; other references are
/// looked up next to the plugin, then in Pinta's own context.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
	private static readonly Assembly shim = typeof (PaintDotNet.Surface).Assembly;
	private static readonly Assembly primitives = typeof (System.Drawing.Rectangle).Assembly;

	private readonly string directory;
	private readonly Dictionary<string, Assembly> facades = new (StringComparer.OrdinalIgnoreCase);

	private static readonly Lazy<Assembly> default_drawing = new (() =>
		Default.LoadFromStream (new MemoryStream (FacadeEmitter.Emit ("System.Drawing.Common", new Version (10, 0), [shim]))));

	static PluginLoadContext ()
	{
		// Resource readers live in Pinta's context and resolve "System.Drawing.Bitmap, System.Drawing.Common" there,
		// e.g. when a plugin's .resx holds its icon. Pinta itself never uses System.Drawing.Common.
		Default.Resolving += (_, name) => name.Name == "System.Drawing.Common" ? default_drawing.Value : null;
	}

	public PluginLoadContext (string directory) : base ($"PdnPlugins:{directory}", isCollectible: true)
	{
		this.directory = directory;
	}

	/// <summary>Assembly names answered by a facade over the shim.</summary>
	public static bool IsShimmed (string name)
		=> name.Equals ("PaintDotNet", StringComparison.OrdinalIgnoreCase)
		|| name.StartsWith ("PaintDotNet.", StringComparison.OrdinalIgnoreCase)
		|| name is "System.Drawing" or "System.Drawing.Common" or "System.Windows.Forms" or "WindowsBase" or "PresentationCore";

	protected override Assembly? Load (AssemblyName assemblyName)
	{
		string name = assemblyName.Name ?? string.Empty;

		if (name == shim.GetName ().Name)
			return shim;

		if (IsShimmed (name)) {
			// Render threads can ask for the same facade at once; an assembly name may only be loaded once per context.
			lock (facades) {
				if (facades.TryGetValue (name, out Assembly? existing))
					return existing;
				// System.Drawing (the .NET Framework name) also carries Rectangle, Color, ... from System.Drawing.Primitives.
				Assembly[] targets = name == "System.Drawing" ? [shim, primitives] : [shim];
				byte[] bytes = FacadeEmitter.Emit (name, assemblyName.Version ?? new Version (1, 0), targets);
				return facades[name] = LoadFromStream (new MemoryStream (bytes));
			}
		}

		string candidate = Path.Combine (directory, name + ".dll");
		if (File.Exists (candidate))
			return LoadFromAssemblyPath (candidate);

		return null; // Pinta's context: the framework, GirCore, ...
	}
}
