//
// LayersPad.cs
//
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
//
// Copyright (c) 2011 Jonathan Pobst
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

using Pinta.Core;
using Pinta.Docking;
using Pinta.Gui.Widgets;

namespace Pinta;

/// <summary>
/// Paint.NET's Layers window: the layer list with its button row below it.
/// </summary>
internal static class LayersPad
{
	internal static FloatingPanel Create (LayerActions layer_actions)
	{
		FloatingPanel panel = FloatingPanel.New ("layers", Translations.GetString ("Layers"), LayersListView.New (), resizable: true);

		Gtk.Button move_up = layer_actions.MoveLayerUp.CreateDockToolBarItem ();
		Gtk.Button move_down = layer_actions.MoveLayerDown.CreateDockToolBarItem ();

		panel.Footer.AppendMultiple ([
			layer_actions.AddNewLayer.CreateDockToolBarItem (),
			layer_actions.DeleteLayer.CreateDockToolBarItem (),
			layer_actions.DuplicateLayer.CreateDockToolBarItem (),
			layer_actions.MergeLayerDown.CreateDockToolBarItem (),
			move_up,
			move_down,
			layer_actions.Properties.CreateDockToolBarItem ()
		]);

		// As in Paint.NET, Ctrl+click on Move Layer Up/Down moves the layer all the way to the top/bottom.
		// The footer catches the press before the button does, so the button doesn't also move it one step.
		Gtk.GestureClick ctrl_click = Gtk.GestureClick.New ();
		ctrl_click.SetPropagationPhase (Gtk.PropagationPhase.Capture);
		ctrl_click.OnPressed += (gesture, args) => {
			Command? command = null;
			if (gesture.GetCurrentEventState ().IsControlPressed ())
				for (Gtk.Widget? w = panel.Footer.Pick (args.X, args.Y, Gtk.PickFlags.Default); w is not null && command is null; w = w.Parent)
					command = w == move_up ? layer_actions.MoveLayerToTop : w == move_down ? layer_actions.MoveLayerToBottom : null;

			if (command is null) {
				gesture.SetState (Gtk.EventSequenceState.Denied);
				return;
			}
			gesture.SetState (Gtk.EventSequenceState.Claimed);
			command.Activate ();
		};
		panel.Footer.AddController (ctrl_click);

		return panel;
	}
}
