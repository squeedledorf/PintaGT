using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class DocumentSelectionTest
{
	// Copy, Cut and Copy Merged stay enabled with no selection and act on the whole layer, as in Paint.NET.
	// That relies on a deselected (hidden) selection still covering the whole canvas, as Document.ResetSelectionPaths leaves it.
	[Test]
	public void HiddenSelection_CoversWholeCanvas ()
	{
		DocumentSelection selection = new ();
		selection.CreateRectangleSelection (new RectangleD (0, 0, 640, 480));
		selection.Visible = false;

		Assert.That (selection.GetBounds ().ToInt (), Is.EqualTo (new RectangleI (0, 0, 640, 480)));
	}
}
