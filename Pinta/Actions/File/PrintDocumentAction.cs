//
// PrintDocumentAction.cs
//
// Author:
//       Cameron White <cameronwhite91@gmail.com>
//
// Copyright (c) 2012 Cameron White
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

/// <summary>
/// File > Print (Ctrl+P): prints the flattened image on one page, centred and scaled down to fit.
/// </summary>
internal sealed class PrintDocumentAction : IActionHandler
{
	private readonly FileActions file;
	private readonly ChromeManager chrome;
	private readonly WorkspaceManager workspace;
	private readonly ToolManager tools;

	internal PrintDocumentAction (FileActions file, ChromeManager chrome, WorkspaceManager workspace, ToolManager tools)
	{
		this.file = file;
		this.chrome = chrome;
		this.workspace = workspace;
		this.tools = tools;
	}

	void IActionHandler.Initialize () => file.Print.Activated += Activated;

	void IActionHandler.Uninitialize () => file.Print.Activated -= Activated;

	private void Activated (object? sender, EventArgs e)
	{
		if (!workspace.HasOpenDocuments)
			return;

		tools.Commit ();
		Document doc = workspace.ActiveDocument;

		Gtk.PrintOperation op = Gtk.PrintOperation.New ();
		op.SetNPages (1);
		op.SetJobName (doc.DisplayName);
		op.OnDrawPage += (_, args) => DrawPage (doc, args.Context);

		try {
			op.Run (Gtk.PrintOperationAction.PrintDialog, chrome.MainWindow);
		} catch (GLib.GException ex) {
			_ = chrome.ShowErrorDialog (chrome.MainWindow, Translations.GetString ("Printing failed"), ex.Message, ex.ToString ());
		}
	}

	private static void DrawPage (Document doc, Gtk.PrintContext context)
	{
		using Cairo.ImageSurface image = doc.GetFlattenedImage ();
		Cairo.Context g = context.GetCairoContext ();

		double scale = Math.Min (1, Math.Min (context.GetWidth () / image.Width, context.GetHeight () / image.Height));
		g.Translate ((context.GetWidth () - image.Width * scale) / 2, (context.GetHeight () - image.Height * scale) / 2);
		g.Scale (scale, scale);
		g.SetSourceSurface (image, 0, 0);
		g.Paint ();
	}
}
