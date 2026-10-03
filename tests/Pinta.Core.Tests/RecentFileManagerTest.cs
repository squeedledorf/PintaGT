using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

	[Test]
	public void RecentFiles_NewestFirstNoDuplicatesTenMax ()
	{
		FakeSettings settings = new ();
		RecentFileManager recent = new (settings);

		for (int i = 0; i < 12; i++)
			recent.Remember ($"file:///img{i}.png");
		recent.Remember ("file:///img5.png");

		Assert.That (recent.RecentFiles, Has.Count.EqualTo (RecentFileManager.MaxRecentFiles));
		Assert.That (recent.RecentFiles[0], Is.EqualTo ("file:///img5.png"));
		Assert.That (recent.RecentFiles.Count (u => u == "file:///img5.png"), Is.EqualTo (1));
		Assert.That (recent.RecentFiles, Does.Not.Contain ("file:///img0.png"));

		// The list survives a restart through the settings.
		Assert.That (new RecentFileManager (settings).RecentFiles, Is.EqualTo (recent.RecentFiles));

		recent.Forget ("file:///img5.png");
		Assert.That (recent.RecentFiles, Does.Not.Contain ("file:///img5.png"));

		recent.ClearRecentFiles ();
		Assert.That (new RecentFileManager (settings).RecentFiles, Is.Empty);
	}

	private sealed class FakeSettings : ISettingsService
	{
		private readonly Dictionary<string, object> values = [];
		public T GetSetting<T> (string key, T defaultValue) => values.TryGetValue (key, out object? v) ? (T) v : defaultValue;
		public string GetUserSettingsDirectory () => Path.GetTempPath ();
		public void PutSetting (string key, object value) => values[key] = value;
		public event EventHandler? SaveSettingsBeforeQuit { add { } remove { } }
		public event EventHandler<SettingChangedEventArgs>? SettingChanged { add { } remove { } }
	}
}
