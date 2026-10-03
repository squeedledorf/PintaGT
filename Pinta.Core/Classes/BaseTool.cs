//
// BaseTool.cs
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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gdk;
using Gtk;

namespace Pinta.Core;

[Mono.Addins.TypeExtensionPoint]
public abstract class BaseTool
{
	private readonly IToolService tools;
	private readonly IWorkspaceService workspace;

	protected IResourceService Resources { get; }
	protected ISettingsService Settings { get; }

	public const int DEFAULT_BRUSH_WIDTH = 2;

	protected BaseTool (IServiceProvider services)
	{
		Resources = services.GetService<IResourceService> ();
		Settings = services.GetService<ISettingsService> ();

		tools = services.GetService<IToolService> ();
		workspace = services.GetService<IWorkspaceService> ();

		CurrentCursor = DefaultCursor;

		// Update cursor when active document changes
		workspace.ActiveDocumentChanged += (_, _) => {
			if (IsActiveTool ()) {
				SetCursor (DefaultCursor);
			}

			RefreshFinishButton ();
		};

		if (!selection_quality_loaded) {
			selection_quality_loaded = true;
			DocumentSelection.AntialiasedClipping = Settings.GetSetting (SettingNames.SELECTION_QUALITY_ANTIALIASED, true);
		}

		// Give tools a chance to save their settings on application quit
		Settings.SaveSettingsBeforeQuit += (_, _)
			=> OnSaveSettings (Settings);
	}

	/// <summary>
	/// The localized name of the tool.
	/// </summary>
	public abstract string Name { get; }

	/// <summary>
	/// The tool's icon which is used in the toolbox.
	/// </summary>
	public abstract string Icon { get; }

	/// <summary>
	/// Localized help text shown to the user on how to use the tool.
	/// </summary>
	public virtual string StatusBarText
		=> string.Empty;

	/// <summary>
	/// The default cursor used by the tool. Return 'null' for the default pointer.
	/// </summary>
	public virtual Cursor? DefaultCursor
		=> null;

	/// <summary>
	/// The current cursor for this tool. Return 'null' for the default pointer.
	/// </summary>
	public Cursor? CurrentCursor { get; private set; }


	/// <summary>
	/// Specifies whether the tool manipulates selections.
	/// This controls whether the selection is drawn with a filled color, rather than only
	/// showing the selection outline.
	/// </summary>
	public virtual bool IsSelectionTool
		=> false;

	/// <summary>
	/// Whether or not the tool is an editable ShapeTool.
	/// </summary>
	public virtual bool IsEditableShapeTool
		=> false;

	/// <summary>
	/// A list of handles that should be drawn on the canvas window.
	/// </summary>
	public virtual IEnumerable<IToolHandle> Handles
		=> [];

	/// <summary>
	/// The shortcut key used to activate this tool in the toolbox.
	/// Return Gdk.Key.Invalid for no shortcut key.
	/// </summary>
	public virtual Gdk.Key ShortcutKey
		=> Gdk.Key.Invalid;

	/// <summary>
	/// Tools in the same group share one Tools window button, which shows the group's icon and
	/// switches to the member used last (Paint.NET's single Shapes tool). Null for a button of its own.
	/// </summary>
	public virtual ToolBoxGroup? ToolBoxGroup
		=> null;

	/// <summary>
	/// Affects the order of the tool in the toolbox. Lower numbers will appear first.
	/// </summary>
	public virtual int Priority
		=> 75;

	/// <summary>
	/// Specifies if the Antialiasing toolbar button should be shown for this tool.
	/// </summary>
	protected virtual bool ShowAntialiasingButton
		=> false;

	/// <summary>
	/// Specifies if the Alpha Blending toolbar button (Normal / Overwrite) should be shown for this tool.
	/// </summary>
	protected virtual bool ShowAlphaBlendingButton
		=> false;

