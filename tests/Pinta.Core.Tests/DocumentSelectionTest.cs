using System.Collections.Generic;
using ClipperLib;
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

	// The ellipse's extremes must land exactly on its rectangle, or Crop to Selection loses a row.
	[TestCase (100, 100, 300, 300)]
	[TestCase (0, 0, 800, 650)]
	[TestCase (13, 7, 301, 99)]
	public void EllipseSelection_BoundsMatchRectangle (int x, int y, int width, int height)
	{
		DocumentSelection selection = new ();
		selection.CreateEllipseSelection (new RectangleD (x, y, width, height));

		Assert.That (selection.GetBounds ().ToInt (), Is.EqualTo (new RectangleI (x, y, width, height)));
	}

	// Edit > Copy/Paste Selection use Paint.NET's clipboard JSON (getpaint.net/doc/latest/EditMenu.html).
	[Test]
	public void PolygonListJson_ParsesPaintNetExample ()
	{
		string pdn = """
			{
			  "polygonList": [
			    "3,4,9,4,9,19,3,19,3,4"
			  ]
			}
			""";
		List<List<IntPoint>>? polygons = DocumentSelection.ParsePolygonListJson (pdn);
		Assert.That (polygons, Is.Not.Null);
		Assert.That (polygons!, Has.Count.EqualTo (1));
		Assert.That (polygons![0], Has.Count.EqualTo (5));
		Assert.That (polygons![0][2], Is.EqualTo (new IntPoint (9, 19)));
	}

	[Test]
	public void PolygonListJson_RoundTrips ()
	{
		List<List<IntPoint>> polygons = [
			[new (3, 4), new (9, 4), new (9, 19), new (3, 19)],
			[new (20, 20), new (30, 20), new (25, 28)],
		];
		string json = DocumentSelection.ToPolygonListJson (polygons);
		Assert.That (json, Does.Contain ("\"3,4,9,4,9,19,3,19,3,4\""));

		List<List<IntPoint>>? back = DocumentSelection.ParsePolygonListJson (json);
		Assert.That (back, Is.Not.Null);
		Assert.That (back!, Has.Count.EqualTo (2));
		Assert.That (back![1], Is.EqualTo (new List<IntPoint> { new (20, 20), new (30, 20), new (25, 28), new (20, 20) }));
	}

	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("hello")]
	[TestCase ("[1,2]")]
	[TestCase ("{\"polygonList\": []}")]
	[TestCase ("{\"polygonList\": [\"1,2,3\"]}")]
	[TestCase ("{\"polygonList\": [\"a,b,c,d,e,f\"]}")]
	[TestCase ("{\"polygonList\": [5]}")]
	public void PolygonListJson_RejectsOtherText (string? text)
		=> Assert.That (DocumentSelection.ParsePolygonListJson (text), Is.Null);
}
