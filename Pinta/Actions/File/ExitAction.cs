// 
// ExitProgramAction.cs
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
using System.Threading.Tasks;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class ExitProgramAction : IActionHandler
{
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	internal ExitProgramAction (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace)
	{
		this.actions = actions;
		this.chrome = chrome;
		this.workspace = workspace;
	}

	void IActionHandler.Initialize ()
	{
		actions.App.Exit.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.App.Exit.Activated -= Activated;
	}

	private bool running;

	private async void Activated (object sender, EventArgs e)
	{
		// The window's close button can fire again while the prompt is up.
		if (running)
			return;

		running = true;
		try {
			if (!await ConfirmUnsavedChanges ())
				return;
		} finally {
			running = false;
		}

		while (workspace.HasOpenDocuments)
			workspace.CloseActiveDocument ();

		// Let everyone know we are quitting
		actions.App.RaiseBeforeQuit ();

		chrome.Application.Quit ();
	}

	/// <summary>
	/// Paint.NET asks once for all unsaved images, listing them as thumbnails.
	/// Returns false if the user cancelled, or cancelled one of the saves.
	/// </summary>
	private async Task<bool> ConfirmUnsavedChanges ()
	{
		// Commit any pending changes (an open text or shape edit marks the image dirty).
		PintaCore.Tools.Commit ();

		List<Document> unsaved = workspace.OpenDocuments.Where (d => d.IsDirty).ToList ();
		if (unsaved.Count == 0)
			return true;

		const int save_response = 0;
		const int cancel_response = 2;

		int response = await TaskDialog.Show (
			chrome.MainWindow,
			Translations.GetString ("Unsaved Changes"),
			Translations.GetString ("The following images that are open have changes that have not been saved. You may click on a thumbnail to show the image in the main window."),
			[
				new (Translations.GetString ("_Save"), Translations.GetString ("Save the images that are in the list above, and then exit."), Resources.StandardIcons.DocumentSave),
				new (Translations.GetString ("Do_n't Save"), Translations.GetString ("Discard all unsaved changes, and then exit."), Resources.Icons.LayerDelete),
				new (Translations.GetString ("_Cancel"), Translations.GetString ("Go back to Pinta."), Resources.StandardIcons.EditUndo),
			],
			cancel_response,
			width: 450,
			thumbnailStrip: TaskDialog.CreateThumbnailStrip (unsaved, workspace));

		if (response == cancel_response)
			return false;

		if (response == save_response) {
			foreach (Document document in unsaved) {
				workspace.SetActiveDocument (workspace.OpenDocuments.IndexOf (document));

				// A cancelled Save As (or a failed save) stops the exit.
				if (!await document.Save (false))
					return false;
			}
		}

		return true;
	}
}
