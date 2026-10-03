using System;

namespace Pinta.Core;

/// <summary>
/// Geometry of a hue/saturation colour wheel laid out like Paint.NET's:
/// red at 3 o'clock, hue increasing clockwise on screen (yellow down and to the right, magenta up and to the right), saturation 0 at the centre and 1 on the rim.
/// Offsets are in screen coordinates (y down) relative to the wheel's centre.
/// </summary>
public static class ColorWheel
{
	public const double SNAP_DEGREES = 15;

	public static PointD HsvToOffset (HsvColor hsv, double radius)
	{
		double rad = hsv.Hue * Math.PI / 180;
		return new (
			Math.Cos (rad) * hsv.Sat * radius,
			Math.Sin (rad) * hsv.Sat * radius);
	}

	/// <param name="keepSat">Ctrl: only the hue changes (stay on the current radius).</param>
	/// <param name="keepHue">Alt: only the saturation changes (stay on the current spoke).</param>
	/// <param name="snapHue">Shift: the hue snaps to <see cref="SNAP_DEGREES"/> spokes.</param>
	public static HsvColor OffsetToHsv (
		PointD offset,
		double radius,
		HsvColor current,
		bool keepSat = false,
		bool keepHue = false,
		bool snapHue = false)
	{
		double hue = Math.Atan2 (offset.Y, offset.X) * 180 / Math.PI;
		if (snapHue)
			hue = Math.Round (hue / SNAP_DEGREES) * SNAP_DEGREES;
		hue = (hue % 360 + 360) % 360;

		double sat = Math.Sqrt (offset.X * offset.X + offset.Y * offset.Y) / radius;

		if (keepHue) {
			// Project the pointer onto the current colour's spoke.
			PointD spoke = HsvToOffset (current with { Sat = 1 }, 1);
			sat = (offset.X * spoke.X + offset.Y * spoke.Y) / radius;
			hue = current.Hue;
		}

		if (keepSat)
			sat = current.Sat;

		// The wheel is drawn at full brightness, so picking from it while the colour is black
		// (the default primary) would otherwise change nothing visible.
		double val = current.Val == 0 ? 1 : current.Val;

		return new (hue, Math.Clamp (sat, 0, 1), val);
	}
}
