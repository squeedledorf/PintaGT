using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class GradientRepeatTests
{
	[TestCase (-0.5, GradientRepeatMode.NoRepeat, 0.0)]
	[TestCase (0.25, GradientRepeatMode.NoRepeat, 0.25)]
	[TestCase (1.75, GradientRepeatMode.NoRepeat, 1.0)]
	[TestCase (1.25, GradientRepeatMode.RepeatWrapped, 0.25)]
	[TestCase (-0.25, GradientRepeatMode.RepeatWrapped, 0.75)]
	[TestCase (1.25, GradientRepeatMode.RepeatReflected, 0.75)]
	[TestCase (2.25, GradientRepeatMode.RepeatReflected, 0.25)]
	[TestCase (-0.25, GradientRepeatMode.RepeatReflected, 0.25)]
	public void ApplyRepeat (double t, GradientRepeatMode mode, double expected)
		=> Assert.That (GradientRenderer.ApplyRepeat (t, mode), Is.EqualTo (expected).Within (1e-9));

	private static GradientRenderer Prepare (GradientRenderer renderer, GradientRepeatMode mode)
	{
		renderer.StartColor = ColorBgra.Black;
		renderer.EndColor = ColorBgra.White;
		renderer.StartPoint = new PointD (50, 50);
		renderer.EndPoint = new PointD (90, 50);
		renderer.RepeatMode = mode;
		renderer.BeforeRender ();
		return renderer;
	}

	[Test]
	public void Radial_Repeats_Beyond_The_End_Point ()
	{
		var op = new UserBlendOps.NormalBlendOp ();
		// 60 px from the start is 1.5 lengths out.
		Assert.That (Prepare (new GradientRenderers.Radial (false, op), GradientRepeatMode.NoRepeat).ComputeByteLerp (110, 50), Is.EqualTo (255));
		Assert.That (Prepare (new GradientRenderers.Radial (false, op), GradientRepeatMode.RepeatWrapped).ComputeByteLerp (110, 50), Is.EqualTo (127));
		Assert.That (Prepare (new GradientRenderers.Radial (false, op), GradientRepeatMode.RepeatReflected).ComputeByteLerp (110, 50), Is.EqualTo (127));
		Assert.That (Prepare (new GradientRenderers.Radial (false, op), GradientRepeatMode.RepeatReflected).ComputeByteLerp (120, 50), Is.EqualTo (63));
	}

	[Test]
	public void Spiral_Adds_A_Turn_To_The_Distance ()
	{
		var op = new UserBlendOps.NormalBlendOp ();
		GradientRenderer cw = Prepare (new GradientRenderers.Spiral (clockwise: true, false, op), GradientRepeatMode.NoRepeat);
		GradientRenderer ccw = Prepare (new GradientRenderers.Spiral (clockwise: false, false, op), GradientRepeatMode.NoRepeat);

		// On the start-end line a quarter length out: no turn, so a quarter of the way along.
		Assert.That (cw.ComputeByteLerp (60, 50), Is.EqualTo (63));
		Assert.That (ccw.ComputeByteLerp (60, 50), Is.EqualTo (63));

		// A quarter length straight down on screen: a quarter turn one way, three quarters the other.
		Assert.That (ccw.ComputeByteLerp (50, 60), Is.EqualTo (127));
		Assert.That (cw.ComputeByteLerp (50, 60), Is.EqualTo (255));

		// Wrapped, three quarters of a turn plus half a length comes round to a quarter of the way along.
		GradientRenderer wrapped = Prepare (new GradientRenderers.Spiral (clockwise: true, false, op), GradientRepeatMode.RepeatWrapped);
		Assert.That (wrapped.ComputeByteLerp (50, 70), Is.EqualTo (63));
	}
}
