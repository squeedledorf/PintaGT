using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using Pinta.Core;
using Pinta.Gui.Widgets;

namespace Pinta.PdnPlugins;

/// <summary>
/// Shows a Paint.NET IndirectUI property collection as a Pinta dialog with live preview:
/// each property gets the widget its control type asks for, rules keep read-only states and
/// linked values in sync, and every change sends a new token to the live preview.
/// </summary>
internal sealed class PdnEffectDialog
{
	private const uint EVENT_DELAY_MILLIS = 100;

	private readonly PdnEffectAdapter adapter;
	private readonly PropertyCollection props;
	private readonly Gtk.Dialog dialog;
	private readonly List<Action> refreshers = [];
	private bool refreshing;
	private uint push_timeout;

	private PdnEffectDialog (PdnEffectAdapter adapter, PropertyCollection props, Gtk.Dialog dialog)
	{
		this.adapter = adapter;
		this.props = props;
		this.dialog = dialog;
	}

	public static async Task<bool> Run (PdnEffectAdapter adapter)
	{
		PropertyBasedEffect effect;
		PropertyCollection props;
		ControlInfo ui;
		PropertyCollection? window = null;
		try {
			RenderEnvironment env = RenderEnvironment.Capture ();
			effect = (PropertyBasedEffect) adapter.Info.CreateInstance (env.CreateParameters (RenderEnvironment.CurrentLayer ()));
			props = effect.CreatePropertyCollection ();
		} catch (Exception ex) {
			PluginRegistry.AddRuntimeError (adapter.Info.File, adapter.Info.EffectType.FullName!, adapter.Info.Name, ex);
			await PintaCore.Chrome.ShowErrorDialog (PintaCore.Chrome.MainWindow, Translations.GetString ("Plugin error"), $"{adapter.Info.Name}: {PluginRegistry.Describe (ex)}", ex.ToString ());
			return false;
		}

		// Start from the last-used values, as Paint.NET does.
		if (adapter.Data.Token is PropertyBasedEffectConfigToken last)
			props.CopyCompatibleValuesFrom (last.Properties, true);

		try {
			ui = effect.CreateConfigUI (props);
		} catch (Exception ex) {
			PluginRegistry.AddRuntimeError (adapter.Info.File, adapter.Info.EffectType.FullName!, adapter.Info.Name, ex);
			ui = ControlInfo.CreateDefaultConfigUI (props);
		}

		try {
			window = effect.CreateWindowProperties ();
		} catch (Exception) {
			// CodeLab plugins set a WinForms help callback here; the defaults are fine.
		}

		string title = window?[ControlInfoPropertyNames.WindowTitle]?.Value as string is { Length: > 0 } t ? t : adapter.Info.Name;
		double widthScale = window?[ControlInfoPropertyNames.WindowWidthScale]?.Value is double ws ? ws : 1.0;

		Gtk.Dialog dialog = Gtk.Dialog.New ();
		dialog.Title = title;
		dialog.TransientFor = PintaCore.Chrome.MainWindow;
		dialog.Modal = true;
		dialog.Resizable = false;
		dialog.IconName = adapter.Icon;
		dialog.WidthRequest = (int) (400 * Math.Clamp (widthScale, 0.75, 2.5));
		dialog.AddCancelOkButtons ();
		dialog.SetDefaultResponse (Gtk.ResponseType.Ok);

		PdnEffectDialog d = new (adapter, props, dialog);
		Gtk.Box content = dialog.GetContentAreaBox ();
		content.Spacing = 12;
		content.SetAllMargins (6);

		foreach (Gtk.Widget w in d.CreateWidgets (ui))
			content.Append (w);

		if (window?[ControlInfoPropertyNames.WindowHelpContentType]?.Value is WindowHelpContentType.PlainText
			&& window[ControlInfoPropertyNames.WindowHelpContent]?.Value is string { Length: > 0 } help) {
			Gtk.Button helpButton = Gtk.Button.NewWithLabel (Translations.GetString ("Help"));
			helpButton.Halign = Gtk.Align.Start;
			helpButton.OnClicked += (_, _) => _ = PintaCore.Chrome.ShowMessageDialog (dialog, title, help);
			content.Append (helpButton);
		}

		effect.Dispose ();
		d.Push ();

		Gtk.ResponseType response = await dialog.RunAsync ();
		d.FlushPending ();
		dialog.Destroy ();
		return response == Gtk.ResponseType.Ok;
	}

