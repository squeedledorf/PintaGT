using System;
using System.Runtime.InteropServices;
using Mono.Addins;
using PaintDotNet.Hosting;
using Pinta.Core;

[assembly: Addin ("PdnPlugins", PintaCore.ApplicationVersion, Category = "Core")]
[assembly: AddinName ("Paint.NET Plugins")]
[assembly: AddinDescription ("Runs Paint.NET effect plugins from the PdnPlugins/Effects folders")]
[assembly: AddinDependency ("Pinta", PintaCore.ApplicationVersion)]
[assembly: AddinFlags (Mono.Addins.Description.AddinFlags.Hidden | Mono.Addins.Description.AddinFlags.CantUninstall)]

namespace Pinta.PdnPlugins;

[Mono.Addins.Extension]
internal sealed class PdnPluginsExtension : IExtension
{
	private Command? list_command;

	public void Initialize ()
	{
		ShimHost.DecodeImage = Decode;
		ShimHost.EncodePng = EncodePng;
		ShimHost.UserDataDirectory = PintaCore.Settings.GetUserSettingsDirectory ();

		// Plugins read the clipboard from render threads, so keep a copy of the current image.
		Gdk.Clipboard clipboard = GdkExtensions.GetDefaultClipboard ();
		clipboard.OnChanged += (_, _) => RefreshClipboard (clipboard);
		RefreshClipboard (clipboard);
		ShimHost.ClipboardImage = () => clipboard_image;

		list_command = new Command ("PdnPlugins", Translations.GetString ("Paint.NET Plugins..."), null, Resources.Icons.AddinsManage);
		list_command.Activated += (_, _) => PluginListDialog.Show ();
		PintaCore.Chrome.Application.AddCommand (list_command);
		PintaCore.Actions.Addins.AddMenuItem (list_command.CreateMenuItem ());

		try {
			PluginHost.LoadAll ();
		} catch (Exception ex) {
			PluginRegistry.Add (new PluginReport (string.Empty, string.Empty, null, PluginStatus.Failed, $"plugin loader failed: {ex}"));
		}
	}

	public void Uninitialize () => PluginHost.UnregisterAll ();

	private static volatile DecodedImage? clipboard_image;

	private static async void RefreshClipboard (Gdk.Clipboard clipboard)
	{
		try {
			using Gdk.Texture? texture = await clipboard.ReadTextureAsync ();
			if (texture is null) {
				clipboard_image = null;
				return;
			}
			using Cairo.ImageSurface surface = texture.ToSurface ();
			clipboard_image = ToDecoded (surface);
		} catch (Exception) {
			clipboard_image = null;
		}
	}

	private static DecodedImage? Decode (byte[] bytes)
	{
		try {
			using GLib.Bytes glibBytes = GLib.Bytes.New (bytes);
			using Gdk.Texture texture = Gdk.Texture.NewFromBytes (glibBytes);
			using Cairo.ImageSurface surface = texture.ToSurface ();
			return ToDecoded (surface);
		} catch (Exception) {
			return null;
		}
	}

	private static DecodedImage ToDecoded (Cairo.ImageSurface surface)
	{
		int w = surface.Width, h = surface.Height;
		byte[] bgra = new byte[w * h * 4];
		ReadOnlySpan<byte> data = surface.GetData ();
		for (int y = 0; y < h; y++)
			PixelConvert.ToStraight (
				MemoryMarshal.Cast<byte, uint> (data.Slice (y * surface.Stride, w * 4)),
				MemoryMarshal.Cast<byte, uint> (bgra.AsSpan (y * w * 4, w * 4)));
		return new DecodedImage (w, h, bgra);
	}

	private static byte[] EncodePng (DecodedImage image)
	{
		using Cairo.ImageSurface surface = CairoExtensions.CreateImageSurface (Cairo.Format.Argb32, image.Width, image.Height);
		Span<byte> data = surface.GetData ();
		for (int y = 0; y < image.Height; y++)
			PixelConvert.ToPremultiplied (
				MemoryMarshal.Cast<byte, uint> (image.Bgra.AsSpan (y * image.Width * 4, image.Width * 4)),
				MemoryMarshal.Cast<byte, uint> (data.Slice (y * surface.Stride, image.Width * 4)));
		surface.MarkDirty ();
		using GdkPixbuf.Pixbuf pixbuf = surface.ToPixbuf ();
		return pixbuf.SaveToBuffer ("png");
	}
}
