using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class AsyncEffectRendererTests
{
	/// <summary>Never returns from Render unless told to; records CancelRender calls.</summary>
	private sealed class StuckEffect (bool canAbandon) : BaseEffect
	{
		public readonly ManualResetEventSlim Release = new ();
		public readonly ManualResetEventSlim Started = new ();
		public int Cancels;

		public override string Name => "Stuck";
		public override bool IsTileable => false;
		public override bool CanAbandonCancelledRender => canAbandon;
		public override void CancelRender () => Interlocked.Increment (ref Cancels);
		public override BaseEffect Clone () => this; // keep one instance to observe it

		public override void Render (Cairo.ImageSurface src, Cairo.ImageSurface dst, ReadOnlySpan<RectangleI> rois)
		{
			Started.Set ();
			Release.Wait ();
		}
	}

	private static RenderHandle Start (BaseEffect effect)
	{
		Cairo.Module.Initialize ();
		Cairo.ImageSurface src = new (Cairo.Format.Argb32, 8, 8);
		Cairo.ImageSurface dst = new (Cairo.Format.Argb32, 8, 8);
		return AsyncEffectRenderer.Start (new AsyncEffectRenderer.Settings (2, new RectangleI (0, 0, 8, 8), false), effect, src, dst);
	}

	[Test]
	public async Task CancelReachesTheEffectAndAStuckRenderIsAbandoned ()
	{
		StuckEffect effect = new (canAbandon: true);
		RenderHandle render = Start (effect);
		Assert.That (effect.Started.Wait (5000), Is.True);

		render.Cancel ();
		Assert.That (effect.Cancels, Is.EqualTo (1));

		Task finished = await Task.WhenAny (render.Completion, Task.Delay (10000));
		Assert.That (finished, Is.SameAs (render.Completion), "a stuck render that may be abandoned must not keep Pinta waiting");
		Assert.That (render.Completion.Result.WasCanceled, Is.True);
		effect.Release.Set ();
	}

	[Test]
	public async Task OrdinaryEffectsAreWaitedFor ()
	{
		StuckEffect effect = new (canAbandon: false);
		RenderHandle render = Start (effect);
		Assert.That (effect.Started.Wait (5000), Is.True);

		render.Cancel ();
		Task finished = await Task.WhenAny (render.Completion, Task.Delay (3000));
		Assert.That (finished, Is.Not.SameAs (render.Completion));

		effect.Release.Set ();
		await render.Completion;
	}
}
