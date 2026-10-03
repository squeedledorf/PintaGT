//
// ShapeTool.cs
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
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// A tool that draws one editable shape at a time with a <see cref="BaseEditEngine"/>: Paint.NET's
/// Shapes and Line/Curve tools.
/// </summary>
public abstract class ShapeTool : BaseTool
{
	public abstract BaseEditEngine EditEngine { get; }

	public ShapeTool (IServiceProvider services) : base (services) { }

	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_O);
	protected override bool ShowAntialiasingButton => true;
	protected override bool ShowBlendModeButton => true;
	protected override bool ShowSelectionQualityButton => true;
	protected override bool ShowFinishButton => true;
	protected override bool CanFinish => EditEngine.IsEditing;

	protected override void OnFinish (Document document)
		=> EditEngine.HandleFinish ();

	protected override void OnBlendModeChanged ()
		=> EditEngine.Redraw ();

	private string SettingsPrefix => GetType ().Name.ToLowerInvariant ();

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		EditEngine.BuildToolBar (tb, Settings, SettingsPrefix);
	}

	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
		=> EditEngine.HandleMouseDown (document, e);

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
		=> EditEngine.HandleMouseUp (document, e);

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
		=> EditEngine.HandleMouseMove (document, e);

	protected override void OnActivated (Document? document)
	{
		EditEngine.HandleActivated ();

		base.OnActivated (document);
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		EditEngine.HandleDeactivated ();

		base.OnDeactivated (document, newTool);
	}

	protected override void OnAfterSave (Document document)
	{
		EditEngine.HandleAfterSave ();

		base.OnAfterSave (document);
	}

	protected override void OnCommit (Document? document)
	{
		EditEngine.HandleCommit ();

		base.OnCommit (document);
	}

	protected override void OnAntialiasingChanged ()
		=> EditEngine.Redraw ();

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
		=> EditEngine.HandleKeyDown (document, e) || base.OnKeyDown (document, e);

	// Don't undo in the middle of a drag.
	protected override bool OnHandleUndo (Document document)
		=> EditEngine.HandleBeforeUndo () || base.OnHandleUndo (document);

	protected override bool OnHandleRedo (Document document)
		=> EditEngine.HandleBeforeRedo () || base.OnHandleRedo (document);

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		EditEngine.OnSaveSettings (settings, SettingsPrefix);
	}

	public override IEnumerable<IToolHandle> Handles => EditEngine.Handles;
}
