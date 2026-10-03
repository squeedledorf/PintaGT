using System;
using System.IO;
using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
internal sealed class RecentFileManagerTest
{
	[Test]
	public void DefaultDialogDirectory_FallsBackToHomeWithoutPictures ()
	{
		string? old_home = Environment.GetEnvironmentVariable ("HOME");
		string? old_config = Environment.GetEnvironmentVariable ("XDG_CONFIG_HOME");
		string home = Directory.CreateTempSubdirectory ("pinta-home").FullName;
		try {
			Environment.SetEnvironmentVariable ("HOME", home);
			Environment.SetEnvironmentVariable ("XDG_CONFIG_HOME", Path.Combine (home, ".config"));

			Assert.That (new RecentFileManager ().DefaultDialogDirectory?.GetPath (), Is.EqualTo (home));

			string pictures = Path.Combine (home, "Pictures");
			Directory.CreateDirectory (pictures);
			Assert.That (new RecentFileManager ().DefaultDialogDirectory?.GetPath (), Is.EqualTo (pictures));
		} finally {
			Environment.SetEnvironmentVariable ("HOME", old_home);
			Environment.SetEnvironmentVariable ("XDG_CONFIG_HOME", old_config);
			Directory.Delete (home, true);
		}
	}
}
