//
// HelpActions.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2010 Jonathan Pobst
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;

namespace Pinta.Core;

public sealed class HelpActions
{
	public Command Contents { get; }
	public Command Website { get; }
	public Command Bugs { get; }
	public Command Translate { get; }

	private readonly SystemManager system;
	private readonly AppActions app;
	private readonly AddinActions addins;
	public HelpActions (
		SystemManager system,
		AppActions app,
		AddinActions addins)
	{
		Contents = new Command (
			"contents",
			Translations.GetString ("Documentation"),
			null,
			Resources.StandardIcons.HelpBrowser,
			shortcuts: ["F1"]);

		Website = new Command (
			"website",
			Translations.GetString ("Pinta Website"),
			null,
			Resources.Icons.HelpWebsite);

		Bugs = new Command (
			"bugs",
			Translations.GetString ("Send Feedback or Bug Report..."),
			null,
			Resources.Icons.HelpBug);

		Translate = new Command (
			"translate",
			Translations.GetString ("Translate This Application"),
			null,
			Resources.Icons.HelpTranslate);

		this.system = system;
		this.app = app;
		this.addins = addins;
	}
	public void RegisterActions (Gtk.Application application, Gio.Menu menu)
	{
		bool isMac = system.OperatingSystem == OS.Mac;

		// Paint.NET order: Documentation, feedback | links | About.
		// Keyboard Shortcuts, Translate, the add-in manager ("Plugins") and Settings are Pinta extras.
		Gio.Menu docs_section = Gio.Menu.New ();
		docs_section.AppendItem (Contents.CreateMenuItem ());
		docs_section.AppendItem (Bugs.CreateMenuItem ());

		Gio.Menu links_section = Gio.Menu.New ();
		links_section.AppendItem (Website.CreateMenuItem ());
		links_section.AppendItem (app.KeyboardShortcuts.CreateMenuItem ());
		links_section.AppendItem (Translate.CreateMenuItem ());

		// Third-party add-ins append their items after the Plugins item.
		Gio.Menu addins_section = Gio.Menu.New ();
		addins.RegisterActions (application, addins_section);

		menu.AppendSection (null, docs_section);
		menu.AppendSection (null, links_section);
		menu.AppendSection (null, addins_section);

		// Settings and About are part of the application menu on macOS.
		if (!isMac) {
			// Settings lives here until the menu bar gets its own Settings button.
			Gio.Menu settings_section = Gio.Menu.New ();
			settings_section.AppendItem (app.Preferences.CreateMenuItem ());
			menu.AppendSection (null, settings_section);

			Gio.Menu about_section = Gio.Menu.New ();
			about_section.AppendItem (app.About.CreateMenuItem ());
			menu.AppendSection (null, about_section);
		}

		application.AddCommands ([
			Contents,
			Website,
			Bugs,
			Translate]);
	}

	public void RegisterHandlers ()
	{
		Contents.Activated += DisplayHelp;
		Website.Activated += Website_Activated;
		Bugs.Activated += Bugs_Activated;
		Translate.Activated += Translate_Activated;
	}

	private async void Bugs_Activated (object sender, EventArgs e)
	{
		await system.LaunchUri ("https://github.com/PintaProject/Pinta/issues");
	}

	private async void DisplayHelp (object sender, EventArgs e)
	{
		await system.LaunchUri ("https://pinta-project.com/user-guide");
	}

	private async void Translate_Activated (object sender, EventArgs e)
	{
		await system.LaunchUri ("https://hosted.weblate.org/engage/pinta/");
	}

	private async void Website_Activated (object sender, EventArgs e)
	{
		await system.LaunchUri ("https://www.pinta-project.com");
	}
}
