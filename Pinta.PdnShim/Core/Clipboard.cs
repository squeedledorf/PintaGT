using System;
using System.Runtime.InteropServices;
using PaintDotNet.Hosting;

namespace PaintDotNet.Clipboard
{
	/// <summary>Read access to the clipboard; the host keeps the current image available.</summary>
	public interface IClipboard
	{
	}

	public static class ClipboardExtensions
	{
		public static bool ContainsImage (this IClipboard clipboard) => ShimHost.ClipboardImage?.Invoke () is not null;

		/// <summary>The clipboard image as a new surface, or null.</summary>
		public static Surface TryGetSurface (this IClipboard clipboard)
		{
			if (ShimHost.ClipboardImage?.Invoke () is not DecodedImage image)
				return null;
			Surface surface = new (image.Width, image.Height);
			for (int y = 0; y < image.Height; y++)
				MemoryMarshal.Cast<byte, ColorBgra> (image.Bgra.AsSpan (y * image.Width * 4, image.Width * 4)).CopyTo (surface.GetRowSpan (y));
			return surface;
		}
	}
}

namespace PaintDotNet.AppModel
{
	public interface IClipboardService : Clipboard.IClipboard
	{
	}
}
