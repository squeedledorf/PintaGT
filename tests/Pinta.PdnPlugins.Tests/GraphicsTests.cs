using System.Drawing;
using System.Drawing.Drawing2D;
using NUnit.Framework;
using PaintDotNet;

namespace Pinta.PdnPlugins.Tests;

/// <summary>The GDI+ subset plugins draw with, implemented on Cairo.</summary>
[TestFixture]
internal sealed class GraphicsTests
{
	[OneTimeSetUp]
	public void InitCairo ()
	{
		Cairo.Module.Initialize ();
		PangoCairo.Module.Initialize ();
	}

	[Test]
	public void FillRectangleCoversExactlyTheRectangle ()
	{
		using Bitmap b = new (100, 80);
		using (Graphics g = Graphics.FromImage (b))
			g.FillRectangle (new SolidBrush (Color.FromArgb (255, 10, 20, 30)), new Rectangle (10, 20, 50, 40));
		Assert.That (b.GetPixel (10, 20).ToArgb (), Is.EqualTo (Color.FromArgb (255, 10, 20, 30).ToArgb ()));
		Assert.That (b.GetPixel (59, 59).ToArgb (), Is.EqualTo (Color.FromArgb (255, 10, 20, 30).ToArgb ()));
		Assert.That (b.GetPixel (60, 59).A, Is.EqualTo (0));
		Assert.That (b.GetPixel (9, 20).A, Is.EqualTo (0));
	}

	[Test]
	public void HatchBrushAndPenWithBrushPaint ()
	{
		using Bitmap b = new (64, 64);
		using (Graphics g = Graphics.FromImage (b)) {
			g.FillRectangle (new HatchBrush (HatchStyle.Percent50, Color.Black, Color.White), new Rectangle (0, 0, 64, 64));
			g.DrawLine (new Pen (new SolidBrush (Color.Red), 3), new Point (0, 32), new Point (63, 32));
		}
		Assert.That (b.GetPixel (40, 40).A, Is.EqualTo (255), "hatch fills the whole rectangle, not one tile");
		Assert.That (b.GetPixel (30, 32).R, Is.EqualTo (255));
		Assert.That (b.GetPixel (30, 32).G, Is.EqualTo (0));
	}

	[Test]
	public void DrawingOnAnAliasedSurfaceKeepsStraightAlpha ()
	{
		using Surface s = new (40, 40);
		s.Clear (ColorBgra.FromBgra (0, 0, 255, 128)); // half-transparent red
		using (RenderArgs args = new (s))
			args.Graphics.FillRectangle (new SolidBrush (Color.FromArgb (255, 0, 0, 255)), 0, 0, 10, 10);
		Assert.That (s[5, 5], Is.EqualTo (ColorBgra.FromBgra (255, 0, 0, 255)));
		ColorBgra untouched = s[30, 30];
		Assert.That (untouched.R, Is.InRange (254, 255));
		Assert.That (untouched.A, Is.EqualTo (128));
	}

	[Test]
	public void TextIsDrawnAndMeasured ()
	{
		using Bitmap b = new (200, 60);
		using Graphics g = Graphics.FromImage (b);
		using Font font = new ("Sans", 20);
		SizeF size = g.MeasureString ("Hello", font);
		Assert.That (size.Width, Is.GreaterThan (40));
		Assert.That (size.Height, Is.GreaterThan (20));
		g.DrawString ("Hello", font, Brushes.Black, 5, 5);
		int inked = 0;
		for (int y = 0; y < 60; y++)
			for (int x = 0; x < 200; x++)
				if (b.GetPixel (x, y).A > 0) inked++;
		Assert.That (inked, Is.GreaterThan (100));
	}

	[Test]
	public void TransformsAndClipApply ()
	{
		using Bitmap b = new (100, 100);
		using (Graphics g = Graphics.FromImage (b)) {
			g.Clip = new Region (new Rectangle (0, 0, 50, 100));
			g.TranslateTransform (40, 0);
			g.FillRectangle (Brushes.Black, 0, 0, 20, 20);
		}
		Assert.That (b.GetPixel (45, 10).A, Is.EqualTo (255));
		Assert.That (b.GetPixel (55, 10).A, Is.EqualTo (0), "clipped");
		Assert.That (b.GetPixel (35, 10).A, Is.EqualTo (0), "translated");
	}
}
