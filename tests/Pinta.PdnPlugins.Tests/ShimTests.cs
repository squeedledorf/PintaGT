using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace Pinta.PdnPlugins.Tests;

[TestFixture]
internal sealed class ShimTests
{
	[Test]
	public void SurfaceCopyIsClipped ()
	{
		using Surface a = new (4, 4);
		using Surface b = new (4, 4);
		a.Clear (ColorBgra.Red);
		b.CopySurface (a, new Point (2, 2), new Rectangle (0, 0, 4, 4));
		Assert.That (b[1, 1], Is.EqualTo (default (ColorBgra)));
		Assert.That (b[2, 2], Is.EqualTo (ColorBgra.Red));
		Assert.That (b[3, 3], Is.EqualTo (ColorBgra.Red));
		Assert.Throws<ArgumentOutOfRangeException> (() => _ = b[4, 0]);
	}

	[Test]
	public void BilinearSampleBlendsByAlpha ()
	{
		using Surface s = new (2, 1);
		s[0, 0] = ColorBgra.FromBgra (0, 0, 255, 255);
		s[1, 0] = ColorBgra.FromBgra (255, 0, 0, 0); // transparent blue must not tint the result
		ColorBgra mid = s.GetBilinearSampleClamped (0.5f, 0f);
		Assert.That (mid.R, Is.EqualTo (255));
		Assert.That (mid.B, Is.EqualTo (0));
		Assert.That (mid.A, Is.InRange (126, 129));
	}

	[Test]
	public void PropertyCollectionRulesAndTokens ()
	{
		PropertyCollection props = new (
			[new BooleanProperty ("Enable", false), new Int32Property ("Amount", 5, 0, 10)],
			[new ReadOnlyBoundToBooleanRule ("Amount", "Enable", true)]);
		Assert.That (props["Amount"]!.ReadOnly, Is.True);
		props["Enable"]!.Value = true;
		Assert.That (props["Amount"]!.ReadOnly, Is.False);
		props["Amount"]!.Value = 50; // clamped
		Assert.That (props["Amount"]!.Value, Is.EqualTo (10));

		PropertyBasedEffectConfigToken token = new (props);
		PropertyBasedEffectConfigToken copy = (PropertyBasedEffectConfigToken) token.Clone ();
		props["Amount"]!.Value = 3;
		Assert.That (copy.GetProperty<Int32Property> ("Amount")!.Value, Is.EqualTo (10));
		Assert.That (copy.GetProperty<BooleanProperty> ("Enable")!.Value, Is.True);
	}

	private enum Choice { First, Second }

	[Test]
	public void DefaultConfigUiPicksControlTypes ()
	{
		PropertyCollection props = new ([
			new DoubleProperty ("Angle", 45, -180, 180),
			StaticListChoiceProperty.CreateForEnum ("Choice", Choice.Second, false),
			new PaintDotNet.PropertySystem.DoubleVectorProperty ("Pan", Pair.Create (0.0, 0.0), Pair.Create (-1.0, -1.0), Pair.Create (1.0, 1.0)),
		]);
		ControlInfo ui = ControlInfo.CreateDefaultConfigUI (props);
		Assert.That (ui.SetPropertyControlType ("Angle", PropertyControlType.AngleChooser), Is.True);
		Assert.That (ui.SetPropertyControlValue ("Choice", ControlInfoPropertyNames.DisplayName, "Pick"), Is.True);
		ui.FindControlForPropertyName ("Choice")!.SetValueDisplayName (Choice.First, "Number one");

		Assert.That (ui.FindControlForPropertyName ("Angle")!.ControlType.Value, Is.EqualTo (PropertyControlType.AngleChooser));
		Assert.That (ui.FindControlForPropertyName ("Choice")!.ControlType.Value, Is.EqualTo (PropertyControlType.DropDown));
		Assert.That (ui.FindControlForPropertyName ("Pan")!.ControlType.Value, Is.EqualTo (PropertyControlType.PanAndSlider));
		Assert.That (ui.FindControlForPropertyName ("Choice")!.GetValueDisplayName (Choice.First), Is.EqualTo ("Number one"));
		Assert.That (props["Choice"]!.Value, Is.EqualTo (Choice.Second));
	}

	/// <summary>A facade named like a Paint.NET assembly resolves its types to the shim.</summary>
	[Test]
	public void FacadeForwardsPaintDotNetNamesToTheShim ()
	{
		PluginLoadContext context = new (Path.GetTempPath ());
		Assembly core = context.LoadFromAssemblyName (new AssemblyName ("PaintDotNet.Core, Version=4.310.8103.32785"));
		Assembly drawing = context.LoadFromAssemblyName (new AssemblyName ("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"));
		Assert.That (core.GetName ().Name, Is.EqualTo ("PaintDotNet.Core"));
		Assert.That (core.GetType ("PaintDotNet.Surface"), Is.SameAs (typeof (Surface)));
		Assert.That (core.GetType ("PaintDotNet.UnaryPixelOps+Desaturate"), Is.SameAs (typeof (UnaryPixelOps.Desaturate)));
		Assert.That (drawing.GetType ("System.Drawing.Bitmap"), Is.SameAs (typeof (Bitmap)));
		Assert.That (drawing.GetType ("System.Drawing.Rectangle"), Is.SameAs (typeof (Rectangle)));
		Assembly forms = context.LoadFromAssemblyName (new AssemblyName ("System.Windows.Forms, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"));
		Assert.That (forms.GetType ("System.Windows.Forms.MessageBox"), Is.Not.Null);
		context.Unload ();
	}
}
