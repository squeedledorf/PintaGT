using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pinta.PdnPlugins;

internal enum PluginStatus
{
	Loaded,
	Unsupported,
	Failed,
}

/// <summary>One line of the plugin list: a plugin class, or a whole DLL that could not be read.</summary>
internal sealed record PluginReport (
	string File,
	string TypeName,
	string? DisplayName,
	PluginStatus Status,
	string? Message,
	string? Author = null,
	string? Version = null,
	string? Website = null);

/// <summary>Everything the loader found, for the "Paint.NET Plugins" dialog. Also records render errors.</summary>
internal static class PluginRegistry
{
	private static readonly List<PluginReport> reports = [];
	private static readonly object sync = new ();

	public static event EventHandler? Changed;

	public static IReadOnlyList<PluginReport> Reports {
		get { lock (sync) return reports.ToArray (); }
	}

	public static void Add (PluginReport report)
	{
		lock (sync)
			reports.Add (report);
		Console.Error.WriteLine ($"Paint.NET plugin {report.Status}: {System.IO.Path.GetFileName (report.File)} {report.TypeName} {report.Message}".TrimEnd ());
		Changed?.Invoke (null, EventArgs.Empty);
	}

	/// <summary>Records an exception thrown by a plugin while it was running (first one per plugin per minute is enough).</summary>
	public static void AddRuntimeError (string file, string typeName, string displayName, Exception ex)
	{
		lock (sync) {
			if (reports.Any (r => r.TypeName == typeName && r.Status == PluginStatus.Failed && r.Message == Describe (ex)))
				return;
		}
		Add (new PluginReport (file, typeName, displayName, PluginStatus.Failed, Describe (ex)));
		Console.Error.WriteLine (ex);
	}

	/// <summary>A short, readable description of what went wrong, including the missing API when it is one.</summary>
	public static string Describe (Exception ex)
	{
		while (ex is System.Reflection.TargetInvocationException or TypeInitializationException && ex.InnerException is not null)
			ex = ex.InnerException;
		string what = ex switch {
			DllNotFoundException => "calls a Windows-only native library",
			EntryPointNotFoundException => "calls a Windows-only native function",
			MissingMemberException => "needs an API Pinta does not provide yet",
			TypeLoadException => "needs a type Pinta does not provide",
			FileNotFoundException => "needs a library that is not available",
			PlatformNotSupportedException => "uses a Windows-only feature",
			_ => "failed",
		};
		return $"{what}: {ex.GetType ().Name}: {ex.Message}";
	}
}
