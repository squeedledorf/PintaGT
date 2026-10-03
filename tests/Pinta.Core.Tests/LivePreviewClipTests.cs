using Cairo;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class LivePreviewClipTests
{
	// An adjustment's live preview stays inside an elliptical selection, as the final commit does.
	[Test]
	public void ShowRenderedArea_ClipsToEllipse ()
	{
		using ImageSurface shown = new (Format.Argb32, 100, 100);
		using ImageSurface original = new (Format.Argb32, 100, 100);
		using ImageSurface effect = new (Format.Argb32, 100, 100);
		using (Context g = new (effect)) {
			g.SetSourceRgb (1, 0, 0);
			g.Paint ();
		}

		DocumentSelection selection = new ();
		selection.CreateEllipseSelection (new RectangleD (0, 0, 100, 100));

		LivePreviewManager.ShowRenderedArea (shown, original, effect, selection, new RectangleI (0, 0, 100, 100));

		Assert.That (shown.GetColorBgra (new PointI (50, 50)).A, Is.EqualTo (255), "centre previews");
		Assert.That (shown.GetColorBgra (new PointI (2, 2)).A, Is.EqualTo (0), "bounding-box corner stays untouched");
	}

	// Only the newly rendered area is copied.
	[Test]
	public void ShowRenderedArea_OnlyTouchesArea ()
	{
		using ImageSurface shown = new (Format.Argb32, 100, 100);
		using ImageSurface original = new (Format.Argb32, 100, 100);
		using ImageSurface effect = new (Format.Argb32, 100, 100);
		using (Context g = new (effect)) {
			g.SetSourceRgb (1, 0, 0);
			g.Paint ();
		}

		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (new RectangleD (0, 0, 100, 100));

		LivePreviewManager.ShowRenderedArea (shown, original, effect, selection, new RectangleI (0, 0, 50, 100));

		Assert.That (shown.GetColorBgra (new PointI (25, 50)).A, Is.EqualTo (255));
		Assert.That (shown.GetColorBgra (new PointI (75, 50)).A, Is.EqualTo (0));
	}

	// A tile rendered twice (settings changed) leaves the antialiased edge as a single blend.
	[Test]
	public void ShowRenderedArea_RerenderDoesNotAccumulateEdge ()
	{
		using ImageSurface original = new (Format.Argb32, 10, 10);
		using ImageSurface shown = new (Format.Argb32, 10, 10);
		using ImageSurface effect = new (Format.Argb32, 10, 10);
		using (Context g = new (effect)) {
			g.SetSourceRgb (1, 0, 0);
			g.Paint ();
		}

		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (new RectangleD (0, 0, 4.5, 10)); // column 4 is half covered
		RectangleI all = new (0, 0, 10, 10);

		LivePreviewManager.ShowRenderedArea (shown, original, effect, selection, all);
		byte once = shown.GetColorBgra (new PointI (4, 5)).A;
		LivePreviewManager.ShowRenderedArea (shown, original, effect, selection, all);

		Assert.That (shown.GetColorBgra (new PointI (4, 5)).A, Is.EqualTo (once));
	}
}
