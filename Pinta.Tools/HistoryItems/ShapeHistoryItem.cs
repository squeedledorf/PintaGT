using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Paint.NET's one history item per shape, named after the tool. While the shape is still being
/// edited, undoing it removes the shape and redoing brings it back for editing. Once the shape is
/// committed, the item holds the pixels it drew instead.
/// </summary>
public sealed class ShapeHistoryItem : BaseHistoryItem
{
	private readonly BaseEditEngine engine;
	private readonly UserLayer layer;

	// The shape removed by Undo while it was still being edited, for Redo to bring back.
	private EditableShape? undone_shape;

	private SurfaceDiff? diff;
	private ImageSurface? surface;

	public ShapeHistoryItem (BaseEditEngine engine, string icon, string text, UserLayer layer)
		: base (icon, text)
	{
		this.engine = engine;
		this.layer = layer;
	}

	public UserLayer Layer => layer;

	/// <summary>Whether the shape has been drawn onto the layer.</summary>
	public bool IsCommitted { get; private set; }

	/// <summary>Records the pixels the shape drew, given the layer's surface from before it was drawn.</summary>
	internal void Commit (ImageSurface before)
	{
		diff = SurfaceDiff.Create (before, layer.Surface, true);
		surface = diff is null ? before : null;
		IsCommitted = true;
	}

	/// <summary>Takes the shape's pixels back off the layer so the shape can be edited again.</summary>
	internal void Uncommit ()
	{
		SwapPixels ();
		diff = null;
		surface = null;
		IsCommitted = false;
	}

	public override void Undo ()
	{
		if (IsCommitted)
			SwapPixels ();
		else
			undone_shape = engine.TakePendingShape (this);
	}

	public override void Redo ()
	{
		if (IsCommitted) {
			SwapPixels ();
		} else if (undone_shape is not null) {
			engine.RestorePendingShape (this, undone_shape);
			undone_shape = null;
		}
	}

	private void SwapPixels ()
	{
		ImageSurface current = layer.Surface;

		if (diff is not null) {
			diff.ApplyAndSwap (current);
			PintaCore.Workspace.Invalidate (diff.GetBounds ());
		} else if (surface is not null) {
			layer.Surface = surface;
			surface = current;
			PintaCore.Workspace.Invalidate ();
		}
	}
}
