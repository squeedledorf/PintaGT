using System.Collections.Generic;

namespace Pinta.Core;

/// <summary>
/// Geometry for the image list (the strip of open-image thumbnails).
/// </summary>
public static class ImageListLayout
{
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
