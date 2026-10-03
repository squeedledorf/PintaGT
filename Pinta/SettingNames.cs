namespace Pinta;

internal static class SettingNames
{
	internal const string LANGUAGE = "language";

	internal const string NEW_IMAGE_WIDTH = "new-image-width";
	internal const string NEW_IMAGE_HEIGHT = "new-image-height";
	internal const string NEW_IMAGE_BACKGROUND = "new-image-bg";

	internal const string STARTUP_IMAGE_WIDTH = "startup-image-width";
	internal const string STARTUP_IMAGE_HEIGHT = "startup-image-height";
	internal const string STARTUP_IMAGE_BACKGROUND = "startup-image-bg";

	internal const string RULER_METRIC = "ruler-metric";
	internal const string COLOR_SCHEME = "color-scheme";
	internal const string WINDOW_MAXIMIZED = "window-maximized";
	internal const string WINDOW_SIZE_WIDTH = "window-size-width";
	internal const string WINDOW_SIZE_HEIGHT = "window-size-height";
	internal const string RULER_SHOWN = "ruler-shown";
	internal const string IMAGE_TABS_SHOWN = "image-tabs-shown";
	internal const string TOOLBAR_SHOWN = "toolbar-shown";
	internal const string MENUBAR_SHOWN = "menubar-shown";
	internal const string STATUSBAR_SHOWN = "statusbar-shown";
	// The floating Tools, History, Layers and Colors windows (their places are saved by PanelArea).
	internal const string TOOLS_WINDOW_SHOWN = "tools-window-shown";
	internal const string HISTORY_WINDOW_SHOWN = "history-window-shown";
	internal const string LAYERS_WINDOW_SHOWN = "layers-window-shown";
	internal const string COLORS_WINDOW_SHOWN = "colors-window-shown";
	internal const string LAST_DIALOG_DIRECTORY = "last-dialog-directory";
	internal const string LAST_SELECTED_TOOL = "last-selected-tool";

	internal const string RESIZE_CANVAS_ANCHOR = "resize-canvas-anchor";
	internal const string RESIZE_CANVAS_MAINTAIN_ASPECT = "resize-canvas-maintain-aspect";
	// Keys renamed from "*-use-percentage" when the default became absolute size, so the old saved default is dropped.
	internal const string RESIZE_CANVAS_USE_PERCENTAGE = "resize-canvas-by-percentage";
	internal const string RESIZE_CANVAS_PERCENTAGE = "resize-canvas-percentage";
	internal const string RESIZE_CANVAS_WIDTH = "resize-canvas-width";
	internal const string RESIZE_CANVAS_HEIGHT = "resize-canvas-height";

	internal const string RESIZE_IMAGE_MAINTAIN_ASPECT = "resize-image-maintain-aspect";
	internal const string RESIZE_IMAGE_USE_PERCENTAGE = "resize-image-by-percentage";
	internal const string RESIZE_IMAGE_PERCENTAGE = "resize-image-percentage";
	internal const string RESIZE_IMAGE_WIDTH = "resize-image-width";
	internal const string RESIZE_IMAGE_HEIGHT = "resize-image-height";
	// Renamed from "resize-image-resampling" when the list became Paint.NET's eight modes, Bicubic first.
	internal const string RESIZE_IMAGE_RESAMPLING = "resize-image-resampling-mode";
	internal const string RESIZE_IMAGE_GAMMA = "resize-image-gamma-correction";
	internal const string RESIZE_CANVAS_FILL = "resize-canvas-fill";
	// Inches or centimeters, shared by the New, Resize and Canvas Size dialogs.
	internal const string PRINT_UNITS = "print-units";
}

internal static class SettingDefaults
{
	public const string LANGUAGE = "";

	// The classic menu bar is shown by default on every OS (Paint.NET layout).
	internal static bool MenuBarShown () => true;
}
