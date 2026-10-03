using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class ZoomFitTests
{
	[TestCase (222, 133, 1000, 700, 980.0 / 222)] // width-bound selection fills the view, whatever the image size
	[TestCase (100, 400, 1000, 700, 680.0 / 400)] // height-bound
	[TestCase (3000, 2000, 1000, 700, 980.0 / 3000)]
	[TestCase (10, 10, 5, 5, 1.0 / 10)] // a window smaller than the margin still gives a positive scale
	public void FitsInsideWindowMinusMargin (double width, double height, int windowWidth, int windowHeight, double expected)
	{
		double scale = DocumentWorkspace.GetFitScale (width, height, new Size (windowWidth, windowHeight), 20);
		Assert.That (scale, Is.EqualTo (expected).Within (1e-9));
	}
}