	/// <summary>Widgets for a control and its children: panels stack vertically, tab containers become notebooks.</summary>
	private IEnumerable<Gtk.Widget> CreateWidgets (ControlInfo info)
	{
		switch (info) {
			case PropertyControlInfo pci:
				if (CreateWidget (pci) is Gtk.Widget w)
					yield return w;
				break;
			case TabContainerControlInfo tabs:
				yield return CreateNotebook (tabs);
				break;
			default:
				foreach (ControlInfo child in info.ChildControls)
					foreach (Gtk.Widget c in CreateWidgets (child))
						yield return c;
				break;
		}
	}

	private Gtk.Notebook CreateNotebook (TabContainerControlInfo tabs)
	{
		Gtk.Notebook notebook = Gtk.Notebook.New ();
		foreach (TabPageControlInfo page in tabs.TabPages) {
			Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 12);
			box.SetAllMargins (8);
			foreach (Gtk.Widget w in CreateWidgets (page))
				box.Append (w);
			notebook.AppendPage (box, Gtk.Label.New (page.Text ?? string.Empty));
		}
		if (tabs.StateProperty is TabContainerStateProperty state) {
			notebook.Page = Math.Clamp (state.Value.SelectedTabIndex, 0, Math.Max (0, tabs.TabPages.Count - 1));
			notebook.OnSwitchPage += (_, e) => Set (state, new TabContainerState ((int) e.PageNum));
		}
		return notebook;
	}

	/// <summary>Sends the current values to the live preview, debounced like Pinta's own effect dialogs.</summary>
	private void SchedulePush ()
	{
		if (push_timeout != 0)
			GLib.Source.Remove (push_timeout);
		push_timeout = GLib.Functions.TimeoutAdd (0, EVENT_DELAY_MILLIS, () => {
			push_timeout = 0;
			Push ();
			return false;
		});
	}

	private void FlushPending ()
	{
		if (push_timeout == 0)
			return;
		GLib.Source.Remove (push_timeout);
		push_timeout = 0;
		Push ();
	}

	private void Push ()
	{
		adapter.Data.Token = new PropertyBasedEffectConfigToken (props);
		adapter.Data.FirePropertyChanged (nameof (PdnEffectData.Token));
	}

	/// <summary>Sets a property from a widget, then refreshes every widget (rules may have changed other properties).</summary>
	private void Set (Property p, object value)
	{
		if (refreshing)
			return;
		try {
			p.Value = value;
		} catch (Exception) {
			// Read-only or out of range: the refresh below puts the widget back.
		}
		Refresh ();
		SchedulePush ();
	}

	private void Refresh ()
	{
		refreshing = true;
		try {
			foreach (Action r in refreshers)
				r ();
		} finally {
			refreshing = false;
		}
	}

	private Gtk.Widget? CreateWidget (PropertyControlInfo pci)
	{
		Property p = pci.Property;
		PropertyControlType type = pci.ControlType.Value is PropertyControlType t ? t : PropertyControlType.Slider;
		string caption = pci.GetControlValue (ControlInfoPropertyNames.DisplayName) as string ?? p.Name;
		string? description = pci.GetControlValue (ControlInfoPropertyNames.Description) as string;

		Gtk.Widget? widget = (type, p) switch {
			(PropertyControlType.Null, _) => null,
			(PropertyControlType.CheckBox, BooleanProperty b) => CheckBox (b, caption, description),
			(PropertyControlType.AngleChooser, DoubleProperty d) => Angle (d, caption),
			(PropertyControlType.ColorWheel, Int32Property i) => ColorWheel (i, caption),
			(PropertyControlType.IncrementButton, Int32Property i) => IncrementButton (i, caption, pci),
			(PropertyControlType.RadioButton, StaticListChoiceProperty s) => Radio (s, caption, pci),
			(PropertyControlType.DropDown or PropertyControlType.RadioButton, StaticListChoiceProperty s) => DropDown (s, caption, pci),
			(_, StaticListChoiceProperty s) => DropDown (s, caption, pci),
			(PropertyControlType.PanAndSlider, DoubleVectorProperty v) => Pan (v, caption),
			(_, DoubleVectorProperty v) => VectorSliders (v, caption, pci),
			(_, DoubleVector3Property v) => Vector3Sliders (v, caption, pci),
			(PropertyControlType.Label, _) => ValueLabel (p, caption),
			(_, Int32Property i) => IntSlider (i, caption, pci),
			(_, DoubleProperty d) => DoubleSlider (d, caption, pci),
			(_, BooleanProperty b) => CheckBox (b, caption, description),
			(_, StringProperty s) => TextBox (s, caption, pci),
			(PropertyControlType.LinkLabel, UriProperty u) => Link (u, caption),
			_ => ValueLabel (p, caption),
		};
		if (widget is null)
			return null;

		widget.Sensitive = !p.ReadOnly;
		p.ReadOnlyChanged += (_, e) => widget.Sensitive = !e.Value;

		if (type != PropertyControlType.CheckBox && !string.IsNullOrEmpty (description))
			widget.TooltipText = description;

		if (pci.GetControlValue (ControlInfoPropertyNames.Footnote) is string { Length: > 0 } footnote) {
			Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
			box.Append (widget);
			box.Append (Hint (footnote));
			return box;
		}
		return widget;
	}

	private static Gtk.Label Hint (string text)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.Wrap = true;
		label.Halign = Gtk.Align.Start;
		label.MaxWidthChars = 40;
		label.AddCssClass ("dim-label");
		return label;
	}

	private static Gtk.Label Caption (string text)
	{
		Gtk.Label label = Gtk.Label.New (text);
		label.Halign = Gtk.Align.Start;
		return label;
	}

	private static int ClampToInt (double v) => (int) Math.Clamp (v, int.MinValue / 2, int.MaxValue / 2);

	private Gtk.Widget IntSlider (Int32Property p, string caption, PropertyControlInfo pci)
	{
		HScaleSpinButtonWidget w = HScaleSpinButtonWidget.New (p.Value);
		w.Label = caption;
		w.MinimumValue = ClampToInt (p.MinValue);
		w.MaximumValue = ClampToInt (p.MaxValue);
		w.IncrementValue = pci.GetControlValue (ControlInfoPropertyNames.SliderSmallChange, 1.0);
		w.Value = p.Value;
		w.ValueChanged += (_, _) => Set (p, w.ValueAsInt);
		refreshers.Add (() => w.Value = p.Value);
		return w;
	}

	private Gtk.Widget DoubleSlider (DoubleProperty p, string caption, PropertyControlInfo pci)
	{
		int digits = pci.GetControlValue (ControlInfoPropertyNames.DecimalPlaces, 2);
		HScaleSpinButtonWidget w = HScaleSpinButtonWidget.New (p.Value);
		w.Label = caption;
		w.MinimumValue = ClampToInt (Math.Floor (p.MinValue));
		w.MaximumValue = ClampToInt (Math.Ceiling (p.MaxValue));
		w.DigitsValue = Math.Max (digits, 1);
		w.IncrementValue = pci.GetControlValue (ControlInfoPropertyNames.SliderSmallChange, Math.Pow (10, -Math.Max (digits, 1)));
		w.Value = p.Value;
		w.ValueChanged += (_, _) => Set (p, Math.Clamp (w.Value, p.MinValue, p.MaxValue));
		refreshers.Add (() => w.Value = p.Value);
		return w;
	}

	private Gtk.Widget Angle (DoubleProperty p, string caption)
	{
		AnglePickerWidget w = AnglePickerWidget.NewWithAngle (new DegreesAngle (p.Value));
		w.Label = caption;
		w.ValueChanged += (_, _) => {
			double deg = w.Value.Degrees;
			if (p.MinValue < 0 && deg > p.MaxValue)
				deg -= 360; // Paint.NET angles are often -180..180
			Set (p, Math.Clamp (deg, p.MinValue, p.MaxValue));
		};
		refreshers.Add (() => w.Value = new DegreesAngle (p.Value));
		return w;
	}

	private Gtk.Widget CheckBox (BooleanProperty p, string caption, string? description)
	{
		bool hasDescription = !string.IsNullOrEmpty (description);
		Gtk.CheckButton check = Gtk.CheckButton.NewWithLabel (hasDescription ? description : caption);
		check.Active = p.Value;
		check.OnToggled += (_, _) => Set (p, check.Active);
		refreshers.Add (() => check.Active = p.Value);
		if (!hasDescription || string.IsNullOrEmpty (caption))
			return check;
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.Append (Caption (caption));
		box.Append (check);
		return box;
	}

	private Gtk.Widget DropDown (StaticListChoiceProperty p, string caption, PropertyControlInfo pci)
	{
		object[] choices = p.ValueChoices;
		ComboBoxWidget w = ComboBoxWidget.New (choices.Select (pci.GetValueDisplayName));
		w.Label = caption;
		w.Active = Math.Max (0, Array.IndexOf (choices, p.Value));
		w.Changed += (_, _) => { if (w.Active >= 0) Set (p, choices[w.Active]); };
		refreshers.Add (() => w.Active = Math.Max (0, Array.IndexOf (choices, p.Value)));
		return w;
	}

	private Gtk.Widget Radio (StaticListChoiceProperty p, string caption, PropertyControlInfo pci)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		if (!string.IsNullOrEmpty (caption))
			box.Append (Caption (caption));
		Gtk.CheckButton? first = null;
		List<Gtk.CheckButton> buttons = [];
		foreach (object choice in p.ValueChoices) {
			Gtk.CheckButton b = Gtk.CheckButton.NewWithLabel (pci.GetValueDisplayName (choice));
			if (first is null) first = b; else b.SetGroup (first);
			b.Active = Equals (choice, p.Value);
			b.OnToggled += (_, _) => { if (b.Active) Set (p, choice); };
			buttons.Add (b);
			box.Append (b);
		}
		refreshers.Add (() => {
			for (int i = 0; i < buttons.Count; i++)
				buttons[i].Active = Equals (p.ValueChoices[i], p.Value);
		});
		return box;
	}

	private Gtk.Widget TextBox (StringProperty p, string caption, PropertyControlInfo pci)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		if (!string.IsNullOrEmpty (caption))
			box.Append (Caption (caption));
		if (pci.GetControlValue (ControlInfoPropertyNames.Multiline, false)) {
			Gtk.TextView view = Gtk.TextView.New ();
			view.Buffer!.SetText (p.Value, -1);
			view.WrapMode = Gtk.WrapMode.WordChar;
			view.Buffer.OnChanged += (_, _) => Set (p, view.Buffer.Text ?? string.Empty);
			refreshers.Add (() => { if (view.Buffer.Text != p.Value) view.Buffer.SetText (p.Value, -1); });
			Gtk.ScrolledWindow scroll = Gtk.ScrolledWindow.New ();
			scroll.SetChild (view);
			scroll.HeightRequest = 90;
			box.Append (scroll);
		} else {
			Gtk.Entry entry = Gtk.Entry.New ();
			entry.SetText (p.Value);
			entry.MaxLength = Math.Min (p.MaxLength, 65535);
			entry.OnChanged += (_, _) => Set (p, entry.GetText ());
			refreshers.Add (() => { if (entry.GetText () != p.Value) entry.SetText (p.Value); });
			box.Append (entry);
		}
		return box;
	}

	private Gtk.Widget ColorWheel (Int32Property p, string caption)
	{
		bool hasAlpha = p.MaxValue > 0xffffff || p.MinValue < 0;
		Cairo.Color ToCairo (int v)
		{
			PaintDotNet.ColorBgra c = hasAlpha ? PaintDotNet.ColorBgra.FromUInt32 ((uint) v) : PaintDotNet.ColorBgra.FromOpaqueInt32 (v);
			return new Cairo.Color (c.R / 255.0, c.G / 255.0, c.B / 255.0, c.A / 255.0);
		}
		int FromCairo (Cairo.Color c)
		{
			PaintDotNet.ColorBgra b = PaintDotNet.ColorBgra.FromBgra ((byte) Math.Round (c.B * 255), (byte) Math.Round (c.G * 255), (byte) Math.Round (c.R * 255), (byte) Math.Round (c.A * 255));
			return hasAlpha ? (int) b.Bgra : PaintDotNet.ColorBgra.ToOpaqueInt32 (b);
		}

		PintaColorButton button = PintaColorButton.New ();
		button.DisplayColor = ToCairo (p.Value);
		button.Hexpand = false;
		button.Halign = Gtk.Align.Start;
		button.WidthRequest = 80;
		button.OnClicked += async (_, _) => {
			using ColorPickerDialog picker = ColorPickerDialog.New (dialog, PintaCore.Palette, new SingleColor (ToCairo (p.Value)), true, false, caption);
			try {
				if (await picker.RunAsync () == Gtk.ResponseType.Ok)
					Set (p, FromCairo (((SingleColor) picker.Colors).Color));
			} finally {
				picker.Destroy ();
			}
		};
		refreshers.Add (() => button.DisplayColor = ToCairo (p.Value));

		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.Append (Caption (caption));
		box.Append (button);
		return box;
	}

	private Gtk.Widget IncrementButton (Int32Property p, string caption, PropertyControlInfo pci)
	{
		string text = pci.GetControlValue (ControlInfoPropertyNames.ButtonText) as string ?? Translations.GetString ("Reseed");
		Gtk.Button button = Gtk.Button.NewWithLabel (text);
		button.Halign = Gtk.Align.Start;
		button.OnClicked += (_, _) => Set (p, p.Value >= p.MaxValue ? p.MinValue : p.Value + 1);
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		if (!string.IsNullOrEmpty (caption))
			box.Append (Caption (caption));
		box.Append (button);
		return box;
	}

	/// <summary>Paint.NET's pan control: (0, 0) is the center and ±1 the edges of the image.</summary>
	private Gtk.Widget Pan (DoubleVectorProperty p, string caption)
	{
		Size size = PintaCore.Workspace.ImageSize;
		PointI ToPixel (Vector2Double v) => new (
			(int) Math.Round ((v.X + 1) * size.Width / 2.0),
			(int) Math.Round ((v.Y + 1) * size.Height / 2.0));

		PointPickerWidget w = PointPickerWidget.New (PintaCore.Workspace, ToPixel (p.Value), adjustToWidgetSize: false);
		w.Label = caption;
		w.PointPicked += (_, _) => {
			CenterOffset<double> o = w.Offset;
			Set (p, new Vector2Double (Math.Clamp (o.Horizontal, p.MinValueX, p.MaxValueX), Math.Clamp (o.Vertical, p.MinValueY, p.MaxValueY)));
		};
		refreshers.Add (() => w.Point = ToPixel (p.Value));
		return w;
	}

	private Gtk.Widget VectorSliders (DoubleVectorProperty p, string caption, PropertyControlInfo pci)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.Append (Caption (caption));
		box.Append (AxisSlider ("X", () => p.ValueX, v => new Vector2Double (v, p.ValueY), p.MinValueX, p.MaxValueX, p, pci));
		box.Append (AxisSlider ("Y", () => p.ValueY, v => new Vector2Double (p.ValueX, v), p.MinValueY, p.MaxValueY, p, pci));
		return box;
	}

	private Gtk.Widget Vector3Sliders (DoubleVector3Property p, string caption, PropertyControlInfo pci)
	{
		Gtk.Box box = Gtk.Box.New (Gtk.Orientation.Vertical, 6);
		box.Append (Caption (caption));
		box.Append (AxisSlider ("X", () => p.ValueX, v => Tuple.Create (v, p.ValueY, p.ValueZ), p.MinValueX, p.MaxValueX, p, pci));
		box.Append (AxisSlider ("Y", () => p.ValueY, v => Tuple.Create (p.ValueX, v, p.ValueZ), p.MinValueY, p.MaxValueY, p, pci));
		box.Append (AxisSlider ("Z", () => p.ValueZ, v => Tuple.Create (p.ValueX, p.ValueY, v), p.MinValueZ, p.MaxValueZ, p, pci));
		return box;
	}

	private Gtk.Widget AxisSlider (string axis, Func<double> get, Func<double, object> make, double min, double max, Property p, PropertyControlInfo pci)
	{
		int digits = pci.GetControlValue (ControlInfoPropertyNames.DecimalPlaces, 2);
		HScaleSpinButtonWidget w = HScaleSpinButtonWidget.New (get ());
		w.Label = axis;
		w.MinimumValue = ClampToInt (Math.Floor (min));
		w.MaximumValue = ClampToInt (Math.Ceiling (max));
		w.DigitsValue = Math.Max (digits, 1);
		w.IncrementValue = Math.Pow (10, -Math.Max (digits, 1));
		w.Value = get ();
		w.ValueChanged += (_, _) => Set (p, make (Math.Clamp (w.Value, min, max)));
		refreshers.Add (() => w.Value = get ());
		return w;
	}

	private static Gtk.Widget Link (UriProperty p, string caption)
	{
		Gtk.LinkButton link = Gtk.LinkButton.NewWithLabel (p.Value?.ToString () ?? string.Empty, caption);
		link.Halign = Gtk.Align.Start;
		return link;
	}

	private Gtk.Widget ValueLabel (Property p, string caption)
	{
		Gtk.Label label = Caption ($"{caption}: {p.Value}");
		refreshers.Add (() => label.SetText ($"{caption}: {p.Value}"));
		return label;
	}
}
