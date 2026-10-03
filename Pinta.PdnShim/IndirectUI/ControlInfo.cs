using System;
using System.Collections.Generic;
using System.Linq;
using PaintDotNet.PropertySystem;

namespace PaintDotNet.IndirectUI;

public enum PropertyControlType
{
	AngleChooser = 0,
	CheckBox = 1,
	PanAndSlider = 2,
	Slider = 3,
	IncrementButton = 4,
	DropDown = 5,
	TextBox = 6,
	RadioButton = 7,
	ColorWheel = 8,
	RollBallAndSliders = 9,
	FileChooser = 10,
	LinkLabel = 11,
	FolderChooser = 12,
	Null = 13,
	Label = 14,
}

public enum ControlInfoPropertyNames
{
	DisplayName = 0,
	Description = 1,
	ControlType = 2,
	ButtonText = 3,
	UseExponentialScale = 4,
	DecimalPlaces = 5,
	SliderSmallChange = 6,
	SliderSmallChangeX = 7,
	SliderSmallChangeY = 8,
	SliderLargeChange = 9,
	SliderLargeChangeX = 10,
	SliderLargeChangeY = 11,
	UpDownIncrement = 12,
	UpDownIncrementX = 13,
	UpDownIncrementY = 14,
	StaticImageUnderlay = 15,
	Multiline = 16,
	ShowResetButton = 17,
	SliderShowTickMarks = 18,
	SliderShowTickMarksX = 19,
	SliderShowTickMarksY = 20,
	WindowTitle = 21,
	WindowWidthScale = 22,
	WindowIsSizable = 23,
	SliderSmallChangeZ = 24,
	SliderLargeChangeZ = 25,
	UpDownIncrementZ = 26,
	SliderShowTickMarksZ = 27,
	Footnote = 28,
	RangeWraps = 29,
	ControlStyle = 30,
	ControlColors = 31,
	WindowHelpContentType = 32,
	WindowHelpContent = 33,
	AllowAllFiles = 34,
	FileTypes = 35,
	ShowHeaderLine = 36,
	WindowShowBottomSeparatorLine = 37,
	ExponentialScale = 38,
}

public enum WindowHelpContentType
{
	None = 0,
	PlainText = 1,
	CustomViaCallback = 2,
}

public enum SliderControlStyle
{
	Default = 0,
	Hue = 1,
	HueCentered = 2,
	SaturationHue = 3,
}

/// <summary>
/// Describes the UI for a set of properties. The host turns it into real widgets.
/// </summary>
public abstract class ControlInfo : ICloneable
{
	private readonly List<ControlInfo> children = [];

	protected ControlInfo () { }

	public IReadOnlyList<ControlInfo> ChildControls => children;
	protected IList<ControlInfo> GetChildControlsCore () => children;

	/// <summary>Control settings keyed by <see cref="ControlInfoPropertyNames"/> name.</summary>
	public PropertyCollection ControlProperties { get; protected set; } = new ();

	internal Dictionary<string, object> Values { get; } = [];

	public static ControlInfo CreateDefaultConfigUI (IEnumerable<Property> properties)
	{
		PanelControlInfo panel = new ();
		foreach (Property p in properties)
			panel.AddChildControl (PropertyControlInfo.CreateFor (p));
		return panel;
	}

	public PropertyControlInfo FindControlForPropertyName (object propertyName)
	{
		string name = propertyName.ToString ();
		if (this is PropertyControlInfo pci && pci.Property.Name == name)
			return pci;
		foreach (ControlInfo child in children)
			if (child.FindControlForPropertyName (name) is PropertyControlInfo found)
				return found;
		return null;
	}

	public bool SetPropertyControlType (object propertyName, PropertyControlType newControlType)
	{
		PropertyControlInfo pci = FindControlForPropertyName (propertyName);
		if (pci is null) return false;
		pci.ControlType.Value = newControlType;
		return true;
	}

	public bool SetPropertyControlValue (object propertyName, object controlPropertyName, object propertyValue)
	{
		PropertyControlInfo pci = FindControlForPropertyName (propertyName);
		if (pci is null) return false;
		pci.Values[controlPropertyName.ToString ()] = propertyValue;
		return true;
	}

