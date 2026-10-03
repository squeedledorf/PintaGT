// 
// TextLayout.cs
//  
// Author:
//       Cameron White <cameronwhite91@gmail.com>
// 
// Copyright (c) 2015 Cameron White
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
using System.Collections.Immutable;
using System.Security;

namespace Pinta.Core;

public sealed class TextLayout
{
	private TextEngine engine = null!; // NRT - Not sure how this is set, but all callers assume it is not-null

	public TextEngine Engine {
		get => engine;
		set {
			if (engine != null)
				engine.Modified -= OnEngineModified;
			engine = value;
			engine.Modified += OnEngineModified;
			OnEngineModified (this, EventArgs.Empty);
		}
	}

	public Pango.Layout Layout { get; }
	public int FontHeight => GetCursorLocation ().Height;

	/// <summary>
	/// Top-left corner of the layout. The engine's Origin is the anchor point the user clicked:
	/// the left edge for left-aligned text, the middle for centered text and the right edge for
	/// right-aligned text, as in Paint.NET.
	/// </summary>
	public PointI LayoutOrigin {
		get {
			Layout.GetPixelExtents (out _, out RectangleI logical);
			int offset = engine.Alignment switch {
				TextAlignment.Center => -logical.Width / 2,
				TextAlignment.Right => -logical.Width,
				_ => 0,
			};
			return new (engine.Origin.X + offset, engine.Origin.Y);
		}
	}

	public TextLayout (IChromeService chrome)
	{
		Layout = Pango.Layout.New (chrome.MainWindow.GetPangoContext ());
	}

	public ImmutableArray<RectangleI> GetSelectionRectangles ()
	{
		var regions = engine.SelectionRegions;
		var rects = ImmutableArray.CreateBuilder<RectangleI> (regions.Length);
		foreach (var region in regions) {
			PointI p1 = TextPositionToPoint (region.Key);
			PointI p2 = TextPositionToPoint (region.Value);
			rects.Add (new RectangleI (p1, new Size (p2.X - p1.X, FontHeight)));
		}
		return rects.MoveToImmutable ();
	}

	public RectangleI GetCursorLocation ()
	{
		int index = engine.PositionToUTF8Index (engine.CurrentPosition);
		Layout.GetCursorPos (index, out RectangleI strong, out _);

		PointI origin = LayoutOrigin;
		int x = PangoExtensions.UnitsToPixels (strong.X) + origin.X;
		int y = PangoExtensions.UnitsToPixels (strong.Y) + origin.Y;
		int w = PangoExtensions.UnitsToPixels (strong.Width);
		int h = PangoExtensions.UnitsToPixels (strong.Height);

		return new (x, y, w, h);
	}

	public RectangleI GetLayoutBounds ()
	{
		Layout.GetPixelExtents (out RectangleI ink, out _);
		var cursor = GetCursorLocation ();

		// GetPixelExtents() doesn't really return a very sensible height.
		// Instead of doing some hacky arithmetic to correct it, the height will just
		// be the cursor's height times the number of lines.
		PointI origin = LayoutOrigin;
		return new (
			origin.X,
			origin.Y,
			ink.Width,
			cursor.Height * engine.LineCount);
	}

	public TextPosition PointToTextPosition (PointI point)
	{
		PointI origin = LayoutOrigin;
		int x = PangoExtensions.UnitsFromPixels (point.X - origin.X);
		int y = PangoExtensions.UnitsFromPixels (point.Y - origin.Y);

		Layout.XyToIndex (x, y, out int index, out int trailing);

		return engine.UTF8IndexToPosition (index + trailing);
	}

	public PointI TextPositionToPoint (TextPosition p)
	{
		int index = engine.PositionToUTF8Index (p);

		Layout.IndexToPos (index, out RectangleI rect);

		PointI origin = LayoutOrigin;
		int x = PangoExtensions.UnitsToPixels (rect.X) + origin.X;
		int y = PangoExtensions.UnitsToPixels (rect.Y) + origin.Y;

		return new PointI (x, y);
	}

	private void OnEngineModified (object? sender, EventArgs e)
	{
		string? markup = SecurityElement.Escape (engine.ToString ());

		if (engine.Underline)
			markup = $"<u>{markup}</u>";

		if (engine.Strikethrough)
			markup = $"<s>{markup}</s>";

		switch (engine.Alignment) {
			case TextAlignment.Right:
				Layout.SetAlignment (Pango.Alignment.Right);
				break;
			case TextAlignment.Center:
				Layout.SetAlignment (Pango.Alignment.Center);
				break;
			case TextAlignment.Left:
				Layout.SetAlignment (Pango.Alignment.Left);
				break;
		}

		Layout.SetFontDescription (engine.Font);

		Layout.SetMarkup (markup, -1);
	}
}
