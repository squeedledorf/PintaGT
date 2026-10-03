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

// Some functions are from Paint.NET:

/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
/////////////////////////////////////////////////////////////////////////////////

using System;
using Cairo;

namespace Pinta.Core;

partial class CairoExtensions
{
	public static void BlendSurface (
		this Context g,
		Surface src,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}

	/// <summary>
	/// Paints the current source with a layer blend mode, as <see cref="Context.PaintWithAlpha"/> does.
	/// Cairo has no operator for Paint.NET's Additive, Reflect, Glow, Negation and (bitwise) Xor, so those are
	/// blended in software, which needs an image surface as the target and leaves the operator as it was.
	/// </summary>
	public static void PaintWithBlendMode (
		this Context g,
		BlendMode mode,
		double opacity = 1.0)
	{
		UserBlendOp? op = UserBlendOps.GetSoftwareBlendOp (mode);

		if (op is null) {
			g.Operator = GetBlendModeOperator (mode);
			g.PaintWithAlpha (opacity);
			return;
		}

		// The target's size, in device pixels: the clip extents with the clip and transform reset.
		g.Save ();
		g.IdentityMatrix ();
		g.ResetClip ();
		g.ClipExtents (out double x1, out double y1, out double x2, out double y2);
		g.Restore ();

		int width = (int) Math.Ceiling (x2);
		int height = (int) Math.Ceiling (y2);
		if (x1 != 0 || y1 != 0 || width <= 0 || height <= 0 || width > short.MaxValue || height > short.MaxValue) {
			// Not a plain image surface: fall back to Normal.
			g.Operator = Operator.Over;
			g.PaintWithAlpha (opacity);
			return;
		}

		// Copy the target's pixels, draw the source as it would land on the target, then blend the two.
		// ponytail: blends the whole target on every paint; limit it to the clip extents if big canvases lag.
		using ImageSurface bottom = CreateImageSurface (Format.Argb32, width, height);
		using ImageSurface blended = CreateImageSurface (Format.Argb32, width, height);

		using (Context b = new (bottom)) {
			b.SetSourceSurface (g.GetTarget (), 0, 0);
			b.Operator = Operator.Source;
			b.Paint ();
		}

		using (Context s = new (blended)) {
			Matrix matrix = CreateIdentityMatrix ();
			g.GetMatrix (matrix);
			s.SetMatrix (matrix);
			s.SetSource (g.GetSource ());
			s.PaintWithAlpha (opacity);
		}

		bottom.Flush ();
		blended.Flush ();
		BlendPixels (op, bottom.GetReadOnlyPixelData (), blended.GetPixelData ());
		blended.MarkDirty ();

		// Copy the result back with Source, which still honours the clip (and its antialiasing).
		g.Save ();
		g.IdentityMatrix ();
		g.Operator = Operator.Source;
		g.SetSourceSurface (blended, 0, 0);
		g.Paint ();
		g.Restore ();
	}

	/// <summary>
	/// Blends premultiplied <paramref name="top"/> over <paramref name="bottom"/> with a Paint.NET blend op
	/// (which works on straight alpha), writing the result into <paramref name="top"/>.
	/// </summary>
	public static void BlendPixels (UserBlendOp op, ReadOnlySpan<ColorBgra> bottom, Span<ColorBgra> top)
	{
		for (int i = 0; i < top.Length; i++) {
			top[i] = top[i].A == 0
				? bottom[i]
				: op.Apply (bottom[i].ToStraightAlpha (), top[i].ToStraightAlpha ()).ToPremultipliedAlpha ();
		}
	}

	public static void BlendSurface (
		this Context g,
		Surface src,
		RectangleD roi,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.Rectangle (roi);
		g.Clip ();
		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}
	public static void BlendSurface (
		this Context g,
		Surface src,
		PointD offset,
		BlendMode mode = BlendMode.Normal,
		double opacity = 1.0)
	{
		g.Save ();

		g.Translate (offset.X, offset.Y);
		g.SetSourceSurface (src, 0, 0);
		g.PaintWithBlendMode (mode, opacity);

		g.Restore ();
	}

	public static void SetBlendMode (
		this Context g,
		BlendMode mode)
	{
		g.Operator = GetBlendModeOperator (mode);
	}

	private static Operator GetBlendModeOperator (BlendMode mode)
		=> mode switch {
			BlendMode.Normal => Operator.Over,
			BlendMode.Multiply => (Operator) ExtendedOperators.Multiply,
			BlendMode.ColorBurn => (Operator) ExtendedOperators.ColorBurn,
			BlendMode.ColorDodge => (Operator) ExtendedOperators.ColorDodge,
			BlendMode.HardLight => (Operator) ExtendedOperators.HardLight,
			BlendMode.SoftLight => (Operator) ExtendedOperators.SoftLight,
			BlendMode.Overlay => (Operator) ExtendedOperators.Overlay,
			BlendMode.Difference => (Operator) ExtendedOperators.Difference,
			BlendMode.Color => (Operator) ExtendedOperators.HslColor,
			BlendMode.Luminosity => (Operator) ExtendedOperators.HslLuminosity,
			BlendMode.Hue => (Operator) ExtendedOperators.HslHue,
			BlendMode.Saturation => (Operator) ExtendedOperators.HslSaturation,
			BlendMode.Lighten => (Operator) ExtendedOperators.Lighten,
			BlendMode.Darken => (Operator) ExtendedOperators.Darken,
			BlendMode.Screen => (Operator) ExtendedOperators.Screen,
			BlendMode.Xor => Operator.Xor,
			_ => throw new ArgumentOutOfRangeException (nameof (mode)),
		};

	private static Status Xor (this Region region, Region other)
		=> RegionXor (region.Handle, other.Handle);

	public enum ExtendedOperators
	{
		Clear = 0,

		Source = 1,
		SourceOver = 2,
		SourceIn = 3,
		SourceOut = 4,
		SourceAtop = 5,

		Destination = 6,
		DestinationOver = 7,
		DestinationIn = 8,
		DestinationOut = 9,
		DestinationAtop = 10,

		Xor = 11,
		Add = 12,
		Saturate = 13,

		Multiply = 14,
		Screen = 15,
		Overlay = 16,
		Darken = 17,
		Lighten = 18,
		ColorDodge = 19,
		ColorBurn = 20,
		HardLight = 21,
		SoftLight = 22,
		Difference = 23,
		Exclusion = 24,
		HslHue = 25,
		HslSaturation = 26,
		HslColor = 27,
		HslLuminosity = 28,
	}
}