	/// <summary>
	/// Specifies if the tool bar shows Paint.NET's blend mode list (the 14 layer blend modes plus Overwrite)
	/// in place of the Normal / Overwrite button. The tool applies <see cref="SelectedBlendMode"/> as if it
	/// drew on a new layer just above the active one and merged it down.
	/// </summary>
	protected virtual bool ShowBlendModeButton
		=> false;

	/// <summary>
	/// Specifies if the tool bar shows the Selection Quality button (pixelated / antialiased selection edges),
	/// which sets <see cref="DocumentSelection.AntialiasedClipping"/>.
	/// </summary>
	protected virtual bool ShowSelectionQualityButton
		=> false;

	/// <summary>
	/// Specifies if the Finish button is shown at the end of the tool bar.
	/// </summary>
	protected virtual bool ShowFinishButton
		=> false;

	/// <summary>
	/// Whether the tool has live work for the Finish button to commit. The button is greyed out otherwise.
	/// It is refreshed after the tool's mouse, key, commit, undo and redo events.
	/// </summary>
	protected virtual bool CanFinish
		=> false;

	/// <summary>
	/// Called when the Finish button is clicked: commits the tool's live work. Calls <see cref="OnCommit"/> by default.
	/// </summary>
	protected virtual void OnFinish (Document document)
		=> OnCommit (document);

	/// <summary>
	/// Called when the blend mode (or Normal / Overwrite) setting is changed.
	/// </summary>
	protected virtual void OnBlendModeChanged ()
	{
	}

	/// <summary>
	/// The blend mode the tool draws with. Normal when the tool overwrites or has no blend mode button.
	/// </summary>
	public BlendMode SelectedBlendMode
		=> HasBlendModeButton && BlendModeDropDown.SelectedItem.Tag is BlendMode mode ? mode : BlendMode.Normal;

	/// <summary>
	/// Specifies if the tool should use anti-aliasing.
	/// </summary>
	public virtual bool UseAntialiasing {
		get => ShowAntialiasingButton && AntialiasingDropDown.SelectedItem.GetTagOrDefault (true);
		set {
			if (!ShowAntialiasingButton)
				return;

			AntialiasingDropDown.SelectedItem = AntialiasingDropDown.Items.First (i => i.Tag is bool b && b == value);
		}
	}

	/// <summary>
	/// Specifies if the tool should use alpha-blending, i.e. anything but Overwrite.
	/// </summary>
	public virtual bool UseAlphaBlending {
		get => HasBlendModeButton && BlendModeDropDown.SelectedItem.Tag is BlendMode;
		set {
			if (!HasBlendModeButton)
				return;

			// Normal is the first item, Overwrite the last.
			BlendModeDropDown.SelectedIndex = value ? 0 : BlendModeDropDown.Items.Count - 1;
		}
	}

	private bool HasBlendModeButton
		=> ShowBlendModeButton || ShowAlphaBlendingButton;

	/// <summary>
	/// Called when the tool is selected from the toolbox.
	/// </summary>
	protected virtual void OnActivated (Document? document)
	{
	}

	/// <summary>
	/// Called after a history item is redone.
	/// </summary>
	protected virtual void OnAfterRedo (Document document)
	{
	}

	/// <summary>
	/// Called after the active document is saved.
	/// </summary>
	protected virtual void OnAfterSave (Document document)
	{
	}

	/// <summary>
	/// Called after a history item is undone.
	/// </summary>
	protected virtual void OnAfterUndo (Document document)
	{
	}

	/// <summary>
	/// Called when the tool needs to add its items to the Tool toolbar.
	/// </summary>
	protected virtual void OnBuildToolBar (Box toolbar)
	{
	}

	/// <summary>
	/// Called whenever another component is activated and this tool should
	/// commit any work that was in a temporary state.
	/// </summary>
	protected virtual void OnCommit (Document? document)
	{
	}

	/// <summary>
	/// Called when the tool is deselected from the toolbox.
	/// </summary>
	protected virtual void OnDeactivated (Document? document, BaseTool? newTool)
	{
	}

