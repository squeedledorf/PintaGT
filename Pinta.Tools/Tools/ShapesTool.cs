using System;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Paint.NET's single Shapes tool, which replaced the Rectangle, Rounded Rectangle, Ellipse and
/// Freeform Shape tools: one tool with a dropdown of preset shapes.
/// </summary>
public sealed class ShapesTool : ShapeTool
{
	public ShapesTool (IServiceProvider services) : base (services)
	{
		DefaultCursor = Gdk.Cursor.NewFromTexture (Resources.GetIcon ("Cursor.Rectangle.png"), 9, 18, null);
		EditEngine = new ShapesEditEngine (services, this);
	}

	public override string Name => Translations.GetString ("Shapes");
	public override string Icon => Pinta.Resources.Icons.ToolShapes;
	public override Gdk.Cursor DefaultCursor { get; }
	public override int Priority => 37;

	public override BaseEditEngine EditEngine { get; }

	public override string StatusBarText => Translations.GetString (
		"Left click and drag to draw a shape with the primary color, right click and drag to use the secondary color. " +
		"Drag a nub to resize, drag inside to move, drag just outside or right-drag to rotate. " +
		"Press A to change the shape, Enter to finish.");
}
