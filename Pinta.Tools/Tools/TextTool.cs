/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Olivier Dufour <olivier.duff@gmail.com>                 //
//                     Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class TextTool : BaseTool
{
	// Variables for dragging
	private PointD start_mouse_xy;
	private PointI start_click_point;
	private bool tracking;
	private readonly Gdk.Cursor cursor_move = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Move);
	private readonly Gdk.Cursor cursor_invalid = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.NotAllowed);

	private PointI click_point;
	private bool is_editing;

	//This is used to temporarily store the UserLayer's and TextLayer's previous ImageSurface states.
	private ImageSurface? text_undo_surface;
	private ImageSurface? user_undo_surface;
	private TextEngine? undo_engine;
	// The last pre-editing string, if pre-editing is active.
	private string? preedit_string;
	// The selection from when editing started. This ensures that text doesn't suddenly disappear/appear
	// if the selection changes before the text is finalized.
	private DocumentSelection? selection;

	private readonly Gtk.IMMulticontext im_context;
	private readonly TextLayout layout;

	private RectangleI CurrentTextBounds {
		get => workspace.ActiveDocument.Layers.CurrentUserLayer.TextBounds;

		set {
			workspace.ActiveDocument.Layers.CurrentUserLayer.PreviousTextBounds = workspace.ActiveDocument.Layers.CurrentUserLayer.TextBounds;
			workspace.ActiveDocument.Layers.CurrentUserLayer.TextBounds = value;
		}
	}

	private TextEngine CurrentTextEngine {
		get {
			if (!workspace.HasOpenDocuments)
				throw new InvalidOperationException ("Attempting to get CurrentTextEngine when there are no open documents");

			return workspace.ActiveDocument.Layers.CurrentUserLayer.TextEngine;
		}
	}

	private TextLayout CurrentTextLayout {
		get {
			if (layout.Engine != CurrentTextEngine)
				layout.Engine = CurrentTextEngine;
			return layout;
		}
	}

	//While this is true, text will not be finalized upon Surface.Clone calls.
	private bool ignore_clone_finalizations = false;

	//Whether or not either (or both) of the Ctrl keys are pressed.
	private bool ctrl_key = false;

	//Store the most recent mouse position.
	private PointI last_mouse_position = new (0, 0);

	public override string Name
		=> Translations.GetString ("Text");

	public override string Icon
		=> Pinta.Resources.Icons.ToolText;

	public override Gdk.Key ShortcutKey
		=> new (Gdk.Constants.KEY_T);

	public override int Priority
		=> 33;

	public override string StatusBarText
		=> Translations.GetString ("Left click to place cursor, then type desired text. Text color is primary color.");

	public override Gdk.Cursor DefaultCursor { get; }

	protected override bool ShowAntialiasingButton => true;
	protected override bool ShowBlendModeButton => true;
	protected override bool ShowSelectionQualityButton => true;
	protected override bool ShowFinishButton => true;
	protected override bool CanFinish => is_editing;

	// Paint.NET's move nub, below and to the right of the text cursor: drag it to move the text before it's finished.
	private readonly MoveNubHandle nub;
	// The caret is drawn over the canvas, not on the text layer, so the layer's opacity and blend mode can't hide it.
	private readonly CaretHandle caret;
	public override IEnumerable<IToolHandle> Handles => [nub, caret];

	private readonly IChromeService chrome;
	private readonly IPaletteService palette;
	private readonly IWorkspaceService workspace;
	public TextTool (IServiceProvider services) : base (services)
	{
		IChromeService chromeService = services.GetService<IChromeService> ();

		chrome = chromeService;
		palette = services.GetService<IPaletteService> ();
		workspace = services.GetService<IWorkspaceService> ();

		im_context = Gtk.IMMulticontext.New ();
		im_context.OnCommit += OnIMCommit;
		im_context.OnPreeditStart += OnPreeditStart;
		im_context.OnPreeditChanged += OnPreeditChanged;
		im_context.OnPreeditEnd += OnPreeditEnd;

		layout = new TextLayout (chromeService);

		DefaultCursor = GdkExtensions.CursorFromName (Pinta.Resources.StandardCursors.Text);

		nub = new MoveNubHandle (workspace);
		caret = new CaretHandle (workspace);
	}

	#region ToolBar
	// Paint.NET's default text size, in points.
	private const double DEFAULT_FONT_SIZE = 12;
	private const string SIZE_SETTING = "text-size-points";
	private const string FIXED_DPI_SETTING = "text-size-fixed-dpi";
	private const string BOLD_SETTING = "text-bold";
	private const string STRIKEOUT_SETTING = "text-strikeout";
	private const string RENDERING_SETTING = "text-rendering-mode";

	private Gtk.Label? font_label;
	private Gtk.StringList? font_families;
	private Gtk.DropDown? font_family_dropdown;
	private Gtk.SpinButton? font_size;
	private Gtk.Box? font_size_box;
	private ToolBarDropDownButton? size_unit_btn;
	private Gtk.Separator? format_sep;
	private Gtk.ToggleButton? bold_btn;
	private Gtk.ToggleButton? italic_btn;
	private Gtk.ToggleButton? underscore_btn;
	private Gtk.ToggleButton? strikeout_btn;
	private ToolBarDropDownButton? rendering_btn;
	private Gtk.Separator? alignment_sep;
	private Gtk.ToggleButton? left_alignment_btn;
	private Gtk.ToggleButton? center_alignment_btn;
	private Gtk.ToggleButton? right_alignment_btn;

	/// <summary>Paint.NET's text rendering modes.</summary>
	private enum TextRenderingMode { Smooth, SharpModern, SharpClassic }

	/// <summary>
	/// The pixel size of a font size in points. Paint.NET's "Points (image DPI)" scales with the image's resolution;
	/// "Fixed (96 DPI)" always uses 96.
	/// </summary>
	public static double PointsToPixels (double points, double dpi)
		=> points * dpi / 72;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		// Paint.NET order: Font, size with − and +, size unit | B I U S, rendering mode | alignment.
		font_label ??= Gtk.Label.New ($" {Translations.GetString ("Font")}: ");
		tb.Append (font_label);
		tb.Append (FontFamilyDropDown);
		tb.Append (FontSizeBox);
		tb.Append (SizeUnitDropDown);

		tb.Append (format_sep ??= GtkExtensions.CreateToolBarSeparator ());

		bold_btn ??= CreateToggle (Markup ("<b>B</b>"), Translations.GetString ("Bold"), Settings.GetSetting (BOLD_SETTING, false));
		italic_btn ??= CreateToggle (Markup ("<i>I</i>"), Translations.GetString ("Italic"), Settings.GetSetting (SettingNames.TEXT_ITALIC, false));
		underscore_btn ??= CreateToggle (Markup ("<u>U</u>"), Translations.GetString ("Underline"), Settings.GetSetting (SettingNames.TEXT_UNDERLINE, false));
		strikeout_btn ??= CreateToggle (Markup ("<s>abc</s>"), Translations.GetString ("Strikeout"), Settings.GetSetting (STRIKEOUT_SETTING, false));
		tb.Append (bold_btn);
		tb.Append (italic_btn);
		tb.Append (underscore_btn);
		tb.Append (strikeout_btn);
		tb.Append (RenderingDropDown);

		tb.Append (alignment_sep ??= GtkExtensions.CreateToolBarSeparator ());

		if (left_alignment_btn is null) {
			TextAlignment alignment = (TextAlignment) Settings.GetSetting (SettingNames.TEXT_ALIGNMENT, (int) TextAlignment.Left);
			left_alignment_btn = CreateToggle (Gtk.Image.NewFromIconName (Pinta.Resources.StandardIcons.FormatJustifyLeft), Translations.GetString ("Left Align"), alignment == TextAlignment.Left, radio: true);
			center_alignment_btn = CreateToggle (Gtk.Image.NewFromIconName (Pinta.Resources.StandardIcons.FormatJustifyCenter), Translations.GetString ("Center Align"), alignment == TextAlignment.Center, radio: true, left_alignment_btn);
			right_alignment_btn = CreateToggle (Gtk.Image.NewFromIconName (Pinta.Resources.StandardIcons.FormatJustifyRight), Translations.GetString ("Right Align"), alignment == TextAlignment.Right, radio: true, left_alignment_btn);
		}

		tb.Append (left_alignment_btn);
		tb.Append (center_alignment_btn!);
		tb.Append (right_alignment_btn!);

		UpdateFont ();
	}

	/// <summary>A flat tool bar toggle that never takes keyboard focus. A radio toggle is one of a group where only one is on.</summary>
	private Gtk.ToggleButton CreateToggle (Gtk.Widget child, string tooltip, bool active, bool radio = false, Gtk.ToggleButton? group = null)
	{
		Gtk.ToggleButton button = Gtk.ToggleButton.New ();
		button.Child = child;
		button.TooltipText = tooltip;
		button.HasFrame = false;
		button.CanFocus = false;
		button.FocusOnClick = false;
		if (group is not null)
			button.SetGroup (group);
		button.Active = active;
		// A radio group toggles twice per click; redraw once, for the button turned on.
		button.OnToggled += (_, _) => {
			if (!radio || button.Active)
				UpdateFont ();
		};
		return button;
	}

	// Paint.NET's formatting buttons are the letters themselves: B, I, U and a struck-out "abc".
	private static Gtk.Label Markup (string markup)
	{
		Gtk.Label label = Gtk.Label.New (null);
		label.SetMarkup (markup);
		return label;
	}

	private Gtk.DropDown FontFamilyDropDown {
		get {
			if (font_family_dropdown is not null)
				return font_family_dropdown;

			List<string> names = [];
			Pango.FontMap map = PangoCairo.Functions.FontMapGetDefault ();
			for (uint i = 0; i < map.GetNItems (); i++)
				if (map.GetObject (i) is Pango.FontFamily f)
					names.Add (f.GetName ());
			names = names.Distinct ().OrderBy (n => n, StringComparer.OrdinalIgnoreCase).ToList ();

			font_families = Gtk.StringList.New ([.. names]);
			// Type-to-search needs an expression for the item's string, which the bindings can only build through GtkBuilder.
			Gtk.Builder builder = Gtk.Builder.NewFromString ("""
				<interface>
				  <object class="GtkDropDown" id="fonts">
				    <property name="enable-search">true</property>
				    <property name="search-match-mode">substring</property>
				    <property name="expression"><lookup name="string" type="GtkStringObject"></lookup></property>
				  </object>
				</interface>
				""", -1);
			font_family_dropdown = (Gtk.DropDown) builder.GetObject ("fonts")!;
			font_family_dropdown.Model = font_families;
			// Not CanFocus = false: that would also keep focus from the popup's search entry. A pick hands focus back to the canvas.
			font_family_dropdown.FocusOnClick = false;
			font_family_dropdown.TooltipText = Translations.GetString ("Font");
			// Fixed width, so the controls after it don't move when the font name changes.
			font_family_dropdown.WidthRequest = 170;

			// As in Paint.NET, the list shows each family in its own typeface.
			Gtk.SignalListItemFactory listFactory = Gtk.SignalListItemFactory.New ();
			listFactory.OnSetup += (_, args) => {
				Gtk.Label label = Gtk.Label.New (null);
				label.Xalign = 0;
				((Gtk.ListItem) args.Object).SetChild (label);
			};
			listFactory.OnBind += (_, args) => {
				Gtk.ListItem item = (Gtk.ListItem) args.Object;
				string name = ((Gtk.StringObject) item.GetItem ()!).GetString ();
				string escaped = GLib.Functions.MarkupEscapeText (name, -1);
				((Gtk.Label) item.GetChild ()!).SetMarkup ($"<span face=\"{escaped}\">{escaped}</span>");
			};
			font_family_dropdown.ListFactory = listFactory;

			// The saved font, or the system font's family on first use.
			string saved = Settings.GetSetting (SettingNames.TEXT_FONT, string.Empty);
			string? family = Pango.FontDescription.FromString (saved.Length > 0 ? saved : Gtk.Settings.GetDefault ()!.GtkFontName!).GetFamily ();
			int index = family is null ? -1 : names.FindIndex (n => string.Equals (n, family, StringComparison.OrdinalIgnoreCase));
			if (index < 0)
				index = Math.Max (0, names.FindIndex (n => n == "Sans"));
			font_family_dropdown.Selected = (uint) index;

			font_family_dropdown.OnNotify += (_, args) => {
				if (args.Pspec.GetName () != "selected")
					return;
				if (workspace.HasOpenDocuments)
					workspace.ActiveDocument.Workspace.GrabFocusToCanvas ();
				UpdateFont ();
			};

			HookFontPopup (font_family_dropdown, names);

			return font_family_dropdown;
		}
	}

	/// <summary>
	/// As in Paint.NET's font list: it opens on the current font, and Enter in the search picks the first match.
	/// </summary>
	private void HookFontPopup (Gtk.DropDown dropdown, List<string> names)
	{
		if (FindDescendant<Gtk.Popover> (dropdown) is not Gtk.Popover popup
		    || FindDescendant<Gtk.ListView> (popup) is not Gtk.ListView list)
			return;

		popup.OnShow += (_, _) => GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_LOW, () => {
			if (dropdown.Selected != Gtk.Constants.INVALID_LIST_POSITION)
				list.ScrollTo (dropdown.Selected, Gtk.ListScrollFlags.None, null);
			return false;
		});

		// However the list closes (a pick, Enter, Esc), typing goes back to the text, not to the tool shortcuts.
		// Deferred: the popover hands focus back to the dropdown after it closes.
		popup.OnClosed += (_, _) => GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT_IDLE, () => {
			if (workspace.HasOpenDocuments)
				workspace.ActiveDocument.Workspace.GrabFocusToCanvas ();
			return false;
		});

		if (FindDescendant<Gtk.SearchEntry> (popup) is Gtk.SearchEntry search) {
			// Match the typed text here rather than read the filtered list: the search entry filters after a short delay,
			// so a fast "C059" + Enter would otherwise pick a stale match. An empty search keeps the current font.
			search.OnActivate += (_, _) => {
				string typed = search.GetText ();
				int index = typed.Length == 0 ? -1 : BestFontMatch (names, typed);
				if (index >= 0)
					dropdown.Selected = (uint) index;
				popup.Popdown ();
			};
			// The search entry takes Esc for itself; one Esc should close the list.
			search.OnStopSearch += (_, _) => popup.Popdown ();
		}
	}

	/// <summary>The font that Enter picks for the typed text: an exact name, else the first that starts with it, else the first that contains it.</summary>
	private static int BestFontMatch (List<string> names, string typed)
	{
		const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;
		int index = names.FindIndex (n => n.Equals (typed, ignoreCase));
		if (index < 0)
			index = names.FindIndex (n => n.StartsWith (typed, ignoreCase));
		if (index < 0)
			index = names.FindIndex (n => n.Contains (typed, ignoreCase));
		return index;
	}

	private static T? FindDescendant<T> (Gtk.Widget widget) where T : Gtk.Widget
	{
		for (Gtk.Widget? child = widget.GetFirstChild (); child is not null; child = child.GetNextSibling ()) {
			if (child is T match)
				return match;
			if (FindDescendant<T> (child) is T nested)
				return nested;
		}
		return null;
	}

	private string FontFamily
		=> font_family_dropdown?.SelectedItem is Gtk.StringObject s ? s.GetString () : "Sans";

	private Gtk.Box FontSizeBox {
		get {
			if (font_size_box is not null)
				return font_size_box;

			double size = Convert.ToDouble (Settings.GetSetting<object> (SIZE_SETTING, DEFAULT_FONT_SIZE));
			font_size = GtkExtensions.CreateToolBarSpinButton (1, 2000, 1, size);
			font_size.Digits = 1;
			// Paint.NET shows a whole size as "12", and allows sizes like 18.3.
			font_size.OnOutput += (spin, _) => {
				double value = spin.Value;
				spin.SetText (value == Math.Floor (value) ? value.ToString ("0") : value.ToString ("0.#"));
				return true;
			};
			font_size.TooltipText = Translations.GetString ("Change font size.") + "\n"
				   + "\n" + Translations.GetString ("Shortcut keys:")
				   + "\n" + Translations.GetString ("Press {0} to decrease font size", "\"[\"")
				   + "\n" + Translations.GetString ("Press {0} to increase font size", "\"]\"");
			font_size.OnValueChanged += (_, _) => UpdateFont ();
			font_size_box = font_size.WithOuterStepButtons ();
			return font_size_box;
		}
	}

	private double FontSizePoints
		=> font_size?.Value ?? DEFAULT_FONT_SIZE;

	private ToolBarDropDownButton SizeUnitDropDown {
		get {
			if (size_unit_btn is null) {
				size_unit_btn = ToolBarDropDownButton.New ();
				size_unit_btn.AddItem (Translations.GetString ("Points (image DPI)"), Pinta.Resources.Icons.TextNormal, false);
				size_unit_btn.AddItem (Translations.GetString ("Fixed (96 DPI)"), Pinta.Resources.Icons.TextNormal, true);
				size_unit_btn.SelectedIndex = Settings.GetSetting (FIXED_DPI_SETTING, false) ? 1 : 0;
				size_unit_btn.SelectedItemChanged += (_, _) => UpdateFont ();
			}

			return size_unit_btn;
		}
	}

	private ToolBarDropDownButton RenderingDropDown {
		get {
			if (rendering_btn is null) {
				rendering_btn = ToolBarDropDownButton.New (showLabel: true);
				rendering_btn.AddItem (Translations.GetString ("Smooth"), Pinta.Resources.Icons.AntiAliasingEnabled, TextRenderingMode.Smooth);
				rendering_btn.AddItem (Translations.GetString ("Sharp (Modern)"), Pinta.Resources.Icons.AntiAliasingDisabled, TextRenderingMode.SharpModern);
				rendering_btn.AddItem (Translations.GetString ("Sharp (Classic)"), Pinta.Resources.Icons.AntiAliasingDisabled, TextRenderingMode.SharpClassic);
				rendering_btn.SelectedIndex = Math.Clamp (Settings.GetSetting (RENDERING_SETTING, 0), 0, 2);
				rendering_btn.SelectedItemChanged += (_, _) => UpdateFont ();
			}

			return rendering_btn;
		}
	}

	private TextRenderingMode RenderingMode
		=> rendering_btn?.SelectedItem.GetTagOrDefault (TextRenderingMode.Smooth) ?? TextRenderingMode.Smooth;

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (font_family_dropdown is not null)
			settings.PutSetting (SettingNames.TEXT_FONT, FontFamily);
		if (font_size is not null)
			settings.PutSetting (SIZE_SETTING, font_size.Value);
		if (size_unit_btn is not null)
			settings.PutSetting (FIXED_DPI_SETTING, size_unit_btn.SelectedIndex == 1);
		if (bold_btn is not null)
			settings.PutSetting (BOLD_SETTING, bold_btn.Active);
		if (italic_btn is not null)
			settings.PutSetting (SettingNames.TEXT_ITALIC, italic_btn.Active);
		if (underscore_btn is not null)
			settings.PutSetting (SettingNames.TEXT_UNDERLINE, underscore_btn.Active);
		if (strikeout_btn is not null)
			settings.PutSetting (STRIKEOUT_SETTING, strikeout_btn.Active);
		if (rendering_btn is not null)
			settings.PutSetting (RENDERING_SETTING, rendering_btn.SelectedIndex);
		if (left_alignment_btn is not null)
			settings.PutSetting (SettingNames.TEXT_ALIGNMENT, (int) Alignment);
	}

	private TextAlignment Alignment {
		get {
			if (right_alignment_btn?.Active == true)
				return TextAlignment.Right;
			else if (center_alignment_btn?.Active == true)
				return TextAlignment.Center;
			else
				return TextAlignment.Left;
		}
	}

	private void HandlePintaCorePalettePrimaryColorChanged (object? sender, EventArgs e)
	{
		UpdateTextEngineColor ();
		if (is_editing || (workspace.HasOpenDocuments && CurrentTextEngine.State == TextMode.NotFinalized))
			RedrawText (is_editing, true);
	}

	private void HandleSelectedLayerChanged (object? sender, EventArgs e)
	{
		UpdateFont ();
	}

	protected override void OnAntialiasingChanged ()
	{
		UpdateFont ();
	}

	protected override void OnBlendModeChanged ()
	{
		UpdateFont ();
	}

	private void UpdateFont ()
	{
		if (workspace.HasOpenDocuments) {
			double dpi = SizeUnitDropDown.SelectedIndex == 1 ? Document.DefaultDpi : workspace.ActiveDocument.Dpi;

			Pango.FontDescription font = Pango.FontDescription.New ();
			font.SetFamily (FontFamily);
			font.SetAbsoluteSize (Pango.Functions.UnitsFromDouble (PointsToPixels (FontSizePoints, dpi)));
			font.SetWeight (bold_btn?.Active == true ? Pango.Weight.Bold : Pango.Weight.Normal);
			font.SetStyle (italic_btn?.Active == true ? Pango.Style.Italic : Pango.Style.Normal);

			CurrentTextEngine.SetFont (font, Alignment, underscore_btn?.Active == true, strikeout_btn?.Active == true);
		}

		if (is_editing || (workspace.HasOpenDocuments && CurrentTextEngine.State == TextMode.NotFinalized))
			RedrawText (is_editing, true);
	}

	private void UpdateTextEngineColor ()
	{
		if (!workspace.HasOpenDocuments) return;
		CurrentTextEngine.PrimaryColor = palette.PrimaryColor;
		CurrentTextEngine.SecondaryColor = palette.SecondaryColor;
	}

	#endregion

	#region Activation/Deactivation
	protected override void OnActivated (Document? document)
	{
		base.OnActivated (document);

		// We may need to redraw our text when the color changes
		palette.PrimaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;

		workspace.LayerAdded += HandleSelectedLayerChanged;
		workspace.LayerRemoved += HandleSelectedLayerChanged;
		workspace.SelectedLayerChanged += HandleSelectedLayerChanged;

		// We always start off not in edit mode
		is_editing = false;
	}

	protected override void OnCommit (Document? document)
	{
		im_context.FocusOut ();
		StopEditing ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);

		// Stop listening for color change events
		palette.PrimaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
		palette.SecondaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;

		workspace.LayerAdded -= HandleSelectedLayerChanged;
		workspace.LayerRemoved -= HandleSelectedLayerChanged;
		workspace.SelectedLayerChanged -= HandleSelectedLayerChanged;

		StopEditing ();
	}
	#endregion

	#region Mouse Handlers
	protected override void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
		// Either button on the move nub drags the text.
		if (is_editing && nub.ContainsPoint (e.WindowPoint)) {
			tracking = true;
			start_mouse_xy = e.PointDouble;
			start_click_point = CurrentTextEngine.Origin;
			UpdateMouseCursor (document);
			return;
		}

		ctrl_key = e.IsControlPressed;
		im_context.FocusIn (); // Grab focus so we can get keystrokes
		selection = document.Selection.Clone ();

		switch (e.MouseButton) {
			case MouseButton.Right:
				HandleRightClick (document, e);
				break;
			case MouseButton.Left:
				HandleLeftClick (document, e);
				break;
		}
	}

	private void HandleLeftClick (Document document, ToolMouseEventArgs e)
	{
		//Store the mouse position.
		PointI pt = e.Point;

		// If the user is [editing or holding down Ctrl] and clicked
		//within the text, move the cursor to the click location
		if ((is_editing || ctrl_key) && CurrentTextBounds.Contains (pt)) {
			StartEditing ();

			//Change the position of the cursor to where the mouse clicked.
			TextPosition p = CurrentTextLayout.PointToTextPosition (pt);
			CurrentTextEngine.SetCursorPosition (p, true);

			//Redraw the text with the new cursor position.
			RedrawText (true, true);

			return;
		}

		// We're already editing and the user clicked outside the text:
		// commit the user's work, and start a new edit
		StopEditing ();

		if (ctrl_key) {
			//Go through every UserLayer.
			foreach (UserLayer ul in document.Layers.UserLayers) {
				//Check each UserLayer's editable text boundaries to see if they contain the mouse position.
				if (!ul.TextBounds.Contains (pt))
					continue;

				//The mouse clicked on editable text.

				//Change the current UserLayer to the Layer that contains the text that was clicked on.
				document.Layers.SetCurrentUserLayer (ul);

				//The user is editing text now.
				is_editing = true;

				//Set the cursor in the editable text where the mouse was clicked.
				TextPosition p = CurrentTextLayout.PointToTextPosition (pt);
				CurrentTextEngine.SetCursorPosition (p, true);

				//Redraw the editable text with the cursor.
				RedrawText (true, true);

				//Don't check any more UserLayers - stop at the first UserLayer that has editable text containing the mouse position.
				return;
			}
		} else {
			if (is_editing)
				return;

			// Start editing at the cursor location
			click_point = pt;
			CurrentTextEngine.Clear ();
			UpdateFont ();
			click_point = click_point with { Y = click_point.Y - (CurrentTextLayout.FontHeight / 2) };
			CurrentTextEngine.Origin = click_point;
			StartEditing ();
			RedrawText (true, true);
		}
	}

	private void HandleRightClick (Document document, ToolMouseEventArgs e)
	{
		// A right click allows you to move the text around

		//The user is dragging text with the right mouse button held down, so track the mouse as it moves.
		tracking = true;

		//Remember the position of the mouse before the text is dragged.
		start_mouse_xy = e.PointDouble;
		start_click_point = CurrentTextEngine.Origin;

		//Change the cursor to indicate that the text is being dragged.
		UpdateMouseCursor (document);
	}

	protected override void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
		ctrl_key = e.IsControlPressed;

		last_mouse_position = e.Point;

		// If we're dragging the text around, do that
		if (tracking) {
			PointD delta = new (
				e.PointDouble.X - start_mouse_xy.X,
				e.PointDouble.Y - start_mouse_xy.Y);

			click_point = new PointI ((int) (start_click_point.X + delta.X), (int) (start_click_point.Y + delta.Y));
			CurrentTextEngine.Origin = click_point;

			RedrawText (true, true);
		} else if (is_editing && nub.ContainsPoint (e.WindowPoint)) {
			SetCursor (cursor_move);
		} else {
			UpdateMouseCursor (document);
		}
	}

	protected override void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
		// If we were dragging the text around, finish that up
		if (!tracking)
			return;

		PointD delta = new (e.PointDouble.X - start_mouse_xy.X, e.PointDouble.Y - start_mouse_xy.Y);

		click_point = new PointI ((int) (start_click_point.X + delta.X), (int) (start_click_point.Y + delta.Y));
		CurrentTextEngine.Origin = click_point;

		RedrawText (is_editing, true);
		tracking = false;
		UpdateMouseCursor (document);
	}

	private void UpdateMouseCursor (Document document)
	{
		if (tracking) {
			SetCursor (cursor_move);
			return;
		}

		//Whether or not to show the normal text cursor.
		Gdk.Cursor newCursor = cursor_invalid;

		if (ctrl_key && workspace.HasOpenDocuments) {
			//Go through every UserLayer.
			foreach (UserLayer ul in document.Layers.UserLayers) {
				if (!ul.TextBounds.Contains (last_mouse_position)) continue; //Check each UserLayer's editable text boundaries to see if they contain the mouse position.
				newCursor = DefaultCursor; //The mouse is over editable text.
			}
		} else {
			newCursor = DefaultCursor;
		}

		if (newCursor != CurrentCursor) {
			SetCursor (newCursor);
			RedrawText (is_editing, true);
		}
	}
	#endregion

	#region Keyboard Handlers

	protected override bool OnKeyDown (Document document, ToolKeyEventArgs e)
	{
		if (!workspace.HasOpenDocuments)
			return false;

		// If we are dragging the text, we
		// aren't going to handle key presses
		if (tracking)
			return false;

		// Ignore anything with Alt pressed
		if (e.IsAltPressed)
			return false;

		ctrl_key = e.Key.IsControlKey ();
		UpdateMouseCursor (document);

		bool keyHandled = false;
		if (is_editing) {
			if (preedit_string is not null && e.Event is not null) {
				// When pre-editing is active, the input method should consume all keystrokes first.
				// (e.g. Enter might be used to finish pre-editing)
				keyHandled = TryHandleChar (e.Event);
			}

			if (!keyHandled) {
				// Assume that we are going to handle the key
				keyHandled = true;

				switch (e.Key.Value) {
					case Gdk.Constants.KEY_BackSpace:
						CurrentTextEngine.PerformBackspace (e.IsControlPressed);
						break;

					case Gdk.Constants.KEY_Delete:
						CurrentTextEngine.PerformDelete ();
						break;

					case Gdk.Constants.KEY_KP_Enter:
					case Gdk.Constants.KEY_Return:
						CurrentTextEngine.PerformEnter ();
						break;

					case Gdk.Constants.KEY_Left:
						CurrentTextEngine.PerformLeft (e.IsControlPressed, e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_Right:
						CurrentTextEngine.PerformRight (e.IsControlPressed, e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_Up:
						CurrentTextEngine.PerformUp (e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_Down:
						CurrentTextEngine.PerformDown (e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_Home:
						CurrentTextEngine.PerformHome (e.IsControlPressed, e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_End:
						CurrentTextEngine.PerformEnd (e.IsControlPressed, e.IsShiftPressed);
						break;

					case Gdk.Constants.KEY_Next:
					case Gdk.Constants.KEY_Prior:
						break;

					case Gdk.Constants.KEY_Escape:
						// Escape commits the text, as in Paint.NET.
						StopEditing ();
						return true;
					case Gdk.Constants.KEY_Insert:
						if (e.IsShiftPressed) {
							CurrentTextEngine.PerformPaste (GdkExtensions.GetDefaultClipboard ()).Wait ();
						} else if (e.IsControlPressed) {
							CurrentTextEngine.PerformCopy (GdkExtensions.GetDefaultClipboard ());
						}
						break;
					default:
						if (e.IsControlPressed) {
							if (e.Key.Value == Gdk.Constants.KEY_z) {
								//Ctrl + Z for undo while editing.
								OnHandleUndo (document);

								if (workspace.ActiveDocument.History.CanUndo)
									workspace.ActiveDocument.History.Undo ();

								return true;
							} else if (e.Key.Value == Gdk.Constants.KEY_i) {
								italic_btn?.Toggle ();
							} else if (e.Key.Value == Gdk.Constants.KEY_b) {
								bold_btn?.Toggle ();
							} else if (e.Key.Value == Gdk.Constants.KEY_u) {
								underscore_btn?.Toggle ();
							} else if (e.Key.Value == Gdk.Constants.KEY_a) {
								// Select all of the text.
								CurrentTextEngine.PerformHome (true, false);
								CurrentTextEngine.PerformEnd (true, true);
							} else {
								//Ignore command shortcut.
								return false;
							}
						} else {
							if (e.Event is not null)
								keyHandled = TryHandleChar (e.Event);
						}

						break;
				}
			}

			if (keyHandled)
				RedrawText (true, true);
		} else {
			switch (e.Key.Value) {
				case Gdk.Constants.KEY_bracketleft when font_size is not null:
					font_size.Value--;
					return true;
				case Gdk.Constants.KEY_bracketright when font_size is not null:
					font_size.Value++;
					return true;
			}
		}

		return keyHandled;
	}

	protected override bool OnKeyUp (Document document, ToolKeyEventArgs e)
	{
		if (!e.Key.IsControlKey () && !e.IsControlPressed)
			return false;

		ctrl_key = false;

		UpdateMouseCursor (document);
		return false;
	}

	private bool TryHandleChar (Gdk.Event eventKey)
	{
		// Try to handle it as a character
		if (im_context.FilterKeypress (eventKey))
			return true;

		// We didn't handle the key
		return false;
	}

	private void OnIMCommit (object o, Gtk.IMContext.CommitSignalArgs args)
	{
		try {
			// Reset the pre-edit string. Depending on the platform there might still be
			// a preedit-changed signal (setting it to the empty string) after the commit, rather than before.
			UpdatePreeditString (string.Empty, redraw: false);

			CurrentTextEngine.InsertText (args.Str);
			RedrawText (true, true);
		} finally {
			im_context.Reset ();
		}
	}

	private void OnPreeditStart (object o, EventArgs args)
	{
		// Initialize to empty string (null means pre-editing is inactive).
		preedit_string = string.Empty;
	}

	private void OnPreeditEnd (object o, EventArgs args)
	{
		// Reset to indicate that pre-editing is done. There should have previously been
		// a preedit-changed signal to erase the last preedited string.
		preedit_string = null;
	}

	private void OnPreeditChanged (object o, EventArgs args)
	{
		// TODO - use the Pango.AttrList argument to better visualize the pre-edited text vs the regular text.
		im_context.GetPreeditString (out string updated_str, out _, out _);
		UpdatePreeditString (updated_str, redraw: true);
	}

	private void UpdatePreeditString (string updated, bool redraw)
	{
		// Remove the previous preedit string.
		for (int i = 0; i < preedit_string?.Length; ++i)
			CurrentTextEngine.PerformBackspace (false);

		// Insert the new string.
		preedit_string = updated;
		CurrentTextEngine.InsertText (preedit_string);

		RedrawText (true, true);
	}

	#endregion

	#region Start/Stop Editing

	private void StartEditing ()
	{
		// A click inside the text while editing only moves the cursor: the undo state stays the one from when editing began.
		if (is_editing)
			return;

		// Ensure we have an event handler added to commit the text if the layer is cloned.
		workspace.ActiveDocument.LayerCloned -= FinalizeText;
		workspace.ActiveDocument.LayerCloned += FinalizeText;

		is_editing = true;

		im_context.SetClientWidget (workspace.ActiveWorkspace.Canvas);

		selection ??= workspace.ActiveDocument.Selection.Clone ();

		//Start ignoring any Surface.Clone calls from this point on (so that it doesn't start to loop).
		ignore_clone_finalizations = true;

		//Store the previous state of the current UserLayer's and TextLayer's ImageSurfaces.
		user_undo_surface = workspace.ActiveDocument.Layers.CurrentUserLayer.Surface.Clone ();
		text_undo_surface = workspace.ActiveDocument.Layers.CurrentUserLayer.TextLayer.Layer.Surface.Clone ();

		//Store the previous state of the Text Engine.
		undo_engine = CurrentTextEngine.Clone ();

		//Update Text Engine to use current colors of color palette
		UpdateTextEngineColor ();

		//Stop ignoring any Surface.Clone calls from this point on.
		ignore_clone_finalizations = false;
	}

	/// <summary>
	/// Stops editing and, as in Paint.NET, renders the text onto the layer as a single "Text" history item.
	/// Undoing that item removes the text.
	/// </summary>
	private void StopEditing ()
	{
		im_context.SetClientWidget (null);

		if (!workspace.HasOpenDocuments)
			return;

		if (!is_editing)
			return;

		is_editing = false;

		// An empty text box that was empty when editing started changes nothing, so it gets no history item.
		if (CurrentTextEngine.State == TextMode.Uncommitted && CurrentTextEngine.IsEmpty () && undo_engine?.IsEmpty () != false)
			CurrentTextEngine.State = TextMode.Unchanged;

		if (text_undo_surface is null || user_undo_surface is null || undo_engine is null || CurrentTextEngine.State != TextMode.Uncommitted) {
			RedrawText (false, true);
			return;
		}

		Document doc = workspace.ActiveDocument;

		//Start ignoring any Surface.Clone calls from this point on (so that it doesn't start to loop).
		ignore_clone_finalizations = true;

		//Draw the text onto the UserLayer (without the cursor) rather than the TextLayer.
		RedrawText (false, false);

		//Clear the TextLayer, the text and its boundaries.
		doc.Layers.CurrentUserLayer.TextLayer.Layer.Clear ();
		CurrentTextEngine.Clear ();
		CurrentTextBounds = RectangleI.Zero;

		doc.History.PushNewItem (
			new TextHistoryItem (
				workspace,
				Icon,
				Name,
				text_undo_surface.Clone (),
				user_undo_surface.Clone (),
				undo_engine.Clone (),
				doc.Layers.CurrentUserLayer
			)
		);

		//Stop ignoring any Surface.Clone calls from this point on.
		ignore_clone_finalizations = false;

		selection = null;
	}
	#endregion

	#region Text Drawing Methods
	/// <summary>
	/// Clears the entire TextLayer and redraw the previous text boundary.
	/// </summary>
	private void ClearTextLayer ()
	{
		//Clear the TextLayer.
		workspace.ActiveDocument.Layers.CurrentUserLayer.TextLayer.Layer.Surface.Clear ();

		//Redraw the previous text boundary.
		InflateAndInvalidate (workspace.ActiveDocument.Layers.CurrentUserLayer.PreviousTextBounds);
	}

	/// <summary>
	/// Applies the antialiasing and rendering mode to the text layout.
	/// Smooth places glyphs at fractional positions with no hinting; the Sharp modes hint the outlines to the pixel grid.
	/// </summary>
	private void ApplyFontOptions ()
	{
		FontOptions options = new ();
		options.Antialias = UseAntialiasing ? Antialias.Gray : Antialias.None;
		(options.HintStyle, options.HintMetrics) = RenderingMode switch {
			TextRenderingMode.SharpModern => (HintStyle.Slight, HintMetrics.On),
			TextRenderingMode.SharpClassic => (HintStyle.Full, HintMetrics.On),
			_ => (HintStyle.None, HintMetrics.Off),
		};

		Pango.Context context = chrome.MainWindow.GetPangoContext ();
		PangoCairo.Functions.ContextSetFontOptions (context, options);
		context.SetRoundGlyphPositions (RenderingMode != TextRenderingMode.Smooth);
		CurrentTextLayout.Layout.ContextChanged ();
	}

	private void DrawLayout (Context g, Color color)
	{
		g.Antialias = UseAntialiasing ? Antialias.Gray : Antialias.None;
		g.MoveTo (CurrentTextLayout.LayoutOrigin.X, CurrentTextLayout.LayoutOrigin.Y);
		g.SetSourceColor (color);
		PangoCairo.Functions.ShowLayout (g, CurrentTextLayout.Layout);
	}

	/// <summary>
	/// Draws the text.
	/// </summary>
	/// <param name="showCursor">Whether or not to show the mouse cursor in the drawing.</param>
	/// <param name="useTextLayer">Whether or not to use the TextLayer (as opposed to the Userlayer).</param>
	private void RedrawText (bool showCursor, bool useTextLayer)
	{
		Document doc = workspace.ActiveDocument;
		UserLayer layer = doc.Layers.CurrentUserLayer;

		ApplyFontOptions ();

		RectangleI r =
			CurrentTextLayout
			.GetLayoutBounds ()
			.Inflated (10, 10);

		InflateAndInvalidate (r);
		CurrentTextBounds = r;

		if (!useTextLayer) {
			// Committing: draw the text on its own, then put it on the layer with the tool's blend mode,
			// as if it were on a new layer merged down.
			ImageSurface layerSurface = layer.Surface;
			using ImageSurface text = CairoExtensions.CreateImageSurface (Format.Argb32, layerSurface.Width, layerSurface.Height);
			using (Context tg = new (text)) {
				selection?.Clip (tg);
				DrawLayout (tg, CurrentTextEngine.PrimaryColor);
			}

			using Context g = new (layerSurface);

			if (UseAlphaBlending) {
				g.BlendSurface (text, SelectedBlendMode);
			} else {
				// Overwrite: the text's pixels replace the layer's wherever a glyph covers them.
				using ImageSurface mask = CairoExtensions.CreateImageSurface (Format.Argb32, layerSurface.Width, layerSurface.Height);
				using (Context mg = new (mask)) {
					selection?.Clip (mg);
					DrawLayout (mg, new Color (0, 0, 0));
				}

				g.Operator = Operator.Source;
				g.SetSourceSurface (text, 0, 0);
				g.MaskSurface (mask, 0, 0);
			}
		} else {
			// Live text: drawn on the TextLayer, which the canvas blends onto the image with the tool's blend mode.
			layer.TextLayer.Layer.BlendMode = UseAlphaBlending ? SelectedBlendMode : BlendMode.Normal;

			ImageSurface surf = layer.TextLayer.Layer.Surface;
			ClearTextLayer ();

			using Context g = new (surf);

			// Selected Text
			Color c = new (
				R: 0.7,
				G: 0.8,
				B: 0.9,
				A: 0.5);

			foreach (RectangleI rect in CurrentTextLayout.GetSelectionRectangles ())
				g.FillRectangle (rect.ToDouble (), c);

			g.Save ();
			selection?.Clip (g);
			DrawLayout (g, CurrentTextEngine.PrimaryColor);

			g.Restore ();

			// Paint.NET shows only the caret and the move nub while editing; no edit rectangle.
		}

		UpdateNub (doc);
		UpdateCaret (doc, showCursor && useTextLayer);

		InflateAndInvalidate (layer.PreviousTextBounds);
		InflateAndInvalidate (r);
	}

	/// <summary>Shows the move nub at the bottom of the text cursor while editing, and hides it otherwise.</summary>
	private void UpdateNub (Document doc)
	{
		doc.Workspace.InvalidateWindowRect (nub.InvalidateRect);

		nub.Active = is_editing;
		if (is_editing) {
			RectangleI loc = CurrentTextLayout.GetCursorLocation ();
			nub.CanvasPosition = new PointD (loc.X, loc.Y + loc.Height);
			doc.Workspace.InvalidateWindowRect (nub.InvalidateRect);
		}
	}

	private void UpdateCaret (Document doc, bool show)
	{
		doc.Workspace.InvalidateWindowRect (caret.InvalidateRect);

		caret.Active = show;
		if (show) {
			caret.CanvasRect = CurrentTextLayout.GetCursorLocation ();
			caret.Color = CurrentTextEngine.PrimaryColor.ToGdkRGBA ();
			doc.Workspace.InvalidateWindowRect (caret.InvalidateRect);
		}
	}

	/// <summary>The text cursor: a line in the primary colour, one image pixel wide.</summary>
	private sealed class CaretHandle (IWorkspaceService workspace) : IToolHandle
	{
		public bool Active { get; set; }
		public RectangleI CanvasRect { get; set; }
		public Gdk.RGBA Color { get; set; } = new ();

		public bool ContainsPoint (PointD windowPoint) => false;

		private RectangleD WindowRect {
			get {
				PointD top = workspace.CanvasPointToView (new PointD (CanvasRect.X, CanvasRect.Y));
				PointD bottom = workspace.CanvasPointToView (new PointD (CanvasRect.X, CanvasRect.Y + CanvasRect.Height));
				double width = Math.Max (1, workspace.GetScale ());
				return new RectangleD (Math.Floor (top.X), top.Y, width, bottom.Y - top.Y);
			}
		}

		public RectangleI InvalidateRect => WindowRect.Inflated (2, 2).ToInt ();

		public void Draw (Gtk.Snapshot snapshot)
		{
			RectangleD r = WindowRect;
			snapshot.AppendColor (Color, Graphene.Rect.Alloc ().Init ((float) r.X, (float) r.Y, (float) r.Width, (float) r.Height));
		}
	}

	/// <summary>
	/// Commits the text when its layer is cloned, so the copy and the history get the finished pixels.
	/// </summary>
	public void FinalizeText ()
	{
		//If this is true, don't commit any text - this is used to prevent the code from looping recursively.
		if (ignore_clone_finalizations)
			return;

		StopEditing ();
	}

	private void InflateAndInvalidate (in RectangleI passedRectangle)
	{
		//Create a new instance to preserve the passed Rectangle.
		RectangleI r = new (
			passedRectangle.Location,
			passedRectangle.Size);

		r = r.Inflated (2, 2);

		workspace.Invalidate (r);
	}

	#endregion

	#region Undo/Redo

	protected override bool OnHandleUndo (Document document)
	{
		if (!is_editing)
			return false;

		// Commit the text, so that the undo removes it.
		StopEditing ();

		return false;
	}

	protected override bool OnHandleRedo (Document document)
	{
		//Rather than redoing something, if the text has been edited then simply commit and do not redo.
		if (!is_editing || CurrentTextEngine.State != TextMode.Uncommitted)
			return false;

		//Commit a new TextHistoryItem.
		StopEditing ();

		return true;
	}

	#endregion

	#region Copy/Paste

	protected override async Task<bool> OnHandlePaste (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		if (!await CurrentTextEngine.PerformPaste (cb))
			return false;

		RedrawText (true, true);
		return true;
	}

	protected override bool OnHandleCopy (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		CurrentTextEngine.PerformCopy (cb);
		return true;
	}

	protected override bool OnHandleCut (Document document, Gdk.Clipboard cb)
	{
		if (!is_editing)
			return false;

		CurrentTextEngine.PerformCut (cb);
		RedrawText (true, true);
		return true;
	}

	#endregion
}
