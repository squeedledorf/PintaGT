using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PaintDotNet;
using PaintDotNet.Effects;

namespace Pinta.PdnPlugins.Tests;

/// <summary>
/// Loads every plugin under $PDN_SAMPLES and renders each runnable effect with its default settings
/// on a small test image, from several threads, the way Pinta's adapter does. Writes a TSV report
/// to $PDN_SMOKE_OUT. Explicit: it needs the sample plugin set, which is not part of the repository.
/// </summary>
[TestFixture]
[Explicit ("needs PDN_SAMPLES")]
internal sealed class SamplePluginSmokeTest
{
	[Test]
	public void RenderEverySamplePlugin ()
	{
		string? root = Environment.GetEnvironmentVariable ("PDN_SAMPLES");
		if (root is null || !Directory.Exists (root))
			Assert.Ignore ("PDN_SAMPLES is not set");
		Cairo.Module.Initialize ();
		PangoCairo.Module.Initialize ();

		StringBuilder report = new ();
		report.AppendLine ("file\tclass\tname\tresult\tdetail");
		Dictionary<string, PluginLoadContext> contexts = [];
		int ok = 0, failed = 0, unsupported = 0;

		foreach (string file in Directory.EnumerateFiles (root, "*.dll", SearchOption.AllDirectories).Where (f => !f.Contains ("/_zips/")).Order ()) {
			ScannedAssembly scan;
			try {
				scan = PluginScanner.Scan (file);
			} catch (Exception) {
				continue;
			}
			string rel = Path.GetRelativePath (root, file);
			foreach (ScannedClass c in scan.Classes) {
				if (c.UnsupportedReason is not null) {
					report.AppendLine ($"{rel}\t{c.FullName}\t\tunsupported\t{c.UnsupportedReason}");
					unsupported++;
					continue;
				}
				string dir = Path.GetDirectoryName (file)!;
				if (!contexts.TryGetValue (dir, out PluginLoadContext? context))
					contexts[dir] = context = new PluginLoadContext (dir);
				(string result, string name, string detail) = Run (context, file, c.FullName);
				report.AppendLine ($"{rel}\t{c.FullName}\t{name}\t{result}\t{detail.Replace ('\n', ' ').Replace ('\t', ' ')}");
				if (result == "ok") ok++; else if (result == "unsupported") unsupported++; else failed++;
			}
		}

		string summary = $"ok {ok}, failed {failed}, unsupported {unsupported}";
		report.AppendLine ($"# {summary}");
		string? output = Environment.GetEnvironmentVariable ("PDN_SMOKE_OUT");
		if (output is not null)
			File.WriteAllText (output, report.ToString ());
		TestContext.Out.WriteLine (report.ToString ());
		Assert.That (ok, Is.GreaterThan (0), summary);
	}

	private static (string Result, string Name, string Detail) Run (PluginLoadContext context, string file, string typeName)
	{
		string name = string.Empty;
		try {
			const int W = 96, H = 64;
			Surface src = new (W, H);
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
					src[x, y] = ColorBgra.FromBgra ((byte) (x * 255 / W), (byte) (y * 255 / H), (byte) ((x + y) * 2), (byte) (x < 8 ? 128 : 255));
			Surface dst = src.Clone ();
			RenderArgs srcArgs = new (src), dstArgs = new (dst);

			Assembly asm = context.LoadFromAssemblyPath (file);
			Type type = asm.GetType (typeName, throwOnError: true)!;
			using Effect effect = (Effect) Activator.CreateInstance (type)!;
			name = effect.Name;
			if (effect is not PropertyBasedEffect && (effect.Options.Flags & EffectFlags.Configurable) != 0)
				return ("unsupported", name, "own settings dialog");
			effect.EnvironmentParameters = new EffectEnvironmentParameters (ColorBgra.Black, ColorBgra.White, 2, src, null);
			effect.Services = PdnServices.Instance;

			EffectConfigToken? token = null;
			if (effect is PropertyBasedEffect pbe) {
				var props = pbe.CreatePropertyCollection ();
				pbe.CreateConfigUI (props);
				try {
					pbe.CreateWindowProperties ();
				} catch (Exception) {
				}
				token = new PropertyBasedEffectConfigToken (props);
			}

			Task render = Task.Run (() => {
				effect.SetRenderInfo (token, dstArgs, srcArgs);
				effect.Render (token, dstArgs, srcArgs, [new Rectangle (0, 0, W, 1)], 0, 1);
				Parallel.For (1, H, y => effect.Render (token, dstArgs, srcArgs, [new Rectangle (0, y, W, 1)], 0, 1));
			});
			if (!render.Wait (TimeSpan.FromSeconds (20)))
				return ("timeout", name, "render took longer than 20 s");
			if (render.Exception is not null)
				throw render.Exception.InnerException!;

			int changed = 0;
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
					if (dst[x, y] != src[x, y]) changed++;
			return ("ok", name, $"{changed} of {W * H} pixels changed");
		} catch (Exception ex) {
			Exception inner = ex is AggregateException ae ? ae.Flatten ().InnerException ?? ex : ex;
			if (inner is TargetInvocationException { InnerException: not null } tie) inner = tie.InnerException;
			return ("fail", name, PluginRegistry.Describe (inner) + " @ " + (inner.StackTrace?.Split ('\n').FirstOrDefault ()?.Trim () ?? ""));
		}
	}
}
