using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class FloatingPanelLayoutTests
{
	private static readonly Size area = new (1000, 600);

	[TestCase (50, 50, 50, 50)] // free placement is kept
	[TestCase (6, 50, 0, 50)] // near the left edge snaps to it
	[TestCase (-40, -40, 0, 0)] // dragged out of the area is clamped
	[TestCase (795, 395, 800, 400)] // near the bottom-right corner snaps to both edges
	[TestCase (2000, 2000, 800, 400)]
	public void SnapsToAreaEdges (double x, double y, double expectedX, double expectedY)
	{
		PointD p = FloatingPanelLayout.Snap (new RectangleD (x, y, 200, 200), area, []);
		Assert.That (p, Is.EqualTo (new PointD (expectedX, expectedY)));
	}

	[Test]
	public void SnapsToNeighbouringPanel ()
	{
		RectangleD other = new (500, 100, 100, 100);

		// Beside the other panel: its left edge meets the other's right edge.
		Assert.That (FloatingPanelLayout.Snap (new RectangleD (606, 120, 50, 50), area, [other]).X, Is.EqualTo (600));
		// Far below it (no overlap vertically): no sideways snap.
		Assert.That (FloatingPanelLayout.Snap (new RectangleD (606, 400, 50, 50), area, [other]).X, Is.EqualTo (606));
		// Just under it: top meets the other's bottom.
		Assert.That (FloatingPanelLayout.Snap (new RectangleD (520, 207, 50, 50), area, [other]).Y, Is.EqualTo (200));
	}

	[Test]
	public void AnchorsToNearestEdgesAndRoundTrips ()
	{
		Size panel = new (200, 100);
		PanelAnchor anchor = FloatingPanelLayout.ToAnchor (new RectangleD (790, 20, 200, 100), area);
		Assert.That (anchor, Is.EqualTo (new PanelAnchor (Right: true, Bottom: false, OffsetX: 10, OffsetY: 20)));

		// A wider area keeps the distance to the right edge.
		Assert.That (FloatingPanelLayout.ToPosition (anchor, panel, new Size (1200, 600)), Is.EqualTo (new PointD (990, 20)));
	}

	[Test]
	public void ClampKeepsPanelInsideSmallerArea ()
	{
		PanelAnchor anchor = new (Right: false, Bottom: true, OffsetX: 700, OffsetY: 500);
		PanelAnchor clamped = FloatingPanelLayout.Clamp (anchor, new Size (200, 100), new Size (600, 400));
		Assert.That (clamped, Is.EqualTo (anchor with { OffsetX = 400, OffsetY = 300 }));

		// A panel larger than the area sits at the anchored edge.
		Assert.That (FloatingPanelLayout.Clamp (anchor, new Size (900, 900), new Size (600, 400)), Is.EqualTo (anchor with { OffsetX = 0, OffsetY = 0 }));
	}
}
