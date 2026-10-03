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

	private static DecodedImage? Decode (byte[] bytes)
	{
		try {
			using GLib.Bytes glibBytes = GLib.Bytes.New (bytes);
			using Gdk.Texture texture = Gdk.Texture.NewFromBytes (glibBytes);
			using Cairo.ImageSurface surface = texture.ToSurface ();
			int w = surface.Width, h = surface.Height;
			byte[] bgra = new byte[w * h * 4];
			ReadOnlySpan<byte> data = surface.GetData ();
			for (int y = 0; y < h; y++)
				PixelConvert.ToStraight (
					MemoryMarshal.Cast<byte, uint> (data.Slice (y * surface.Stride, w * 4)),
					MemoryMarshal.Cast<byte, uint> (bgra.AsSpan (y * w * 4, w * 4)));
			return new DecodedImage (w, h, bgra);
		} catch (Exception) {
			return null;
		}
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
