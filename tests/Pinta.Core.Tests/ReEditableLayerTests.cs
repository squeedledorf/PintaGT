using System.Linq;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ReEditableLayerTests
{
	// Live text and shapes must look as they will once committed onto the layer (veterans round 3, F16).
	[Test]
	public void LiveDrawing_UsesParentOpacityAndBlendMode ()
	{
		UserLayer layer = new (CairoExtensions.CreateImageSurface (Cairo.Format.Argb32, 4, 4)) {
			Opacity = 0.5,
			BlendMode = BlendMode.Multiply,
		};
		_ = layer.TextLayer.Layer; // Sets up the text layer.

		Layer live = layer.GetLayersToPaint ().Last ();
		Assert.That (live.Surface, Is.SameAs (layer.TextLayer.Layer.Surface));
		Assert.That (live.Opacity, Is.EqualTo (0.5));
		Assert.That (live.BlendMode, Is.EqualTo (BlendMode.Multiply));
	}

	[Test]
	public void LiveDrawing_ToolBlendModeWinsOverNormalLayer ()
	{
		UserLayer layer = new (CairoExtensions.CreateImageSurface (Cairo.Format.Argb32, 4, 4));
		layer.TextLayer.Layer.BlendMode = BlendMode.Screen;

		Layer live = layer.GetLayersToPaint ().Last ();
		Assert.That (live.BlendMode, Is.EqualTo (BlendMode.Screen));
		Assert.That (layer.TextLayer.Layer.BlendMode, Is.EqualTo (BlendMode.Screen));
	}
}
