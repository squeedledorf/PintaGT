//
// TaskDialog.cs
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta;

/// <summary>One command link: a large label with a mnemonic ("_Save"), a caption under it and an icon.</summary>
internal sealed record TaskCommand (string Label, string Caption, string IconName);

/// <summary>
/// Paint.NET's task dialog (Unsaved Changes, Paste, Flatten): a message beside an image thumbnail
/// (or above a strip of them), then a column of command links. The mnemonic letters are underlined
/// all the time and work without Alt, as in Windows; Enter runs the focused link, Escape the cancel one.
/// </summary>
internal static class TaskDialog
{
	private const string LINK_CLASS = "pinta-task-link";
	private const int THUMBNAIL_SIZE = 96;
	private const int THUMBNAIL_PADDING = 4;
	private const int STRIP_THUMBNAIL_SIZE = 64;
	private const string THUMB_CLASS = "pinta-task-thumbnail";

	private static bool style_loaded;

	/// <summary>True while a task dialog is on screen (Exit uses it to ignore a window close behind a modal prompt).</summary>
	public static bool IsOpen => open_count > 0;
	private static int open_count;

	/// <summary>Shows the dialog and returns the index of the chosen command (closing it picks <paramref name="cancelIndex"/>).</summary>
	public static Task<int> Show (
		Gtk.Window parent,
		string title,
		string message,
		IReadOnlyList<TaskCommand> commands,
		int cancelIndex,
		int width,
		Gtk.Widget? thumbnail = null,
		Gtk.Widget? thumbnailStrip = null)
	{
		EnsureStyle ();

		TaskCompletionSource<int> result = new ();

		Gtk.Window window = Gtk.Window.New ();
		window.Title = title;
		window.IconName = "dialog-warning";
		window.TransientFor = parent;
		window.Modal = true;
		window.Resizable = false;
		window.SetSizeRequest (width, -1);
		open_count++;

		void Respond (int index)
		{
			if (result.TrySetResult (index)) {
				open_count--;
				window.Destroy ();
			}
		}

		window.OnCloseRequest += (_, _) => {
			if (result.TrySetResult (cancelIndex))
				open_count--;
			return false;
		};

		Gtk.Label messageLabel = Gtk.Label.New (message);
		messageLabel.Wrap = true;
		messageLabel.MaxWidthChars = 20; // The width request sets the real width; this only keeps the natural width from pushing past it.
		messageLabel.Xalign = 0;
		messageLabel.Valign = Gtk.Align.Start;
		messageLabel.Hexpand = true;

		Gtk.Box top = Gtk.Box.New (Gtk.Orientation.Horizontal, 12);
		top.SetAllMargins (10);
		if (thumbnail is not null)
			top.Append (thumbnail);
		top.Append (messageLabel);

		Gtk.Box links = Gtk.Box.New (Gtk.Orientation.Vertical, 2);
		links.SetAllMargins (8);
		List<(uint Key, Gtk.Button Button)> mnemonics = [];
		for (int i = 0; i < commands.Count; i++) {
			int index = i;
			Gtk.Button button = CreateLink (commands[i]);
			button.OnClicked += (_, _) => Respond (index);
			links.Append (button);

			if (MnemonicOf (commands[i].Label) is char c)
				mnemonics.Add ((Gdk.Functions.UnicodeToKeyval (char.ToLowerInvariant (c)), button));
		}

		Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		content.Append (top);
		if (thumbnailStrip is not null) {
			thumbnailStrip.MarginStart = thumbnailStrip.MarginEnd = 10;
			thumbnailStrip.MarginBottom = 8;
			content.Append (thumbnailStrip);
		}
		content.Append (links);
		window.SetChild (content);

		// Bare letters (and Alt+letter) press the matching link; Escape cancels.
		Gtk.EventControllerKey keys = Gtk.EventControllerKey.New ();
		keys.OnKeyPressed += (_, args) => {
			if (args.Keyval == Gdk.Constants.KEY_Escape) {
				Respond (cancelIndex);
				return true;
			}

			if (args.State.HasFlag (Gdk.ModifierType.ControlMask))
				return false;

			uint key = Gdk.Functions.KeyvalToLower (args.Keyval);
			foreach (var (mnemonicKey, button) in mnemonics) {
				if (mnemonicKey != key)
					continue;
				button.Activate ();
				return true;
			}

			return false;
		};
		window.AddController (keys);

		window.Present ();
		links.GetFirstChild ()?.GrabFocus ();

		return result.Task;
	}

