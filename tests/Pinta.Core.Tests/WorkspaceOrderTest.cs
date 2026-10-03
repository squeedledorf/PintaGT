using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class WorkspaceOrderTest
{
	// Moving item 'from' to 'to' in a list of 5 must leave every other index pointing at the same item.
	[Test]
	public void MovedIndex_FollowsTheSameItem ()
	{
		for (int from = 0; from < 5; from++) {
			for (int to = 0; to < 5; to++) {
				List<int> items = [0, 1, 2, 3, 4];
				int moved = items[from];
				items.RemoveAt (from);
				items.Insert (to, moved);

				for (int index = 0; index < 5; index++)
					Assert.That (items[WorkspaceManager.MovedIndex (index, from, to)], Is.EqualTo (index));
			}
		}
	}

	[Test]
	public void ToolDropdown_ListsSelectionToolsBeforeMoveAndViewTools ()
	{
		// Toolbox priorities: RectSelect 1, MoveSelected 3, Lasso 5, MoveSelection 7,
		// EllipseSelect 9, Zoom 11, MagicWand 13, Pan 15, PaintBucket 17, Gradient 19.
		int[] priorities = [1, 3, 5, 7, 9, 11, 13, 15, 17, 19];
		int[] order = [.. priorities.OrderBy (ToolManager.DropdownOrder)];
		Assert.That (order, Is.EqualTo (new[] { 1, 5, 9, 13, 3, 7, 11, 15, 17, 19 }));
	}
}
