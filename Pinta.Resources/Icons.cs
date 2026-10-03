//
// Icons.cs
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

namespace Pinta.Resources;

// Names without a "-symbolic" suffix are the colourful Paint.NET-style 16px icons
// (from Pinta 1.x) in icons/hicolor/16x16/actions. Freedesktop standard names get a
// "pinta-" prefix there so the system icon theme cannot replace them.
public static class StandardIcons
{
	public const string ApplicationExit = "application-exit-symbolic";

	public const string DialogError = "dialog-error-symbolic";

	public const string DocumentNew = "pinta-document-new";
	public const string DocumentOpen = "pinta-document-open";
	public const string DocumentPrint = "pinta-document-print";
	public const string DocumentRevert = "document-revert-symbolic";
	public const string DocumentSave = "pinta-document-save";
	public const string DocumentSaveAs = "pinta-document-save-as";

	public const string FormatJustifyLeft = "pinta-format-justify-left";
	public const string FormatJustifyCenter = "pinta-format-justify-center";
	public const string FormatJustifyRight = "pinta-format-justify-right";
	public const string FormatTextItalic = "pinta-format-text-italic";
	public const string FormatTextUnderline = "pinta-format-text-underline";

	public const string EditCopy = "pinta-edit-copy";
	public const string EditCut = "pinta-edit-cut";
	public const string EditPaste = "pinta-edit-paste";
	public const string EditRedo = "pinta-edit-redo";
	public const string EditSelectAll = "pinta-edit-select-all";
	public const string EditUndo = "pinta-edit-undo";
	public const string EditSwap = "pinta-edit-swap";

	public const string GoPrevious = "go-previous-symbolic";

	public const string HelpAbout = "help-about-symbolic";
	public const string HelpBrowser = "pinta-help";

	public const string ImageGeneric = "image-x-generic-symbolic";
	public const string ImageMissing = "image-missing-symbolic";

	public const string Preferences = "pinta-preferences";

	public const string LayerMoveUp = "pinta-layer-move-up";
	public const string LayerMoveDown = "pinta-layer-move-down";

	public const string OpenMenu = "open-menu-symbolic";
	public const string ObjectSelect = "object-select-symbolic";

	public const string ApplicationAddon = "application-x-addon-symbolic";
	public const string SystemSearch = "system-search-symbolic";
	public const string SystemSoftwareInstall = "system-software-install-symbolic";
	public const string SoftwareUpdateAvailable = "software-update-available-symbolic";

	// Only used by View > Zoom Out / Zoom In, so these show Paint.NET-style magnifiers.
	public const string ValueDecrease = "pinta-zoom-out";
	public const string ValueIncrease = "pinta-zoom-in";
	public const string ViewFullscreen = "view-fullscreen-symbolic";
	public const string ViewRefresh = "view-refresh-symbolic";
	public const string ViewConceal = "view-conceal-symbolic";
	public const string ViewReveal = "view-reveal-symbolic";

	public const string WindowClose = "window-close-symbolic";
	public const string WindowMaximize = "window-maximize-symbolic";
	public const string WindowMinimize = "window-minimize-symbolic";

	public const string ZoomFitBest = "view-zoom-window";
	public const string ZoomIn = "pinta-zoom-in";
	public const string ZoomOut = "pinta-zoom-out";
	public const string ZoomOriginal = "view-zoom-100";
}

public static class Icons
{
	// Toggles for the floating Tools, History, Layers and Colors windows (Silk icons, see icons/pinta-icons.md).
	public const string WindowTools = "pinta-window-tools";
	public const string WindowHistory = "pinta-window-history";
	public const string WindowLayers = "pinta-window-layers";
	public const string WindowColors = "pinta-window-colors";

	public const string AddinsManage = "addins-manage";

	public const string AdjustmentsDefault = "adjustments-default-symbolic";
	public const string AdjustmentsAutoLevel = "adjustments-autolevel";
	public const string AdjustmentsBlackAndWhite = "adjustments-blackandwhite";
	public const string AdjustmentsBrightnessContrast = "adjustments-brightnesscontrast";
	public const string AdjustmentsCurves = "adjustments-curves";
	public const string AdjustmentsHueSaturation = "adjustments-huesaturation";
	public const string AdjustmentsInvertColors = "adjustments-invertcolors";
	public const string AdjustmentsLevels = "adjustments-levels";
	public const string AdjustmentsPosterize = "adjustments-posterize";
	public const string AdjustmentsSepia = "adjustments-sepia";

	public const string AntiAliasingEnabled = "tool-antialiasing-enabled";
	public const string AntiAliasingDisabled = "tool-antialiasing-disabled";

	public const string BlendingNormal = "tool-blending-normal";
	public const string BlendingOverwrite = "tool-blending-overwrite";

	public const string ColorModeColor = "tool-gradient-colormode-color";
	public const string ColorModeTransparency = "tool-gradient-colormode-transparency";

	public const string CursorPosition = "ui-cursor-location-symbolic";

