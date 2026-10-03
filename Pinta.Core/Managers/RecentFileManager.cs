// 
// RecentFileManager.cs
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
using System.Collections.Generic;
using System.Linq;
using Gtk;

namespace Pinta.Core;

public sealed class RecentFileManager
{
	/// <summary>Paint.NET's File > Open Recent holds the last ten images.</summary>
	public const int MaxRecentFiles = 10;
	private const string RECENT_FILES_SETTING = "recent-files";

	private Gio.File? last_dialog_directory;
	private readonly ISettingsService? settings;
	private List<string> recent_uris;

	public RecentFileManager (ISettingsService? settings = null)
	{
		last_dialog_directory = DefaultDialogDirectory;
		this.settings = settings;
		// URIs never contain a raw newline, so one per line is a safe encoding for the string setting.
		recent_uris = [.. (settings?.GetSetting (RECENT_FILES_SETTING, "") ?? "")
			.Split ('\n', StringSplitOptions.RemoveEmptyEntries)
			.Take (MaxRecentFiles)];
	}

	/// <summary>
	/// The URIs of the images most recently opened or saved, newest first.
	/// </summary>
	public IReadOnlyList<string> RecentFiles => recent_uris;

	public event EventHandler? RecentFilesChanged;

	public Gio.File? LastDialogDirectory {
		get => last_dialog_directory;
		set {
			// The file chooser dialog may return null for the current folder in certain cases,
			// such as the Recently Used pane in the Gnome file chooser.
			if (value != null)
				last_dialog_directory = value;
		}
	}

	public Gio.File? DefaultDialogDirectory {
		get {
			// Fall back to the home folder when there is no Pictures folder, so the
			// file chooser doesn't open on "Recent" or the working directory.
			string path = System.Environment.GetFolderPath (Environment.SpecialFolder.MyPictures);
			if (!System.IO.Directory.Exists (path))
				path = System.Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
			return !string.IsNullOrEmpty (path) ? Gio.FileHelper.NewForPath (path) : null;
		}
	}

	/// <summary>
	/// Returns a directory for use in a dialog. The last dialog directory is
	/// returned if it exists, otherwise the default directory is used.
	/// </summary>
	public Gio.File? GetDialogDirectory ()
	{
		return (last_dialog_directory != null && last_dialog_directory.QueryExists (null)) ? last_dialog_directory : DefaultDialogDirectory;
	}

	/// <summary>
	/// Add a file to the list of recently-used files.
	/// </summary>
	public void AddFile (Gio.File file)
	{
		RecentManager.GetDefault ().AddItem (file.GetUri ());
		Remember (file.GetUri ());
	}

	/// <summary>
	/// Moves the URI to the top of the Open Recent list, dropping the oldest entry past ten.
	/// </summary>
	public void Remember (string uri)
	{
		recent_uris.Remove (uri);
		recent_uris.Insert (0, uri);
		if (recent_uris.Count > MaxRecentFiles)
			recent_uris.RemoveRange (MaxRecentFiles, recent_uris.Count - MaxRecentFiles);
		OnRecentFilesChanged ();
	}

	/// <summary>
	/// Removes a URI from the Open Recent list, e.g. one that no longer opens.
	/// </summary>
	public void Forget (string uri)
	{
		if (recent_uris.Remove (uri))
			OnRecentFilesChanged ();
	}

	/// <summary>
	/// File > Open Recent > Clear this list.
	/// </summary>
	public void ClearRecentFiles ()
	{
		recent_uris.Clear ();
		OnRecentFilesChanged ();
	}

	private void OnRecentFilesChanged ()
	{
		settings?.PutSetting (RECENT_FILES_SETTING, string.Join ('\n', recent_uris));
		RecentFilesChanged?.Invoke (this, EventArgs.Empty);
	}
}
