//
// MoveSelectedTool.cs
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
using System.Linq;
using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public sealed class MoveSelectedTool : BaseTransformTool
{
	private MovePixelsHistoryItem? hist;
	private DocumentSelection? original_selection;
	private readonly Matrix original_transform = CairoExtensions.CreateIdentityMatrix ();

	private const string SAMPLING_SETTING = "move-selected-sampling";

	// Paint.NET's resampling modes, mapped to the closest Cairo filter. Anisotropic has no Cairo equivalent.
	private static readonly (string Label, Filter Filter)[] sampling_modes = [
		(Translations.GetString ("Nearest Neighbor"), Filter.Nearest),
		(Translations.GetString ("Bilinear"), Filter.Bilinear),
		(Translations.GetString ("Multisample Bilinear"), Filter.Good),
		(Translations.GetString ("Bicubic"), Filter.Best),
	];

	private readonly SystemManager system_manager;
	private readonly IWorkspaceService workspace;
	public MoveSelectedTool (IServiceProvider services) : base (services)
	{
		system_manager = services.GetService<SystemManager> ();
		workspace = services.GetService<IWorkspaceService> ();

		workspace.SelectionChanged += (_, _) => UpdateFinishButton ();
		workspace.ActiveDocumentChanged += (_, _) => UpdateFinishButton ();
	}

	public override string Name => Translations.GetString ("Move Selected Pixels");
	public override string Icon => Pinta.Resources.Icons.ToolMove;
	public override string StatusBarText => Translations.GetString (
		"Drag to move, drag a nub to resize, drag just outside to rotate (or right-drag)." +
		"\nHold Ctrl to move a copy, Shift to keep the aspect ratio or rotate in 15° steps.");

	public override Gdk.Cursor DefaultCursor => Gdk.Cursor.NewFromTexture (Resources.GetIcon (Pinta.Resources.Icons.ToolMoveCursor), 0, 0, null);
	public override Gdk.Key ShortcutKey => new (Gdk.Constants.KEY_M);
	public override int Priority => 3;

	protected override RectangleD GetSourceRectangle (Document document)
		=> document.Selection.SelectionPolygons.Count == 0
		? new RectangleD (0, 0, document.ImageSize.Width, document.ImageSize.Height)
		: document.Selection.GetBounds ();

	// With no selection the whole layer moves, so its nubs are always shown.
	protected override bool ShowFrame (Document document)
		=> true;

	protected override void OnBuildToolBar (Gtk.Box tb)
	{
		base.OnBuildToolBar (tb);

		tb.Append (SamplingLabel);
		tb.Append (SamplingComboBox);
		tb.Append (Separator);
		tb.Append (FinishButton);

		UpdateFinishButton ();
	}

	protected override void OnSaveSettings (ISettingsService settings)
	{
		base.OnSaveSettings (settings);

		if (sampling_combo is not null)
			settings.PutSetting (SAMPLING_SETTING, sampling_combo.ComboBox.Active);
	}

	protected override void OnStartTransform (Document document)
	{
		base.OnStartTransform (document);

		// If there is no selection, select the whole image.
		if (document.Selection.SelectionPolygons.Count == 0) {
			RectangleD imageBounds = new (0, 0, document.ImageSize.Width, document.ImageSize.Height);
			document.Selection.CreateRectangleSelection (imageBounds);
		}

		// Ctrl on pixels that are already floating: drop a copy of them where they are, then carry on with another copy.
		if (IsCopying && document.Layers.ShowSelectionLayer)
			document.FinishSelection ();

		original_selection = document.Selection.Clone ();
		original_transform.InitMatrix (document.Layers.SelectionLayer.Transform);

		hist = new MovePixelsHistoryItem (Icon, Name, document);
		hist.TakeSnapshot (!document.Layers.ShowSelectionLayer);

		if (!document.Layers.ShowSelectionLayer) {
			// Copy the selection to the temp layer
			document.Layers.CreateSelectionLayer ();
			document.Layers.ShowSelectionLayer = true;
			// Use same BlendMode, Opacity and Visibility for SelectionLayer
			document.Layers.SelectionLayer.BlendMode = document.Layers.CurrentUserLayer.BlendMode;
			document.Layers.SelectionLayer.Opacity = document.Layers.CurrentUserLayer.Opacity;
			document.Layers.SelectionLayer.Hidden = document.Layers.CurrentUserLayer.Hidden;

			using Context selection_ctx = new (document.Layers.SelectionLayer.Surface);
			selection_ctx.AppendPath (document.Selection.SelectionPath);
			selection_ctx.FillRule = FillRule.EvenOdd;
			selection_ctx.SetSourceSurface (document.Layers.CurrentUserLayer.Surface, 0, 0);
			selection_ctx.Clip ();
			selection_ctx.Paint ();

			// Ctrl moves a copy, leaving the original pixels in place.
			if (!IsCopying) {
				var surf = document.Layers.CurrentUserLayer.Surface;

				using Context surf_ctx = new (surf);
				surf_ctx.AppendPath (document.Selection.SelectionPath);
				surf_ctx.FillRule = FillRule.EvenOdd;
				surf_ctx.Operator = Cairo.Operator.Clear;
				surf_ctx.Fill ();
			}
		}

		document.Layers.SelectionLayer.TransformFilter = SelectedFilter;
		UpdateFinishButton ();

		document.Workspace.Invalidate ();
	}

	protected override void OnUpdateTransform (Document document, Matrix transform)
	{
		base.OnUpdateTransform (document, transform);

		document.Selection = original_selection!.Transform (transform); // NRT - Set in OnStartTransform
		document.Selection.Visible = true;

		document.Layers.SelectionLayer.Transform.InitMatrix (original_transform);
		document.Layers.SelectionLayer.Transform.Multiply (transform);

		document.Workspace.Invalidate ();
	}

	protected override void OnFinishTransform (Document document, Matrix transform)
	{
		base.OnFinishTransform (document, transform);

		// Also transform the base selection used for the various select modes.
		var prev_selection = document.PreviousSelection;
		document.PreviousSelection = prev_selection.Transform (transform);

		if (hist != null)
			document.History.PushNewItem (hist);

		hist = null;
		original_selection = null;
		original_transform.InitIdentity ();
		UpdateFinishButton ();
	}

	protected override void OnCommit (Document? document)
	{
		document?.FinishSelection ();
		UpdateFinishButton ();
	}

	protected override void OnDeactivated (Document? document, BaseTool? newTool)
	{
		base.OnDeactivated (document, newTool);

		document?.FinishSelection ();
	}

	protected override void OnAfterUndo (Document document)
	{
		base.OnAfterUndo (document);
		UpdateFinishButton ();
	}

	protected override void OnAfterRedo (Document document)
	{
		base.OnAfterRedo (document);
		UpdateFinishButton ();
	}

	private Filter SelectedFilter {
		get {
			int index = SamplingComboBox.ComboBox.Active;
			return sampling_modes[index >= 0 && index < sampling_modes.Length ? index : sampling_modes.Length - 1].Filter;
		}
	}

	// Finish is only available while there are floating pixels to drop.
	private void UpdateFinishButton ()
	{
		if (finish_button is not null)
			finish_button.Sensitive = workspace.HasOpenDocuments && workspace.ActiveDocument.Layers.ShowSelectionLayer;
	}

	private Gtk.Label? sampling_label;
	private ToolBarComboBox? sampling_combo;
	private Gtk.Separator? separator;
	private Gtk.Button? finish_button;

	private Gtk.Label SamplingLabel => sampling_label ??= Gtk.Label.New (string.Format (" {0}: ", Translations.GetString ("Sampling")));

	private Gtk.Separator Separator => separator ??= GtkExtensions.CreateToolBarSeparator ();

	private ToolBarComboBox SamplingComboBox {
		get {
			if (sampling_combo is null) {
				int index = Math.Clamp (Settings.GetSetting (SAMPLING_SETTING, sampling_modes.Length - 1), 0, sampling_modes.Length - 1);
				sampling_combo = ToolBarComboBox.New (150, index, false, sampling_modes.Select (m => m.Label));

				// Re-render floating pixels with the new filter straight away.
				sampling_combo.ComboBox.OnChanged += (_, _) => {
					if (!workspace.HasOpenDocuments)
						return;

					Document document = workspace.ActiveDocument;
					document.Layers.SelectionLayer.TransformFilter = SelectedFilter;
					document.Workspace.Invalidate ();
				};
			}

			return sampling_combo;
		}
	}

	private Gtk.Button FinishButton {
		get {
			if (finish_button is null) {
				Gtk.Box content = Gtk.Box.New (Gtk.Orientation.Horizontal, 4);
				content.Append (Gtk.Image.NewFromIconName (Pinta.Resources.StandardIcons.ObjectSelect));
				content.Append (Gtk.Label.New (Translations.GetString ("Finish")));

				finish_button = Gtk.Button.New ();
				finish_button.Child = content;
				finish_button.HasFrame = false;
				finish_button.CanFocus = false;
				finish_button.TooltipText = Translations.GetString ("Finish");
				finish_button.OnClicked += (_, _) => {
					if (workspace.HasOpenDocuments)
						workspace.ActiveDocument.FinishSelection ();

					UpdateFinishButton ();
				};
			}

			return finish_button;
		}
	}
}
