using System.Globalization;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class ZoomPercentTest
{
	[TestCase (36, "3600%")]
	[TestCase (24, "2400%")]
	[TestCase (1, "100%")]
	[TestCase (0.66, "66%")]
	public void ToPercent_HasNoGroupSeparator (double scale, string expected)
	{
		CultureInfo old = CultureInfo.CurrentCulture;
		try {
			CultureInfo.CurrentCulture = new CultureInfo ("en-US");
			Assert.That (ViewActions.ToPercent (scale), Is.EqualTo (expected));
			Assert.That (ViewActions.TryParsePercent (expected, out double percent), Is.True);
			Assert.That (percent, Is.EqualTo (scale * 100).Within (0.5));
		} finally {
			CultureInfo.CurrentCulture = old;
		}
	}
}