	public const string EditSelectionErase = "edit-selection-erase";
	public const string EditSelectionFill = "edit-selection-fill";
	public const string EditSelectionInvert = "edit-selection-invert";
	public const string EditSelectionOffset = "edit-selection-offset";
	public const string EditSelectionNone = "edit-selection-none";

	public const string EffectsAlignObject = "tool-move";
	public const string EffectsArtisticInkSketch = "effects-artistic-inksketch";
	public const string EffectsArtisticOilPainting = "effects-artistic-oilpainting";
	public const string EffectsArtisticPencilSketch = "effects-artistic-pencilsketch";
	public const string EffectsBlursFragment = "effects-blurs-fragment";
	public const string EffectsBlursGaussianBlur = "effects-blurs-gaussianblur";
	public const string EffectsBlursMotionBlur = "effects-blurs-motionblur";
	public const string EffectsBlursRadialBlur = "effects-blurs-radialblur";
	public const string EffectsBlursUnfocus = "effects-blurs-unfocus";
	public const string EffectsBlursZoomBlur = "effects-blurs-zoomblur";
	public const string EffectsColorDithering = "effects-color-dithering";
	public const string EffectsDefault = "effects-default";
	public const string EffectsDistortBulge = "effects-distort-bulge";
	public const string EffectsDistortDents = "effects-distort-dents";
	public const string EffectsDistortFrostedGlass = "effects-distort-frostedglass";
	public const string EffectsDistortPixelate = "effects-distort-pixelate";
	public const string EffectsDistortPolarInversion = "effects-distort-polarinversion";
	public const string EffectsDistortTile = "effects-distort-tile";
	public const string EffectsDistortTwist = "effects-distort-twist";
	public const string EffectsObjectFeatherObject = "effects-object-featherobject-symbolic";
	public const string EffectsNoiseAddNoise = "effects-noise-addnoise";
	public const string EffectsNoiseMedian = "effects-noise-median";
	public const string EffectsNoiseReduceNoise = "effects-noise-reducenoise";
	public const string EffectsPhotoGlow = "effects-photo-glow";
	public const string EffectsPhotoRedEyeRemove = "effects-photo-redeyeremove";
	public const string EffectsPhotoSharpen = "effects-photo-sharpen";
	public const string EffectsPhotoSoftenPortrait = "effects-photo-softenportrait";
	public const string EffectsPhotoVignette = "effects-photo-vignette";
	public const string EffectsRenderCells = "effects-render-cells";
	public const string EffectsRenderClouds = "effects-render-clouds";
	public const string EffectsRenderJuliaFractal = "effects-render-juliafractal";
	public const string EffectsRenderMandelbrotFractal = "effects-render-mandelbrotfractal";
	public const string EffectsRenderVoronoiDiagram = "effects-render-voronoidiagram";
	public const string EffectsStylizeEdgeDetect = "effects-stylize-edgedetect";
	public const string EffectsStylizeEmboss = "effects-stylize-emboss";
	public const string EffectsStylizeOutline = "effects-stylize-outline";
	public const string EffectsStylizeRelief = "effects-stylize-relief";

	public const string GradientConical = "tool-gradient-conical";
	public const string GradientDiamond = "tool-gradient-diamond";
	public const string GradientLinear = "tool-gradient-linear";
	public const string GradientLinearReflected = "tool-gradient-linear-reflected";
	public const string GradientRadial = "tool-gradient-radial";

	public const string FillStyleBackground = "tool-fillstyle-background";
	public const string FillStyleFill = "tool-fillstyle-fill";
	public const string FillStyleOutline = "tool-fillstyle-outline";
	public const string FillStyleOutlineFill = "tool-fillstyle-outlinefill";

	public const string LassoFreeform = "tool-select-lasso-freeform-symbolic";
	public const string LassoPolygon = "tool-select-lasso-polygon-symbolic";

	public const string HelpBug = "help-bug";
	public const string HelpTranslate = "help-translate";
	public const string HelpWebsite = "help-website";

	public const string HistoryList = "ui-historylist-symbolic";

	public const string ImageCrop = "image-crop";
	public const string ImageResize = "image-resize";
	public const string ImageResizeCanvas = "image-resize-canvas";
	public const string ImageFlipHorizontal = "image-flip-horizontal";
	public const string ImageFlipVertical = "image-flip-vertical";
	public const string ImageRotate90CW = "image-rotate-90cw";
	public const string ImageRotate90CCW = "image-rotate-90ccw";
	public const string ImageRotate180 = "image-rotate-180";
	public const string ImageFlatten = "image-flatten";
	public const string OrientationPortrait = "image-orientation-portrait-symbolic";
	public const string OrientationLandscape = "image-orientation-landscape-symbolic";

	public const string JoinMiter = "join-miter-symbolic";
	public const string JoinRound = "join-round-symbolic";
	public const string JoinBevel = "join-bevel-symbolic";

