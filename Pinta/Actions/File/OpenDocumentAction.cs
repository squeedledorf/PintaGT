// 
// OpenDocumentAction.cs
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
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class OpenDocumentAction : IActionHandler
{
	private readonly FileActions file;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly RecentFileManager recent_files;
	private readonly ImageConverterManager image_formats;
	internal OpenDocumentAction (
		FileActions file,
		ChromeManager chrome,
		WorkspaceManager workspace,
		RecentFileManager recentFiles,
		ImageConverterManager imageFormats)
	{
		this.file = file;
		this.chrome = chrome;
		this.workspace = workspace;
		recent_files = recentFiles;
		image_formats = imageFormats;
	}

	void IActionHandler.Initialize ()
	{
		file.Open.Activated += Activated;
		file.OpenRecent.OnActivate += HandleOpenRecent;
		file.ClearRecent.Activated += HandleClearRecent;
		recent_files.RecentFilesChanged += HandleRecentFilesChanged;
		RebuildRecentMenu ();
	}

	void IActionHandler.Uninitialize ()
	{
		file.Open.Activated -= Activated;
		file.OpenRecent.OnActivate -= HandleOpenRecent;
		file.ClearRecent.Activated -= HandleClearRecent;
		recent_files.RecentFilesChanged -= HandleRecentFilesChanged;
	}

	private void HandleRecentFilesChanged (object? sender, EventArgs e) => RebuildRecentMenu ();

	private void HandleClearRecent (object sender, EventArgs e) => recent_files.ClearRecentFiles ();

	private void HandleOpenRecent (Gio.SimpleAction sender, Gio.SimpleAction.ActivateSignalArgs args)
	{
		int index = args.Parameter!.GetInt32 ();
		if (index < 0 || index >= recent_files.RecentFiles.Count)
			return;

		string uri = recent_files.RecentFiles[index];
		Gio.File recent = Gio.FileHelper.NewForUri (uri);

		if (workspace.OpenFile (recent))
			recent_files.AddFile (recent);
		else if (!recent.QueryExists (null))
			recent_files.Forget (uri); // OpenFile has already said "File not found".
	}

	// Paint.NET lists "1 name.png" ... "10 name.png" with a thumbnail and the full path as a tooltip.
	// GTK menu items built from a GMenu show neither, so this is the names only.
	private void RebuildRecentMenu ()
	{
		file.RecentMenu.RemoveAll ();

		for (int i = 0; i < recent_files.RecentFiles.Count; i++) {
			Gio.File recent = Gio.FileHelper.NewForUri (recent_files.RecentFiles[i]);
			string name = recent.GetBasename () ?? recent.GetParseName ();
			// Menu labels use mnemonic syntax, so a literal underscore is doubled.
			Gio.MenuItem item = Gio.MenuItem.New ($"{i + 1} {name.Replace ("_", "__")}", $"app.{file.OpenRecent.Name}({i})");
			file.RecentMenu.AppendItem (item);
		}

		file.ClearRecent.Sensitive = recent_files.RecentFiles.Count > 0;
	}

	private async void Activated (object sender, EventArgs e)
	{
		using Gtk.FileFilter imagesFilter = CreateImagesFilter ();
		using Gtk.FileFilter catchAllFilter = CreateCatchAllFilter ();

		using Gio.ListStore filters = Gio.ListStore.New (Gtk.FileFilter.GetGType ());
		filters.Append (imagesFilter);
		filters.Append (catchAllFilter);

		using Gtk.FileDialog fileDialog = Gtk.FileDialog.New ();
		fileDialog.SetTitle (Translations.GetString ("Open Image File"));
		fileDialog.SetFilters (filters);
		fileDialog.Modal = true;

		if (recent_files.GetDialogDirectory () is Gio.File dir && dir.QueryExists (null))
			fileDialog.SetInitialFolder (dir);

		var selection = await fileDialog.OpenFilesAsync (chrome.MainWindow);

		if (selection is null)
			return;

		foreach (var file in selection) {

			if (!workspace.OpenFile (file))
				continue;

			recent_files.AddFile (file);

			Gio.File? directory = file.GetParent ();

			if (directory is not null)
				recent_files.LastDialogDirectory = directory;
		}
	}

	private static Gtk.FileFilter CreateCatchAllFilter ()
	{
		Gtk.FileFilter result = Gtk.FileFilter.New ();
		result.Name = Translations.GetString ("All files");
		result.AddPattern ("*");
		return result;
	}

	private Gtk.FileFilter CreateImagesFilter ()
	{
		Gtk.FileFilter result = Gtk.FileFilter.New ();

		result.Name = Translations.GetString ("Image files");

		foreach (var format in image_formats.Formats) {

			if (!format.IsImportAvailable ())
				continue;

			foreach (var ext in format.Extensions)
				result.AddPattern ($"*.{ext}");

			// On Unix-like systems, file extensions are often considered optional.
			// Files can often also be identified by their MIME types.
			// Windows does not understand MIME types natively.
			// Adding a MIME filter on Windows would break the native file picker and force a GTK file picker instead.
			if (SystemManager.GetOperatingSystem () != OS.Windows) {
				foreach (var mime in format.Mimes)
					result.AddMimeType (mime);
			}
		}

		return result;
	}
}