	/// <summary>
	/// Called to give the tool an opportunity to consume a Copy clipboard operation.
	/// Return 'true' if the Copy is handled, or 'false' to allow other
	/// components to handle it.
	/// </summary>
	protected virtual bool OnHandleCopy (Document document, Clipboard cb)
		=> false;

	/// <summary>
	/// Called to give the tool an opportunity to consume a Cut clipboard operation.
	/// Return 'true' if the Cut is handled, or 'false' to allow other
	/// components to handle it.
	/// </summary>
	protected virtual bool OnHandleCut (Document document, Clipboard cb)
		=> false;

	/// <summary>
	/// Called to give the tool an opportunity to consume a Paste clipboard operation.
	/// Return 'true' if the Cut is handled, or 'false' to allow other
	/// components to handle it.
	/// </summary>
	protected virtual Task<bool> OnHandlePaste (Document document, Clipboard cb)
		=> Task.FromResult (false);

	/// <summary>
	/// Called to give the tool an opportunity to consume a Redo operation.
	/// Return 'true' if the Redo is handled, or 'false' to allow other
	/// components to handle it.
	/// </summary>
	protected virtual bool OnHandleRedo (Document document)
		=> false;

	/// <summary>
	/// Called to give the tool an opportunity to consume an Undo operation.
	/// Return 'true' if the Undo is handled, or 'false' to allow other
	/// components to handle it.
	/// </summary>
	protected virtual bool OnHandleUndo (Document document)
		=> false;

	/// <summary>
	/// Called when a key is pressed. Return 'true' if the key is handled, or
	/// 'false' to allow other components to handle it.
	/// </summary>
	protected virtual bool OnKeyDown (Document document, ToolKeyEventArgs e)
		=> false;

	/// <summary>
	/// Called when a key is released. Return 'true' if the key is handled, or
	/// 'false' to allow other components to handle it.
	/// </summary>
	protected virtual bool OnKeyUp (Document document, ToolKeyEventArgs e)
		=> false;

	/// <summary>
	/// Called when a mouse button is pressed.
	/// </summary>
	protected virtual void OnMouseDown (Document document, ToolMouseEventArgs e)
	{
	}

	/// <summary>
	/// Called when the mouse is moved.
	/// </summary>
	protected virtual void OnMouseMove (Document document, ToolMouseEventArgs e)
	{
	}

	/// <summary>
	/// Called when a mouse button is released.
	/// </summary>
	protected virtual void OnMouseUp (Document document, ToolMouseEventArgs e)
	{
	}

	/// <summary>
	/// Called before the application exist to give tool a chance to save settings.
	/// </summary>
	protected virtual void OnSaveSettings (ISettingsService settings)
	{
		if (alphablending_button is not null)
			settings.PutSetting (BlendModeSettingName, alphablending_button.SelectedIndex);

		if (antialiasing_button is not null)
			settings.PutSetting (SettingNames.ToolAntialias (this), antialiasing_button.SelectedIndex);

		settings.PutSetting (SettingNames.SELECTION_QUALITY_ANTIALIASED, DocumentSelection.AntialiasedClipping);
	}

	/// <summary>
	/// Called when the antialias setting is changed.
	/// </summary>
	protected virtual void OnAntialiasingChanged ()
	{

	}

	/// <summary>
	/// Tool should call this in order to change the application cursor.
	/// Pass 'null' to reset the cursor back to the default.
	/// </summary>
	public void SetCursor (Cursor? cursor)
	{
		CurrentCursor = cursor;

		if (workspace.HasOpenDocuments)
			workspace.ActiveWorkspace.Canvas.Cursor = cursor;
	}

	protected bool IsActiveTool ()
	{
		return tools.CurrentTool == this;
	}

	#region Toolbar
	private static bool selection_quality_loaded;

