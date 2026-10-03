using Cairo;
using Pinta.Core;

namespace Pinta;

public enum BackgroundType
{
	White,
	Transparent,
	SecondaryColor,
}

public readonly record struct NewImageDialogOptions (Size Size, bool UsingClipboard);
public readonly record struct NewImageOptions (Size NewImageSize, double Dpi);
public readonly record struct ResizeImageOptions (Size NewSize, ResamplingMode ResamplingMode, bool GammaCorrection, double Dpi);
public readonly record struct ResizeCanvasOptions (Size NewSize, Anchor Anchor, CompoundHistoryItem? CompoundAction, Color? Fill, double Dpi);
