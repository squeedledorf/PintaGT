//
// EraserTool.cs
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
using Gtk;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class EraserTool : BaseBrushTool
{
	private readonly IWorkspaceService workspace;

	public EraserTool (IServiceProvider services) : base (services)
	{
		workspace = services.GetService<IWorkspaceService> ();

		// Update cursor on zoom
		workspace.ViewSizeChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}
		};
	}

	public override string Name
		=> Translations.GetString ("Eraser");

	public override string Icon
		=> Pinta.Resources.Icons.ToolEraser;

	public override string StatusBarText
		=> Translations.GetString ("Left click to erase using the primary color's transparency, right click to erase using the secondary color's transparency.");

	public override Gdk.Key ShortcutKey
		=> new (Gdk.Constants.KEY_E);

	public override int Priority => 23;

	public override Gdk.Cursor DefaultCursor {
		get {
			double scale = workspace.GetScale ();
			var icon = GdkExtensions.CreateIconWithShape (
				"Cursor.Eraser.png",
				CursorShape.Ellipse,
				scale,
				BrushWidthCeiling,
				8,
				22,
				out int iconOffsetX,
				out int iconOffsetY);

			return Gdk.Cursor.NewFromTexture (icon, iconOffsetX, iconOffsetY, null);
		}
	}

	protected override bool ShowDabOptions => true;

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		if (mouse_button == MouseButton.None)
			return;

		Erase (document, StampDabs (e.PointDouble));
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		if (mouse_button != MouseButton.None)
			Erase (document, FinishDabStroke ());

		base.OnMouseUp (document, e);
	}

	/// <summary>
	/// Recomputes the layer inside <paramref name="dirty"/> from the undo surface and the stroke mask,
	/// so overlapping dabs never erase a pixel more than once.
	/// </summary>
	private void Erase (Document document, RectangleI dirty)
	{
		if (dirty.IsEmpty || StrokeMask is null || undo_surface is null)
			return;

		surface_modified = true;

		// Left click erases by the primary color's alpha, right click by the secondary's.
		// The eraser never paints color.
		double eraseAlpha = (mouse_button == MouseButton.Right ? Palette.SecondaryColor : Palette.PrimaryColor).A;

		using (Context g = document.CreateClippedContext ()) {
			g.Rectangle (dirty.ToDouble ());
			g.Clip ();

			g.Operator = Operator.Source;
			g.SetSourceSurface (undo_surface, 0, 0);
			g.Paint ();

			g.Operator = Operator.DestOut;
			g.SetSourceSurface (StrokeMask, 0, 0);
			g.PaintWithAlpha (eraseAlpha);
		}

		document.Workspace.Invalidate (dirty);
	}
}