	/// <summary>
	/// Shows the dialog and waits for it in a nested main loop, for callers that must finish synchronously
	/// (Close is activated in a loop by Close All and by the thumbnail strip).
	/// </summary>
	public static int RunBlocking (
		Gtk.Window parent,
		string title,
		string message,
		IReadOnlyList<TaskCommand> commands,
		int cancelIndex,
		int width,
		Gtk.Widget? thumbnail = null)
	{
		Task<int> task = Show (parent, title, message, commands, cancelIndex, width, thumbnail);
		if (!task.IsCompleted) {
			GLib.MainLoop loop = GLib.MainLoop.New (null, false);
			task.GetAwaiter ().OnCompleted (() => {
				if (loop.IsRunning ())
					loop.Quit ();
			});
			loop.Run ();
		}
		return task.Result;
	}

	/// <summary>The image scaled to fit a 96 px square over a checkerboard, centred in a white box with a grey border.</summary>
	public static Gtk.Widget CreateThumbnail (ImageSurface image, int size = THUMBNAIL_SIZE)
	{
		Gtk.Picture picture = Gtk.Picture.NewForPaintable (RenderThumbnail (image, size));
		picture.CanShrink = false;
		picture.Halign = Gtk.Align.Start;
		picture.Valign = Gtk.Align.Start;
		return picture;
	}

	/// <summary>
	/// Paint.NET's strip of unsaved images on Exit: one thumbnail per document; clicking one shows it in the main window.
	/// </summary>
	public static Gtk.Widget CreateThumbnailStrip (IReadOnlyList<Document> documents, WorkspaceManager workspace)
	{
		Gtk.Box row = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
		row.SetAllMargins (4);

		Gtk.ToggleButton? group = null;
		foreach (Document document in documents) {
			using ImageSurface flattened = document.GetFlattenedImage ();
			Gtk.ToggleButton button = Gtk.ToggleButton.New ();
			button.AddCssClass (THUMB_CLASS);
			button.TooltipText = document.DisplayName;
			button.SetChild (CreateThumbnail (flattened, STRIP_THUMBNAIL_SIZE));
			button.Active = workspace.ActiveDocumentOrDefault == document;
			if (group is null)
				group = button;
			else
				button.SetGroup (group);

			button.OnToggled += (_, _) => {
				int index = workspace.OpenDocuments.IndexOf (document);
				if (button.Active && index >= 0)
					workspace.SetActiveDocument (index);
			};
			row.Append (button);
		}

		Gtk.ScrolledWindow scroller = Gtk.ScrolledWindow.New ();
		scroller.SetPolicy (Gtk.PolicyType.Automatic, Gtk.PolicyType.Never);
		scroller.HasFrame = true;
		scroller.AddCssClass ("view");
		scroller.SetChild (row);
		return scroller;
	}

	private static Gdk.Texture RenderThumbnail (ImageSurface image, int size)
	{
		double fit = Math.Min (1.0, Math.Min (size / (double) image.Width, size / (double) image.Height));
		int width = Math.Max (1, (int) Math.Round (image.Width * fit));
		int height = Math.Max (1, (int) Math.Round (image.Height * fit));

		int box = size + 2 * THUMBNAIL_PADDING;
		using ImageSurface surface = CairoExtensions.CreateImageSurface (Format.Argb32, box, box);
		using (Context g = new (surface)) {
			g.SetSourceColor (new Color (1, 1, 1));
			g.Paint ();
			g.Rectangle (0.5, 0.5, box - 1, box - 1);
			g.SetSourceColor (new Color (0.8, 0.8, 0.8));
			g.LineWidth = 1;
			g.Stroke ();

			g.Translate ((box - width) / 2, (box - height) / 2);

			// A soft shadow, as on the main window's image tabs, so a white image still shows its edges.
			for (int i = 2; i >= 1; i--) {
				g.Rectangle (-i, -i, width + 2 * i, height + 2 * i);
				g.SetSourceColor (new Color (0, 0, 0, 0.08));
				g.Fill ();
			}

			g.Rectangle (0, 0, width, height);
			g.Clip ();
			using Pattern checkers = CairoExtensions.CreateTransparentBackgroundPattern (4);
			g.SetSource (checkers);
			g.Paint ();
			g.Scale (fit, fit);
			using SurfacePattern pattern = new (image) { Filter = Filter.Good };
			g.SetSource (pattern);
			g.Paint ();
		}
		return surface.ToTexture ();
	}

