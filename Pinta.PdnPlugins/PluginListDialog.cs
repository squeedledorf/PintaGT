using System.IO;
using System.Linq;
using Pinta.Core;

namespace Pinta.PdnPlugins;

/// <summary>
/// Lists the Paint.NET plugins Pinta found: the loaded ones, and, like Paint.NET's plugin errors
/// list, the ones that could not be loaded or failed while running, with the reason.
/// </summary>
internal static class PluginListDialog
{
	public static void Show ()
	{
		Gtk.Dialog dialog = Gtk.Dialog.New ();
		dialog.Title = Translations.GetString ("Paint.NET Plugins");
		dialog.TransientFor = PintaCore.Chrome.MainWindow;
		dialog.Modal = true;
		dialog.SetDefaultSize (760, 560);
		dialog.AddButton (Translations.GetString ("_Close"), (int) Gtk.ResponseType.Close);
		dialog.OnResponse += (_, _) => dialog.Destroy ();

		var reports = PluginRegistry.Reports;
		int loaded = reports.Count (r => r.Status == PluginStatus.Loaded);
		int problems = reports.Count - loaded;

		Gtk.Box content = dialog.GetContentAreaBox ();
		content.Spacing = 6;
		content.SetAllMargins (12);

		Gtk.Label summary = Gtk.Label.New (
			Translations.GetString ("{0} plugins loaded, {1} with errors or not supported.", loaded, problems) + "\n" +
			Translations.GetString ("Plugin folders:") + " " + string.Join ("  ", PluginHost.PluginDirectories));
		summary.Halign = Gtk.Align.Start;
		summary.Wrap = true;
		summary.Selectable = true;
		content.Append (summary);

		Gtk.ListBox list = Gtk.ListBox.New ();
		list.SelectionMode = Gtk.SelectionMode.None;
		list.AddCssClass ("boxed-list");

		// Problems first, as in Paint.NET's plugin errors list.
		foreach (PluginReport r in reports.OrderBy (r => r.Status == PluginStatus.Loaded).ThenBy (r => Path.GetFileName (r.File)).ThenBy (r => r.TypeName))
			list.Append (Row (r));

		Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New ();
		scroll.SetChild (list);
		scroll.Vexpand = true;
		scroll.HscrollbarPolicy = Gtk.PolicyType.Never;
		content.Append (scroll);

		dialog.Present ();
	}

	private static Gtk.Widget Row (PluginReport r)
	{
		string status = r.Status switch {
			PluginStatus.Loaded => Translations.GetString ("Loaded"),
			PluginStatus.Unsupported => Translations.GetString ("Not supported"),
			_ => Translations.GetString ("Error"),
		};
		string title = r.DisplayName ?? (r.TypeName.Length > 0 ? r.TypeName : Path.GetFileName (r.File));
		string details = string.Join (" · ", new[] { Path.GetFileName (r.File), r.TypeName, r.Author, r.Version is null ? null : "v" + r.Version }.Where (s => !string.IsNullOrEmpty (s)));

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		box.SetAllMargins (6);

		Gtk.Box header = Gtk.Box.New (Gtk.Orientation.Horizontal, 8);
		Gtk.Label name = Gtk.Label.New (title);
		name.Halign = Gtk.Align.Start;
		name.Hexpand = true;
		name.AddCssClass ("heading");
		Gtk.Label badge = Gtk.Label.New (status);
		badge.AddCssClass (r.Status == PluginStatus.Loaded ? "success" : r.Status == PluginStatus.Unsupported ? "warning" : "error");
		header.Append (name);
		header.Append (badge);
		box.Append (header);

		Gtk.Label info = Gtk.Label.New (details);
		info.Halign = Gtk.Align.Start;
		info.AddCssClass ("dim-label");
		info.Selectable = true;
		info.Wrap = true;
		box.Append (info);

		if (!string.IsNullOrEmpty (r.Message)) {
			Gtk.Label message = Gtk.Label.New (r.Message);
			message.Halign = Gtk.Align.Start;
			message.Wrap = true;
			message.Selectable = true;
			message.Xalign = 0;
			box.Append (message);
		}
		return box;
	}
}
