using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class IconsTest
{
	// Colour (non-symbolic) icon names are never provided by the system theme,
	// so each one must be shipped in Pinta's own icons/hicolor folder.
	[Test]
	public void ColourIconsAreShipped ()
	{
		string assemblyDir = Path.GetDirectoryName (typeof (IconsTest).Assembly.Location)!;
		var shipped = Directory
			.EnumerateFiles (Path.Combine (assemblyDir, "icons", "hicolor"), "*", SearchOption.AllDirectories)
			.Select (Path.GetFileNameWithoutExtension)
			.ToHashSet ();

		var missing = new[] { typeof (Resources.StandardIcons), typeof (Resources.Icons) }
			.SelectMany (t => t.GetFields (BindingFlags.Public | BindingFlags.Static))
			.Select (f => (string) f.GetRawConstantValue ()!)
			.Where (name => !name.EndsWith ("-symbolic") && !shipped.Contains (name))
			.ToList ();

		Assert.That (missing, Is.Empty);
	}
}
