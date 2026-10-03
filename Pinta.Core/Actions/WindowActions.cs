//
// WindowActions.cs
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

namespace Pinta.Core;

public sealed class WindowActions
{
	private Gio.Menu doc_section = null!; // NRT - Set in RegisterActions
	private static readonly string doc_action_id = "active_document";
	private readonly Gio.SimpleAction active_doc_action;
	private uint deferred_update_event;

	public Command SaveAll { get; }
	public Command CloseAll { get; }
	public Command NextImage { get; }
	public Command PreviousImage { get; }

	private readonly WorkspaceManager workspace;
	public WindowActions (WorkspaceManager workspace)
	{
		SaveAll = new Command (
			"SaveAll",
			Translations.GetString ("Save All"),
			null,
			Resources.StandardIcons.DocumentSave,
			shortcuts: ["<Primary><Alt>S"]);

		CloseAll = new Command (
			"CloseAll",
			Translations.GetString ("Close All"),
			null,
			Resources.StandardIcons.WindowClose,
			shortcuts: ["<Primary><Shift>W"]);

		NextImage = new Command (
			"NextImage",
			Translations.GetString ("Next Image"),
			null,
			null,
			shortcuts: ["<Primary>Tab", "<Primary>Page_Down"]);

		PreviousImage = new Command (
			"PreviousImage",
			Translations.GetString ("Previous Image"),
			null,
			null,
			shortcuts: ["<Primary><Shift>Tab", "<Primary><Shift>ISO_Left_Tab", "<Primary>Page_Up"]);

		NextImage.Activated += (_, _) => CycleActiveDocument (1);
		PreviousImage.Activated += (_, _) => CycleActiveDocument (-1);

		// TODO: Make `Command`
		active_doc_action = Gio.SimpleAction.NewStateful (
			doc_action_id,
			GtkExtensions.IntVariantType,
			GLib.Variant.NewInt32 (-1));

		active_doc_action.OnActivate += (o, e) => {
			int idx = e.Parameter!.GetInt32 ();
			workspace.SetActiveDocument (idx);
			active_doc_action.ChangeState (e.Parameter);
		};

		workspace.DocumentActivated += (o, e) => {
			e.Document.Renamed += (_, _) => RebuildDocumentMenu ();
			e.Document.IsDirtyChanged += (_, _) => RebuildDocumentMenu ();
			AddDocumentMenuItem (workspace.OpenDocuments.IndexOf (e.Document));
		};

		workspace.ActiveDocumentChanged += OnActiveDocumentChanged;
		workspace.DocumentClosed += (_, _) => RebuildDocumentMenu ();
		workspace.DocumentsReordered += (_, _) => {
			RebuildDocumentMenu ();
			OnActiveDocumentChanged (null, System.EventArgs.Empty);
		};

		this.workspace = workspace;
	}

	public void RegisterActions (
		Gtk.Application app,
		Gio.Menu menu)
	{
		menu.AppendItem (SaveAll.CreateMenuItem ());
		menu.AppendItem (CloseAll.CreateMenuItem ());

		doc_section = Gio.Menu.New ();
		menu.AppendSection (null, doc_section);

		app.AddCommands ([
			SaveAll,
			CloseAll,
			NextImage,
			PreviousImage]);

		app.AddAction (active_doc_action);

		// Assign accelerators up to Ctrl-9 / Alt-9 for the active documents.
		for (int i = 0; i < 9; ++i)
			app.SetAccelsForAction (BuildActionId (i), [$"<Primary>{i + 1}", $"<Alt>{i + 1}"]);
	}

	private void CycleActiveDocument (int step)
	{
		int count = workspace.OpenDocuments.Count;
		if (count < 2)
			return;

		workspace.SetActiveDocument ((workspace.ActiveDocumentIndex + step + count) % count);
	}

	private void AddDocumentMenuItem (int idx)
	{
		Document doc = workspace.OpenDocuments[idx];
		string actionId = BuildActionId (idx);
		string label = $"{doc.DisplayName}{(doc.IsDirty ? "*" : string.Empty)}";

		Gio.MenuItem menuItem = Gio.MenuItem.New (label, actionId);
		doc_section.AppendItem (menuItem);
	}

	private static string BuildActionId (int idx) => $"app.{doc_action_id}({idx})";

	private void RebuildDocumentMenu ()
	{
		doc_section.RemoveAll ();

		for (int i = 0; i < workspace.OpenDocuments.Count; ++i)
			AddDocumentMenuItem (i);

		workspace.ResetTitle ();
	}

	private void OnActiveDocumentChanged (object? o, System.EventArgs eventArgs)
	{
		// Updating the action's state can be surprisingly expensive when e.g. opening
		// many documents (bug #1574), so an update is deferred until we return to the event loop.
		if (deferred_update_event > 0)
			return;

		deferred_update_event = GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
			active_doc_action.ChangeState (GLib.Variant.NewInt32 (workspace.ActiveDocumentIndex));
			deferred_update_event = 0;
			return false;
		});
	}
}
