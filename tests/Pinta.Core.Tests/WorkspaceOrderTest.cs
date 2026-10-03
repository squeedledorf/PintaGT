using System.Collections.Generic;
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

}
