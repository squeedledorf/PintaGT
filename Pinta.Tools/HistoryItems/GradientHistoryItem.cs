using Cairo;
using Pinta.Core;

namespace Pinta.Tools;

public class GradientHistoryItem : BaseHistoryItem
{
	private readonly GradientTool tool;

	private readonly SimpleHistoryItem surface;

	private GradientData gradient_data;

	public GradientHistoryItem (string icon, string text, ImageSurface passedUserSurface, int layerIndex, GradientData passedData, GradientTool passedTool) : base (icon, text)
	{
		tool = passedTool;

		gradient_data = passedData;

		surface = new SimpleHistoryItem (string.Empty, string.Empty, passedUserSurface, layerIndex);
	}


	public override void Undo ()
	{
		surface.Undo ();
		Swap ();
		SelectToolIfLive ();
	}

	public override void Redo ()
	{
		surface.Redo ();
		Swap ();
		SelectToolIfLive ();
	}

	/// <summary>
	/// Brings the Gradient tool back only when the history step leaves a live gradient with handles.
	/// A History click undoes or redoes every step in between, so the check waits until the jump is done.
	/// </summary>
	private void SelectToolIfLive ()
	{
		GLib.Functions.IdleAdd (GLib.Constants.PRIORITY_DEFAULT, () => {
			if (tool.Data.Active && PintaCore.Tools.CurrentTool != tool)
				PintaCore.Tools.SetCurrentTool (tool);
			return false;
		});
	}

	private void Swap ()
	{
		SwapData (ref gradient_data, tool);
	}

	private static void SwapData (ref GradientData new_data, GradientTool tool)
	{
		GradientData old_data = tool.Data;

		tool.Data = new_data;

		new_data = old_data;
	}
}

public struct GradientData
{
	public PointD StartPosition { get; set; }
	public PointD EndPosition { get; set; }
	public bool Active { get; set; }
	public bool IsReversed { get; set; }

	public GradientData (PointD startPosition, PointD endPosition, bool active, bool is_reversed)
	{
		this.StartPosition = startPosition;
		this.EndPosition = endPosition;
		this.Active = active;
		this.IsReversed = is_reversed;
	}

}