	public abstract ControlInfo Clone ();
	object ICloneable.Clone () => Clone ();

	protected void CopyTo (ControlInfo other)
	{
		foreach (ControlInfo c in children) other.children.Add (c.Clone ());
		foreach (var kv in Values) other.Values[kv.Key] = kv.Value;
	}
}

public sealed class PanelControlInfo : ControlInfo
{
	public PanelControlInfo () { }

	public TControlInfo AddChildControl<TControlInfo> (TControlInfo child) where TControlInfo : ControlInfo
	{
		GetChildControlsCore ().Add (child);
		return child;
	}

	public override ControlInfo Clone ()
	{
		PanelControlInfo p = new ();
		CopyTo (p);
		return p;
	}
}

public sealed class TabPageControlInfo : ControlInfo
{
	public TabPageControlInfo () { }
	public string Text { get; set; }
	public string ToolTipText { get; set; }

	public T AddChildControl<T> (T controlInfo) where T : ControlInfo
	{
		GetChildControlsCore ().Add (controlInfo);
		return controlInfo;
	}

	public void RemoveChildControl (ControlInfo controlInfo) => GetChildControlsCore ().Remove (controlInfo);
	public override ControlInfo Clone () { TabPageControlInfo p = new () { Text = Text, ToolTipText = ToolTipText }; CopyTo (p); return p; }
}

/// <summary>The selected tab of a <see cref="TabContainerControlInfo"/>.</summary>
public struct TabContainerState : IEquatable<TabContainerState>
{
	public TabContainerState (int selectedTabIndex) { SelectedTabIndex = selectedTabIndex; }
	public int SelectedTabIndex { readonly get; init; }
	public readonly bool Equals (TabContainerState other) => SelectedTabIndex == other.SelectedTabIndex;
	public override readonly bool Equals (object obj) => obj is TabContainerState s && Equals (s);
	public override readonly int GetHashCode () => SelectedTabIndex;
	public override readonly string ToString () => $"Tab {SelectedTabIndex}";
	public static bool operator == (TabContainerState left, TabContainerState right) => left.Equals (right);
	public static bool operator != (TabContainerState left, TabContainerState right) => !left.Equals (right);
}

/// <summary>A property that only stores UI state (such as the selected tab); it has no control of its own.</summary>
public abstract class ControlInfoStateProperty<TState> : Property<TState> where TState : struct
{
	protected ControlInfoStateProperty (object name, TState defaultValue) : base (name, defaultValue, false, ValueValidationFailureResult.Ignore) { }
	protected override TState OnClampNewValueT (TState newValue) => newValue;
}

public sealed class TabContainerStateProperty : ControlInfoStateProperty<TabContainerState>
{
	public TabContainerStateProperty (object name) : base (name, default) { }

	public override Property Clone ()
	{
		TabContainerStateProperty p = new (Name);
		p.Value = Value;
		return p;
	}
}

public abstract class StatefulControlInfo<TState, TStateProperty> : ControlInfo
	where TState : struct
	where TStateProperty : Property<TState>
{
	protected StatefulControlInfo (TStateProperty stateProperty) { StateProperty = stateProperty; }
	public TStateProperty StateProperty { get; set; }
}

/// <summary>Tabs (Paint.NET 5): each page holds controls.</summary>
public sealed class TabContainerControlInfo : StatefulControlInfo<TabContainerState, TabContainerStateProperty>
{
	public TabContainerControlInfo () : base (null) { }
	public TabContainerControlInfo (TabContainerStateProperty stateProperty) : base (stateProperty) { }
	/// <summary>The Paint.NET 5.0 signature.</summary>
	public TabContainerControlInfo (Property stateProperty) : base (stateProperty as TabContainerStateProperty) { }

	public IReadOnlyList<TabPageControlInfo> TabPages => ChildControls.OfType<TabPageControlInfo> ().ToList ();

	public TabPageControlInfo AddTab (TabPageControlInfo controlInfo)
	{
		GetChildControlsCore ().Add (controlInfo);
		return controlInfo;
	}

