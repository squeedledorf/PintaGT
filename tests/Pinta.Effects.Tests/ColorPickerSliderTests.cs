using System;
using System.Threading.Tasks;
using Cairo;
using NUnit.Framework;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.Effects.Tests;

[TestFixture]
internal sealed class ColorPickerSliderTests
{
	static ColorPickerSliderTests () => Cairo.Module.Initialize ();

	// The compact Colors window gives each slider row about 20 px; heights near or below
	// twice the padding used to make the checkerboard step 0, an endless loop on the UI thread.
	// Runs on a worker so a regression fails the test instead of hanging the run.
	[Test]
	public void DrawGradientReturnsForShortHeights ()
	{
		Task draw = Task.Run (() => {
			using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, 200, 30);
			for (int height = 0; height <= 24; height++)
				foreach (int width in new[] { 0, 10, 28, 200 }) {
					using Context context = new (surface);
					ColorPickerSlider.DrawGradient (context, width, height, new Color (0.2, 0.4, 0.6, 0.5), ColorPickerSlider.Component.Alpha);
				}
		});
		Assert.That (draw.Wait (TimeSpan.FromSeconds (10)), Is.True);
	}
}
