using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class SelectionModeTest
{
	// Paint.NET modifiers: Left Ctrl = Add, Left Alt = Subtract, Right Ctrl = Invert, Right Alt = Intersect.
	// A plain click with either button uses the toolbar mode.
	[TestCase (MouseButton.Left, false, false, CombineMode.Replace)]
	[TestCase (MouseButton.Right, false, false, CombineMode.Replace)]
	[TestCase (MouseButton.Left, true, false, CombineMode.Union)]
	[TestCase (MouseButton.Left, false, true, CombineMode.Exclude)]
	[TestCase (MouseButton.Right, true, false, CombineMode.Xor)]
	[TestCase (MouseButton.Right, false, true, CombineMode.Intersect)]
	public void DetermineCombineMode_MatchesPaintDotNet (MouseButton button, bool ctrl, bool alt, CombineMode expected)
	{
		Assert.That (SelectionModeHandler.DetermineCombineMode (button, ctrl, alt, CombineMode.Replace), Is.EqualTo (expected));
	}
}