	private ToolBarDropDownButton? antialiasing_button;
	private ToolBarDropDownButton? alphablending_button;
	private ToolBarDropDownButton? selection_quality_button;
	private Button? finish_button;
	private Separator? separator;
	private Separator? finish_separator;

	private Separator Separator => separator ??= GtkExtensions.CreateToolBarSeparator ();
	private Separator FinishSeparator => finish_separator ??= GtkExtensions.CreateToolBarSeparator ();

	private string BlendModeSettingName
		=> ShowBlendModeButton ? SettingNames.ToolBlendMode (this) : SettingNames.ToolAlphaBlend (this);

	/// <summary>
	/// The blend mode button. Item tags are a <see cref="BlendMode"/>, or null for Overwrite (the last item).
	/// </summary>
	protected ToolBarDropDownButton BlendModeDropDown {
		get {
			if (alphablending_button is null) {
				if (ShowBlendModeButton) {
					// Paint.NET: the flask icon and the mode's name, with Overwrite after the 14 layer modes.
					alphablending_button = ToolBarDropDownButton.New (showLabel: true);

					foreach (BlendMode mode in UserBlendOps.GetAllBlendModes ())
						alphablending_button.AddItem (UserBlendOps.GetBlendModeName (mode), Pinta.Resources.Icons.BlendingNormal, mode);
				} else {
					alphablending_button = ToolBarDropDownButton.New ();
					alphablending_button.AddItem (Translations.GetString ("Normal Blending"), Pinta.Resources.Icons.BlendingNormal, BlendMode.Normal);
				}

				alphablending_button.AddItem (Translations.GetString ("Overwrite"), Pinta.Resources.Icons.BlendingOverwrite, null);

				alphablending_button.SelectedIndex = Settings.GetSetting (BlendModeSettingName, 0);

				alphablending_button.SelectedItemChanged += (_, _) => OnBlendModeChanged ();
			}

			return alphablending_button;
		}
	}

	private ToolBarDropDownButton SelectionQualityDropDown {
		get {
			if (selection_quality_button is null) {
				selection_quality_button = ToolBarDropDownButton.New ();

				selection_quality_button.AddItem (Translations.GetString ("Pixelated selection quality"), Pinta.Resources.Icons.SelectionQualityPixelated, false);
				selection_quality_button.AddItem (Translations.GetString ("Antialiased selection quality"), Pinta.Resources.Icons.SelectionQualityAntialiased, true);

				selection_quality_button.SelectedIndex = DocumentSelection.AntialiasedClipping ? 1 : 0;

				selection_quality_button.SelectedItemChanged += (_, _) => {
					DocumentSelection.AntialiasedClipping = selection_quality_button.SelectedItem.GetTagOrDefault (true);

					if (workspace.HasOpenDocuments)
						workspace.Invalidate ();
				};
			}

			return selection_quality_button;
		}
	}

	private Button FinishButton {
		get {
			if (finish_button is null) {
				Box content = Box.New (Orientation.Horizontal, 4);
				content.Append (Image.NewFromIconName (Pinta.Resources.StandardIcons.ObjectSelect));
				content.Append (Label.New (Translations.GetString ("Finish")));

				finish_button = Button.New ();
				finish_button.Child = content;
				finish_button.HasFrame = false;
				finish_button.CanFocus = false;
				finish_button.TooltipText = Translations.GetString ("Finish");
				finish_button.OnClicked += (_, _) => {
					if (workspace.HasOpenDocuments)
						OnFinish (workspace.ActiveDocument);

					RefreshFinishButton ();
				};
			}

			return finish_button;
		}
	}

	/// <summary>
	/// Greys out the Finish button when there is nothing to finish. See <see cref="CanFinish"/>.
	/// </summary>
	private void RefreshFinishButton ()
	{
		if (finish_button is not null)
			finish_button.Sensitive = workspace.HasOpenDocuments && CanFinish;
	}

