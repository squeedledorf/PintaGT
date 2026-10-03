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
			if (Environment.GetEnvironmentVariable ("PDN_SMOKE_ONLY") is string only && !rel.Contains (only, StringComparison.OrdinalIgnoreCase))
				continue;
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

	/// <summary>$PDN_SMOKE_IMAGE (a PNG) or a generated gradient with a half-transparent strip.</summary>
	private static Surface TestImage ()
	{
		if (Environment.GetEnvironmentVariable ("PDN_SMOKE_IMAGE") is string path && File.Exists (path)) {
			using Cairo.ImageSurface png = new (path);
			// Paint onto ARGB32 so RGB24 files get a real alpha channel.
			using Cairo.ImageSurface image = new (Cairo.Format.Argb32, png.Width, png.Height);
			using (Cairo.Context cr = new (image)) {
				cr.SetSourceSurface (png, 0, 0);
				cr.Paint ();
			}
			return RenderEnvironment.ToSurface (image);
		}
		const int W = 96, H = 64;
		Surface src = new (W, H);
		for (int y = 0; y < H; y++)
			for (int x = 0; x < W; x++)
				src[x, y] = ColorBgra.FromBgra ((byte) (x * 255 / W), (byte) (y * 255 / H), (byte) ((x + y) * 2), (byte) (x < 8 ? 128 : 255));
		return src;
	}

	private static unsafe void SavePng (Surface s, string path)
	{
		using Cairo.ImageSurface image = new (Cairo.Format.Argb32, s.Width, s.Height);
		Span<byte> data = image.GetData ();
		for (int y = 0; y < s.Height; y++)
			PixelConvert.ToPremultiplied (
				System.Runtime.InteropServices.MemoryMarshal.Cast<ColorBgra, uint> (s.GetRowSpan (y)),
				System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint> (data.Slice (y * image.Stride, s.Width * 4)));
		image.MarkDirty ();
		image.WriteToPng (path);
	}

	private static (string Result, string Name, string Detail) Run (PluginLoadContext context, string file, string typeName)
	{
		string name = string.Empty;
		try {
			Surface src = TestImage ();
			int W = src.Width, H = src.Height;
			Surface dst = src.Clone ();
			RenderArgs srcArgs = new (src), dstArgs = new (dst);

			Assembly asm = context.LoadFromAssemblyPath (file);
			Type type = asm.GetType (typeName, throwOnError: true)!;
			object instance = Activator.CreateInstance (type)!;
			using IDisposable _ = (IDisposable) instance;
			Effect? effect = instance as Effect;
			BitmapEffect? bitmapEffect = instance as BitmapEffect;
			if (effect is not null) {
				name = effect.Name;
				if (effect is not PropertyBasedEffect && (effect.Options.Flags & EffectFlags.Configurable) != 0)
					return ("unsupported", name, "own settings dialog");
				effect.EnvironmentParameters = new EffectEnvironmentParameters (ColorBgra.Black, ColorBgra.White, 2, src, null);
				effect.Services = PdnServices.Instance;
			} else {
				name = bitmapEffect!.Name;
				bitmapEffect.SetEnvironment (new BitmapEffectEnvironment (src, ColorBgra.Black, ColorBgra.White, 2, null), PdnServices.Instance);
			}

			EffectConfigToken? token = null;
			if (instance is IPropertyBasedEffect pbe) {
				var props = pbe.CreatePropertyCollection ();
				pbe.CreateConfigUI (props);
				try {
					pbe.CreateWindowProperties ();
				} catch (Exception) {
				}
				token = new PropertyBasedEffectConfigToken (props);
				if (Environment.GetEnvironmentVariable ("PDN_SMOKE_ONLY") is not null)
					foreach (var p in props)
						TestContext.Out.WriteLine ($"  {name}: {p}");
			}

			System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew ();
			long setupMs = 0;
			Task render = Task.Run (() => {
				if (bitmapEffect is not null) {
					bitmapEffect.Initialize (token);
					setupMs = timer.ElapsedMilliseconds;
					Parallel.For (0, H, y => bitmapEffect.Render (dst, new Rectangle (0, y, W, 1)));
					return;
				}
				effect!.SetRenderInfo (token, dstArgs, srcArgs);
				setupMs = timer.ElapsedMilliseconds;
				if (effect.Options.RenderingSchedule == EffectRenderingSchedule.None || (effect.Options.Flags & EffectFlags.LegacySingleRenderCall) != 0) {
					effect.Render (token, dstArgs, srcArgs, [new Rectangle (0, 0, W, H)], 0, 1);
					return;
				}
				effect.Render (token, dstArgs, srcArgs, [new Rectangle (0, 0, W, 1)], 0, 1);
				Parallel.For (1, H, y => effect.Render (token, dstArgs, srcArgs, [new Rectangle (0, y, W, 1)], 0, 1));
			});
			if (!render.Wait (TimeSpan.FromSeconds (60)))
				return ("timeout", name, $"render took longer than 60 s (setup {setupMs} ms)");
			if (render.Exception is not null)
				throw render.Exception.InnerException!;

			int changed = 0;
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
					if (dst[x, y] != src[x, y]) changed++;
			if (Environment.GetEnvironmentVariable ("PDN_SMOKE_PNG_DIR") is string pngDir)
				SavePng (dst, Path.Combine (pngDir, $"{Path.GetFileNameWithoutExtension (file)}-{type.Name}.png"));
			return ("ok", name, $"{changed} of {W * H} pixels changed; setup {setupMs} ms, total {timer.ElapsedMilliseconds} ms");
		} catch (Exception ex) {
			Exception inner = ex is AggregateException ae ? ae.Flatten ().InnerException ?? ex : ex;
			if (inner is TargetInvocationException { InnerException: Exception innermost }) inner = innermost;
			return ("fail", name, PluginRegistry.Describe (inner) + " @ " + (inner.StackTrace?.Split ('\n').FirstOrDefault ()?.Trim () ?? ""));
		}
	}
}