	private static Gtk.Button CreateLink (TaskCommand command)
	{
		Gtk.Label label = Gtk.Label.New (null);
		label.SetMarkup (MnemonicMarkup (command.Label));
		label.Xalign = 0;
		label.AddCssClass ("title");

		Gtk.Label caption = Gtk.Label.New (command.Caption);
		caption.Wrap = true;
		caption.MaxWidthChars = 20;
		caption.Xalign = 0;
		caption.AddCssClass ("caption");

		Gtk.Box text = Gtk.Box.New (Gtk.Orientation.Vertical, 0);
		text.Append (label);
		text.Append (caption);
		text.Hexpand = true;

		Gtk.Image icon = Gtk.Image.NewFromIconName (command.IconName);
		icon.PixelSize = 16;
		icon.Valign = Gtk.Align.Start;
		icon.MarginTop = 3;

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Horizontal, 6);
		box.Append (icon);
		box.Append (text);

		Gtk.Button button = Gtk.Button.New ();
		button.AddCssClass (LINK_CLASS);
		button.SetChild (box);
		return button;
	}

	/// <summary>The character after the first underscore, e.g. 'n' in "Do_n't Save".</summary>
	private static char? MnemonicOf (string label)
	{
		int i = label.IndexOf ('_');
		return i >= 0 && i + 1 < label.Length ? label[i + 1] : null;
	}

	/// <summary>"Do_n't Save" as markup with the mnemonic always underlined.</summary>
	private static string MnemonicMarkup (string label)
	{
		int i = label.IndexOf ('_');
		if (i < 0 || i + 1 >= label.Length)
			return GLib.Functions.MarkupEscapeText (label, -1);

		return GLib.Functions.MarkupEscapeText (label[..i], -1)
			+ "<u>" + GLib.Functions.MarkupEscapeText (label[(i + 1)..(i + 2)], -1) + "</u>"
			+ GLib.Functions.MarkupEscapeText (label[(i + 2)..], -1);
	}

	/// <summary>
	/// Command links: blue text on a flat face, a pale blue box on hover and focus (any focus, not just keyboard focus,
	/// so the default link is boxed even when the dialog was opened with the mouse, as in Paint.NET); the Exit strip's thumbnails likewise.
	/// Scoped to the link class so it cannot touch anything else (the shared style.css belongs to the theme work).
	/// </summary>
	private static void EnsureStyle ()
	{
		if (style_loaded)
			return;

		Gdk.Display? display = Gdk.Display.GetDefault ();
		if (display is null)
			return;

		Gtk.CssProvider provider = Gtk.CssProvider.New ();
		provider.LoadFromString ($$"""
			button.{{LINK_CLASS}} { background: none; border: 1px solid transparent; border-radius: 0; box-shadow: none; padding: 5px 8px; color: #0b55a8; }
			button.{{LINK_CLASS}}:hover, button.{{LINK_CLASS}}:focus { background-color: #e5f1fb; border-color: #a8cdef; }
			button.{{LINK_CLASS}}:active { background-color: #cce4f7; border-color: #7eb4ea; }
			button.{{LINK_CLASS}} label.title { font-size: 1.25em; }
			button.{{LINK_CLASS}} label.caption { font-size: 0.95em; }
			button.{{THUMB_CLASS}} { background: none; border: 1px solid transparent; border-radius: 0; box-shadow: none; padding: 3px; }
			button.{{THUMB_CLASS}}:hover { background-color: #e5f1fb; border-color: #a8cdef; }
			button.{{THUMB_CLASS}}:checked { background-color: #cce4f7; border-color: #3c8ee0; }
			""");
		Gtk.StyleContext.AddProviderForDisplay (display, provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
		style_loaded = true;
	}
}
