//
// CloseDocumentAction.cs
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
using Cairo;
using Pinta.Core;

namespace Pinta.Actions;

internal sealed class CloseDocumentAction : IActionHandler
{
	private readonly ActionManager actions;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;
	internal CloseDocumentAction (
		ActionManager actions,
		ChromeManager chrome,
		WorkspaceManager workspace,
		ToolManager tools)
	{
		this.actions = actions;
		this.chrome = chrome;
		this.workspace = workspace;
		this.tools = tools;
	}

	void IActionHandler.Initialize ()
	{
		actions.File.Close.Activated += Activated;
	}

	void IActionHandler.Uninitialize ()
	{
		actions.File.Close.Activated -= Activated;
	}

	private async void Activated (object sender, EventArgs e)
	{
		// Commit any pending changes
		tools.Commit ();

		// If it's not dirty, just close it
		if (!workspace.ActiveDocument.IsDirty) {
			workspace.CloseActiveDocument ();
			return;
		}

		Document document = workspace.ActiveDocument;

		// Paint.NET's Unsaved Changes task dialog.
		string message = Translations.GetString (
			"{0} has unsaved changes. What would you like to do?",
			document.DisplayName);

		using ImageSurface flattened = document.GetFlattenedImage ();

		const int save_response = 0;
		const int discard_response = 1;
		const int cancel_response = 2;

		int response = TaskDialog.RunBlocking (
			chrome.MainWindow,
			Translations.GetString ("Unsaved Changes"),
			message,
			[
				new (Translations.GetString ("_Save"), Translations.GetString ("Save the image, and then close it."), Resources.StandardIcons.DocumentSave),
				new (Translations.GetString ("Do_n't Save"), Translations.GetString ("Discard the unsaved changes."), Resources.Icons.LayerDelete),
				new (Translations.GetString ("_Cancel"), Translations.GetString ("Go back to Pinta."), Resources.StandardIcons.EditUndo),
			],
			cancel_response,
			width: 340,
			thumbnail: TaskDialog.CreateThumbnail (flattened));

		if (response == save_response) {

			bool saved = await workspace.ActiveDocument.Save (false);

			// If saved is false, then the user
			// must have cancelled the Save dialog
			if (saved)
				workspace.CloseActiveDocument ();

		} else if (response == discard_response) {
			workspace.CloseActiveDocument ();
		}

	}
}
