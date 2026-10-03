using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinta.Core;

/// <summary>
/// Where a floating panel sits: the corner it is anchored to and its distance from those two edges.
/// Anchoring to the nearest edges keeps that distance when the area resizes, as Paint.NET does.
/// </summary>
public readonly record struct PanelAnchor (bool Right, bool Bottom, int OffsetX, int OffsetY);

/// <summary>
/// Geometry for the floating Tools/History/Layers/Colors panels over the canvas area.
/// </summary>
public static class FloatingPanelLayout
{
	public const double SNAP_DISTANCE = 10;

	/// <summary>
	/// Keep a panel inside the area, then snap it to the area edges or to the edges of a neighbouring panel
	/// when it is within <see cref="SNAP_DISTANCE"/> of them. Returns the panel's new top-left corner.
	/// </summary>
	public static PointD Snap (RectangleD panel, Size area, IEnumerable<RectangleD> others)
	{
		double x = Clamp (panel.X, panel.Width, area.Width);
		double y = Clamp (panel.Y, panel.Height, area.Height);

		List<double> xs = [0, area.Width - panel.Width];
		List<double> ys = [0, area.Height - panel.Height];

		foreach (RectangleD other in others) {
			// Only panels beside each other snap: they must (nearly) overlap on the other axis.
			if (Overlaps (y, panel.Height, other.Y, other.Height))
				xs.AddRange ([other.X - panel.Width, other.X + other.Width, other.X, other.X + other.Width - panel.Width]);

			if (Overlaps (x, panel.Width, other.X, other.Width))
				ys.AddRange ([other.Y - panel.Height, other.Y + other.Height, other.Y, other.Y + other.Height - panel.Height]);
		}

		return new (
			Clamp (Nearest (x, xs), panel.Width, area.Width),
			Clamp (Nearest (y, ys), panel.Height, area.Height));
	}

	/// <summary>
	/// The anchor for a panel at <paramref name="panel"/>: the nearest horizontal and vertical edges of the area.
	/// </summary>
	public static PanelAnchor ToAnchor (RectangleD panel, Size area)
	{
		bool right = panel.X + panel.Width / 2 > area.Width / 2.0;
		bool bottom = panel.Y + panel.Height / 2 > area.Height / 2.0;
		double offsetX = right ? area.Width - panel.X - panel.Width : panel.X;
		double offsetY = bottom ? area.Height - panel.Y - panel.Height : panel.Y;
		return new (right, bottom, Math.Max (0, (int) Math.Round (offsetX)), Math.Max (0, (int) Math.Round (offsetY)));
	}

	/// <summary>
	/// Shrink the anchor's offsets so a panel of <paramref name="panel"/> size stays inside the area.
	/// </summary>
	public static PanelAnchor Clamp (PanelAnchor anchor, Size panel, Size area)
		=> anchor with {
			OffsetX = Math.Max (0, Math.Min (anchor.OffsetX, area.Width - panel.Width)),
			OffsetY = Math.Max (0, Math.Min (anchor.OffsetY, area.Height - panel.Height)),
		};

	/// <summary>
	/// The top-left corner of a panel of <paramref name="panel"/> size placed by <paramref name="anchor"/>.
	/// </summary>
	public static PointD ToPosition (PanelAnchor anchor, Size panel, Size area)
		=> new (
			anchor.Right ? area.Width - panel.Width - anchor.OffsetX : anchor.OffsetX,
			anchor.Bottom ? area.Height - panel.Height - anchor.OffsetY : anchor.OffsetY);

	private static double Clamp (double position, double size, double area)
		=> Math.Max (0, Math.Min (position, area - size));

	private static bool Overlaps (double a, double aLength, double b, double bLength)
		=> a < b + bLength + SNAP_DISTANCE && b < a + aLength + SNAP_DISTANCE;

	private static double Nearest (double position, List<double> candidates)
	{
		double best = candidates.MinBy (c => Math.Abs (c - position));
		return Math.Abs (best - position) <= SNAP_DISTANCE ? best : position;
	}
}