	public const string LayerDelete = "layers-remove-layer";
	public const string LayerDuplicate = "layers-duplicate-layer";
	public const string LayerFlipHorizontal = "layers-flip-horizontal";
	public const string LayerFlipVertical = "layers-flip-vertical";
	public const string LayerImport = "layer-import";
	public const string LayerMergeDown = "layers-merge-down";
	public const string LayerNew = "layers-add-layer";
	public const string LayerProperties = "layers-properties";
	public const string LayerRotateZoom = "layers-rotate-zoom";

	public const string Pinta = "com.github.PintaProject.Pinta";

	public const string ResizeCanvasBase = "image-resize-canvas-base";
	public const string ResizeCanvasDown = "image-resize-canvas-down";
	public const string ResizeCanvasLeft = "image-resize-canvas-left";
	public const string ResizeCanvasNE = "image-resize-canvas-ne";
	public const string ResizeCanvasNW = "image-resize-canvas-nw";
	public const string ResizeCanvasRight = "image-resize-canvas-right";
	public const string ResizeCanvasSE = "image-resize-canvas-se";
	public const string ResizeCanvasSW = "image-resize-canvas-sw";
	public const string ResizeCanvasUp = "image-resize-canvas-up";

	public const string Sampling1 = "tool-colorpicker-sampling-1x1";
	public const string Sampling3 = "tool-colorpicker-sampling-3x3";
	public const string Sampling5 = "tool-colorpicker-sampling-5x5";
	public const string Sampling7 = "tool-colorpicker-sampling-7x7";
	public const string Sampling9 = "tool-colorpicker-sampling-9x9";

	public const string TextExtraLight = "text-extra-light-symbolic";
	public const string TextLight = "text-light-symbolic";
	public const string TextNormal = "text-normal-symbolic";
	public const string TextBold = "text-bold-symbolic";
	public const string TextExtraBold = "text-extra-bold-symbolic";

	public const string TextVariantNormal = "text-variant-normal-symbolic";
	public const string TextVariantSmallCaps = "text-variant-small-caps-symbolic";
	public const string TextVariantAllSmallCaps = "text-variant-all-small-caps-symbolic";
	public const string TextVariantPetiteCaps = "text-variant-petite-caps-symbolic";
	public const string TextVariantAllPetiteCaps = "text-variant-all-petite-caps-symbolic";
	public const string TextVariantUnicase = "text-variant-unicase-symbolic";
	public const string TextVariantTitleCaps = "text-variant-title-caps-symbolic";

	public const string ToolCloneStamp = "tool-clonestamp";
	public const string ToolColorPicker = "tool-colorpicker";
	public const string ToolColorPickerPreviousTool = "tool-colorpicker-previous-tool";
	public const string ToolEllipse = "tool-ellipse";
	public const string ToolEraser = "tool-eraser";
	public const string ToolFreeformShape = "tool-freeformshape";
	public const string ToolGradient = "tool-gradient";
	public const string ToolLine = "tool-line";
	public const string ToolMove = "tool-move";
	public const string ToolMoveCursor = "tool-move-cursor-symbolic";
	public const string ToolMoveSelection = "tool-move-selection";
	public const string ToolPaintBrush = "tool-paintbrush";
	public const string ToolPaintBucket = "tool-paintbucket";
	public const string ToolPan = "tool-pan";
	public const string ToolPencil = "tool-pencil";
	public const string ToolRecolor = "tool-recolor";
	public const string ToolRectangle = "tool-rectangle";
	public const string ToolRectangleRounded = "tool-rectangle-rounded";
	public const string ToolSelectEllipse = "tool-select-ellipse";
	public const string ToolSelectLasso = "tool-select-lasso";
	public const string ToolSelectMagicWand = "tool-select-magicwand";
	public const string ToolSelectRectangle = "tool-select-rectangle";
	public const string ToolText = "pinta-tool-text";
	public const string ToolZoom = "tool-zoom";

	public const string ViewGrid = "pinta-view-grid";
	public const string ViewRulers = "view-rulers";
	public const string ViewZoom100 = "view-zoom-100";
	public const string ViewZoomSelection = "view-zoom-selection";
	public const string ViewZoomWindow = "view-zoom-window";
}

/// <summary>
/// Standard CSS cursor names (see Gdk.Cursor.new_from_name docs for a complete list).
/// </summary>
public static class StandardCursors
{
	public const string Default = "default";
	public const string Grab = "grab";
	public const string Grabbing = "grabbing";
	public const string Move = "move";
	public const string NotAllowed = "not-allowed";
	public const string Progress = "progress";

	public const string ResizeN = "n-resize";
	public const string ResizeE = "e-resize";
	public const string ResizeS = "s-resize";
	public const string ResizeW = "w-resize";
	public const string ResizeNE = "ne-resize";
	public const string ResizeNW = "nw-resize";
	public const string ResizeSE = "se-resize";
	public const string ResizeSW = "sw-resize";

	public const string Text = "text";
	public const string ZoomIn = "zoom-in";
	public const string ZoomOut = "zoom-out";
}
