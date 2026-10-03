using System.Globalization;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ZoomStepsTest
{
	// Paint.NET's documented steps (ViewMenu.html).
	[Test]
	public void ZoomIn_FollowsPaintNetSteps ()
	{
		double[] expected = [150, 200, 300, 400, 500, 600, 800, 1000, 1200, 1400, 1600, 2000, 2400, 2800, 3200, 4000, 4800, 5600, 6400];
		double zoom = 100;
		foreach (double next in expected) {
			int step = ViewActions.GetZoomStep (zoom, zoomIn: true);
			Assert.That (step, Is.GreaterThanOrEqualTo (0));
			zoom = ViewActions.ZoomLevels[step] * 100;
			Assert.That (zoom, Is.EqualTo (next).Within (0.001));
		}
		Assert.That (ViewActions.GetZoomStep (zoom, zoomIn: true), Is.EqualTo (-1));
		Assert.That (zoom, Is.EqualTo (ViewActions.MaxZoomPercent));
	}

	[Test]
	public void ZoomOut_FollowsPaintNetSteps ()
	{
		double[] expected = [67, 50, 33, 25, 20];
		double zoom = 100;
		foreach (double next in expected) {
			zoom = ViewActions.ZoomLevels[ViewActions.GetZoomStep (zoom, zoomIn: false)] * 100;
			Assert.That (zoom, Is.EqualTo (next).Within (0.001));
		}
	}

	[TestCase (175, true, 200)]
	[TestCase (175, false, 150)]
	[TestCase (45, true, 50)]
	[TestCase (45, false, 33)]
	[TestCase (6400, false, 5600)]
	public void OffStepZoom_GoesToTheNeighbouringStep (double zoom, bool zoomIn, double expected)
	{
		Assert.That (ViewActions.ZoomLevels[ViewActions.GetZoomStep (zoom, zoomIn)] * 100, Is.EqualTo (expected).Within (0.001));
	}

	[Test]
	public void ZoomOut_StopsAtTheSmallestStep ()
		=> Assert.That (ViewActions.GetZoomStep (ViewActions.MinZoomPercent, zoomIn: false), Is.EqualTo (-1));

	[TestCase (800, 0, "800")]
	[TestCase (-3.5, 0, "-4")]
	[TestCase (96, 1, "1.00")]
	[TestCase (800, 1, "8.33")]
	[TestCase (378, 2, "10.00")]
	public void FormatLength_UsesTheUnits (double pixels, int metric, string expected)
	{
		CultureInfo old = CultureInfo.CurrentCulture;
		try {
			CultureInfo.CurrentCulture = new CultureInfo ("en-US");
			Assert.That (ViewActions.FormatLength (pixels, metric), Is.EqualTo (expected));
		} finally {
			CultureInfo.CurrentCulture = old;
		}
	}
}