	public TabPageControlInfo InsertTab (int index, TabPageControlInfo controlInfo)
	{
		GetChildControlsCore ().Insert (index, controlInfo);
		return controlInfo;
	}

	public void RemoveTab (TabPageControlInfo controlInfo) => GetChildControlsCore ().Remove (controlInfo);

	public override ControlInfo Clone () { TabContainerControlInfo p = new (StateProperty); CopyTo (p); return p; }
}

/// <summary>The UI for one property: its control type, captions and slider settings.</summary>
public sealed class PropertyControlInfo : ControlInfo
{
	private readonly Dictionary<object, string> value_display_names = [];

	private PropertyControlInfo (Property property, StaticListChoiceProperty controlType)
	{
		Property = property;
		ControlType = controlType;
	}

	public Property Property { get; }

	/// <summary>The chosen control type; its value is a <see cref="PropertyControlType"/>.</summary>
	public StaticListChoiceProperty ControlType { get; }

	public static PropertyControlInfo CreateFor (Property property)
	{
		// Any type is accepted (the dialog falls back sensibly); the property's natural type is the default.
		PropertyControlType[] valid = ValidTypesFor (property);
		object[] choices = valid.Concat (Enum.GetValues<PropertyControlType> ().Except (valid)).Cast<object> ().ToArray ();
		StaticListChoiceProperty controlType = new (ControlInfoPropertyNames.ControlType, choices, 0);
		PropertyControlInfo pci = new (property, controlType);
		pci.Values[nameof (ControlInfoPropertyNames.DisplayName)] = property.Name;
		return pci;
	}

	/// <summary>Valid control types for each kind of property; the first is the default.</summary>
	private static PropertyControlType[] ValidTypesFor (Property p) => p switch {
		Int32Property => [PropertyControlType.Slider, PropertyControlType.ColorWheel, PropertyControlType.IncrementButton, PropertyControlType.Label, PropertyControlType.TextBox, PropertyControlType.Null],
		DoubleProperty => [PropertyControlType.Slider, PropertyControlType.AngleChooser, PropertyControlType.Label, PropertyControlType.Null],
		BooleanProperty => [PropertyControlType.CheckBox, PropertyControlType.Label, PropertyControlType.Null],
		StringProperty => [PropertyControlType.TextBox, PropertyControlType.FileChooser, PropertyControlType.FolderChooser, PropertyControlType.Label, PropertyControlType.Null],
		StaticListChoiceProperty => [PropertyControlType.DropDown, PropertyControlType.RadioButton, PropertyControlType.Label, PropertyControlType.Null],
		DoubleVectorProperty => [PropertyControlType.PanAndSlider, PropertyControlType.Slider, PropertyControlType.Null],
		DoubleVector3Property => [PropertyControlType.RollBallAndSliders, PropertyControlType.Slider, PropertyControlType.Null],
		UriProperty => [PropertyControlType.LinkLabel, PropertyControlType.Null],
		TabContainerStateProperty => [PropertyControlType.Null],
		_ => [PropertyControlType.Label, PropertyControlType.Null],
	};

	public void SetValueDisplayName (object value, string displayName) => value_display_names[value] = displayName;

	public string GetValueDisplayName (object value)
		=> value_display_names.TryGetValue (value, out string name) ? name : value?.ToString () ?? string.Empty;

	/// <summary>Reads a control setting such as <see cref="ControlInfoPropertyNames.DecimalPlaces"/>.</summary>
	public object GetControlValue (ControlInfoPropertyNames name) => Values.TryGetValue (name.ToString (), out object v) ? v : null;

	public T GetControlValue<T> (ControlInfoPropertyNames name, T fallback)
	{
		object v = GetControlValue (name);
		if (v is T t) return t;
		try {
			return v is null ? fallback : (T) Convert.ChangeType (v, typeof (T));
		} catch (Exception) {
			return fallback;
		}
	}

	public override ControlInfo Clone ()
	{
		PropertyControlInfo p = new (Property, (StaticListChoiceProperty) ControlType.Clone ());
		CopyTo (p);
		foreach (var kv in value_display_names) p.value_display_names[kv.Key] = kv.Value;
		return p;
	}
}