	private ToolBarDropDownButton AntialiasingDropDown {
		get {
			if (antialiasing_button is null) {
				antialiasing_button = ToolBarDropDownButton.New ();

				antialiasing_button.AddItem (Translations.GetString ("Antialiasing enabled"), Pinta.Resources.Icons.AntiAliasingEnabled, true);
				antialiasing_button.AddItem (Translations.GetString ("Antialiasing disabled"), Pinta.Resources.Icons.AntiAliasingDisabled, false);

				antialiasing_button.SelectedIndex = Settings.GetSetting (
					SettingNames.ToolAntialias (this),
					0);

				antialiasing_button.SelectedItemChanged += (object? sender, EventArgs e) => {
					OnAntialiasingChanged ();
				};
			}

			return antialiasing_button;
		}
	}

	#endregion

	#region Event Invokers
	internal void DoActivated (Document? document)
	{
		SetCursor (DefaultCursor);
		OnActivated (document);
		RefreshFinishButton ();
	}

	internal void DoAfterRedo (Document document)
	{
		OnAfterRedo (document);
		RefreshFinishButton ();
	}

	internal void DoAfterSave (Document document)
	{
		OnAfterSave (document);
		RefreshFinishButton ();
	}

	internal void DoAfterUndo (Document document)
	{
		OnAfterUndo (document);
		RefreshFinishButton ();
	}

	internal void DoBuildToolBar (Box toolbar)
	{
		OnBuildToolBar (toolbar);

		// Paint.NET order at the end of the bar: antialiasing, blend mode, selection quality, then Finish.
		if (HasBlendModeButton || ShowAntialiasingButton || ShowSelectionQualityButton)
			toolbar.Append (Separator);

		if (ShowAntialiasingButton)
			toolbar.Append (AntialiasingDropDown);
		if (HasBlendModeButton)
			toolbar.Append (BlendModeDropDown);

		if (ShowSelectionQualityButton) {
			// The setting is shared by every tool, so pick up a change made on another tool's bar.
			SelectionQualityDropDown.SelectedIndex = DocumentSelection.AntialiasedClipping ? 1 : 0;
			toolbar.Append (SelectionQualityDropDown);
		}

		if (ShowFinishButton) {
			toolbar.Append (FinishSeparator);
			toolbar.Append (FinishButton);
			RefreshFinishButton ();
		}
	}

	internal void DoCommit (Document? document)
	{
		OnCommit (document);
		RefreshFinishButton ();
	}

	internal void DoDeactivated (Document? document, BaseTool? newTool)
	{
		SetCursor (null);
		OnDeactivated (document, newTool);
	}

	internal bool DoHandleCopy (Document document, Clipboard clipboard) => OnHandleCopy (document, clipboard);

	internal bool DoHandleCut (Document document, Clipboard clipboard) => OnHandleCut (document, clipboard);

	internal Task<bool> DoHandlePaste (Document document, Clipboard clipboard) => OnHandlePaste (document, clipboard);

	internal bool DoHandleRedo (Document document) => OnHandleRedo (document);

	internal bool DoHandleUndo (Document document) => OnHandleUndo (document);

	internal bool DoKeyDown (Document document, ToolKeyEventArgs args)
	{
		bool handled = OnKeyDown (document, args);
		RefreshFinishButton ();
		return handled;
	}

	internal bool DoKeyUp (Document document, ToolKeyEventArgs args) => OnKeyUp (document, args);

	internal void DoMouseDown (Document document, ToolMouseEventArgs args)
	{
		OnMouseDown (document, args);
		RefreshFinishButton ();
	}

	internal void DoMouseMove (Document document, ToolMouseEventArgs args) => OnMouseMove (document, args);

	internal void DoMouseUp (Document document, ToolMouseEventArgs args)
	{
		OnMouseUp (document, args);
		RefreshFinishButton ();
	}
	#endregion
}

/// <summary>A set of tools sharing one Tools window button. See <see cref="BaseTool.ToolBoxGroup"/>.</summary>
public sealed record ToolBoxGroup (string Name, string Icon);
