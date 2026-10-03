using System;
using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Geometry for the image list (the strip of open-image thumbnails).
/// </summary>
public static class ImageListLayout
{
	/// <summary>
	/// The width of a thumbnail <paramref name="height"/> pixels tall that keeps the image's aspect ratio,
	/// clamped so that very wide or very tall images still get a usable thumbnail.
	/// </summary>
	public static int ThumbnailWidth (Size imageSize, int height, int minWidth, int maxWidth)
	{
		if (imageSize.Width <= 0 || imageSize.Height <= 0)
			return height;

		int width = (int) Math.Round (height * (double) imageSize.Width / imageSize.Height);
		return Math.Clamp (width, minWidth, maxWidth);
	}

	/// <summary>
	/// The index a dragged thumbnail moves to when dropped at <paramref name="x"/>, given the
	/// horizontal centres of all thumbnails (including the dragged one, at <paramref name="draggedIndex"/>).
	/// The result is the dragged item's index after it is removed and reinserted.
	/// </summary>
	public static int DropIndex (IReadOnlyList<double> centers, int draggedIndex, double x)
	{
		int index = 0;
		for (int i = 0; i < centers.Count; i++) {
			if (i != draggedIndex && centers[i] < x)
				index++;
		}
		return index;
	}
}
