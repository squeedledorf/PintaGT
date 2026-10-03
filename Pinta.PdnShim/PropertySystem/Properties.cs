using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PaintDotNet.Rendering;

namespace PaintDotNet
{
	public delegate void ValueEventHandler<T> (object sender, ValueEventArgs<T> e);

	public sealed class ValueEventArgs<T> : EventArgs
	{
		public ValueEventArgs (T value) { Value = value; }
		public T Value { get; }
		public static ValueEventArgs<T> Create (T value) => new (value);
	}
}

namespace PaintDotNet.PropertySystem
{
	public enum ValueValidationFailureResult
	{
		Ignore = 0,
		Clamp = 1,
		ThrowException = 2,
	}

	/// <summary>A named, typed value with optional read-only state, as used by IndirectUI.</summary>
	public abstract class Property : ICloneable, INotifyPropertyChanged
	{
		private object value;
		private bool read_only;

		protected Property (object name, Type valueType, object defaultValue, bool readOnly, ValueValidationFailureResult vvfResult)
		{
			Name = name?.ToString () ?? throw new ArgumentNullException (nameof (name));
			ValueType = valueType;
			DefaultValue = defaultValue;
			value = defaultValue;
			read_only = readOnly;
			ValueValidationFailureResult = vvfResult;
		}

		public string Name { get; }
		public Type ValueType { get; }
		public object DefaultValue { get; }
		public ValueValidationFailureResult ValueValidationFailureResult { get; }
		public static ValueValidationFailureResult DefaultValueValidationFailureResult => ValueValidationFailureResult.Clamp;
		protected object Sync { get; } = new ();

		public object Value {
			get => value;
			set {
				VerifyNotReadOnly ();
				SetValueCore (value);
			}
		}

		/// <summary>Sets the value even when the property is read-only (used by rules and the host).</summary>
		internal void SetValueCore (object newValue)
		{
			newValue = OnCoerceValue (newValue);
			if (!ValidateNewValue (newValue)) {
				switch (ValueValidationFailureResult) {
					case ValueValidationFailureResult.Ignore:
						return;
					case ValueValidationFailureResult.ThrowException:
						throw new ArgumentOutOfRangeException (nameof (newValue), $"{newValue} is not valid for {Name}");
					default:
						newValue = OnClampNewValue (newValue);
						break;
				}
			}
			if (Equals (value, newValue))
				return;
			value = newValue;
			OnValueChanged (newValue);
			ValueChanged?.Invoke (this, new ValueEventArgs<object> (newValue));
			OnPropertyChanged (nameof (Value));
		}

		public bool ReadOnly {
			get => read_only;
			set {
				if (read_only == value) return;
				read_only = value;
				OnReadOnlyChanged (value);
				ReadOnlyChanged?.Invoke (this, new ValueEventArgs<bool> (value));
				OnPropertyChanged (nameof (ReadOnly));
			}
		}

		public event ValueEventHandler<object> ValueChanged;
		public event ValueEventHandler<bool> ReadOnlyChanged;
		public event PropertyChangedEventHandler PropertyChanged;

		protected void VerifyNotReadOnly ()
		{
			if (read_only) throw new ReadOnlyException ($"Property {Name} is read-only");
		}

		protected void OnPropertyChanged (string propertyName) => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
		protected virtual void OnReadOnlyChanged (bool newReadOnlyValue) { }
		protected virtual void OnValueChanged (object newValue) { }
		protected virtual object OnCoerceValue (object newValue) => newValue;
		protected virtual bool ValidateNewValue (object newValue) => true;
		protected abstract object OnClampNewValue (object newValue);
		protected virtual string PropertyValueToString (object value) => value?.ToString () ?? string.Empty;

		public abstract Property Clone ();
		object ICloneable.Clone () => Clone ();

		/// <summary>Copies the value of <paramref name="source"/> without raising read-only errors.</summary>
		internal void CopyValueFrom (Property source) => SetValueCore (source.Value);

		public static Property Create (Type valueType, object name) => Create (valueType, name, valueType.IsValueType ? Activator.CreateInstance (valueType) : null);

		public static Property Create (Type valueType, object name, object defaultValue)
		{
			if (valueType == typeof (int)) return new Int32Property (name, (int) defaultValue);
			if (valueType == typeof (double)) return new DoubleProperty (name, (double) defaultValue);
			if (valueType == typeof (bool)) return new BooleanProperty (name, (bool) defaultValue);
			if (valueType == typeof (string)) return new StringProperty (name, (string) defaultValue ?? string.Empty);
			if (valueType.IsEnum) return StaticListChoiceProperty.CreateForEnum (valueType, name, defaultValue);
			throw new ArgumentException ($"No default property type for {valueType}");
		}

		public override string ToString () => $"{Name} = {PropertyValueToString (Value)}";
	}

	public sealed class ReadOnlyException : InvalidOperationException
	{
		public ReadOnlyException (string message) : base (message) { }
	}

	public abstract class Property<T> : Property
	{
		protected Property (object name, T defaultValue, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, typeof (T), defaultValue, readOnly, vvfResult)
		{
			base.ValueChanged += (s, e) => ValueChanged?.Invoke (this, new ValueEventArgs<T> ((T) e.Value));
		}

		public new T DefaultValue => (T) base.DefaultValue;

		public new T Value {
			get => (T) base.Value;
			set => base.Value = value;
		}

		public new event ValueEventHandler<T> ValueChanged;

		protected abstract T OnClampNewValueT (T newValue);
		protected sealed override object OnClampNewValue (object newValue) => OnClampNewValueT ((T) newValue);
		protected virtual T OnCoerceValueT (object newValue) => newValue is T t ? t : (T) Convert.ChangeType (newValue, typeof (T));
		protected sealed override object OnCoerceValue (object newValue) => OnCoerceValueT (newValue);
		protected virtual void OnValueChangedT (T newValue) { }
		protected sealed override void OnValueChanged (object newValue) => OnValueChangedT ((T) newValue);
		protected virtual string PropertyValueToStringT (T value) => value?.ToString () ?? string.Empty;
		protected sealed override string PropertyValueToString (object value) => PropertyValueToStringT ((T) value);
		protected virtual bool ValidateNewValueT (T newValue) => true;
		protected sealed override bool ValidateNewValue (object newValue) => ValidateNewValueT ((T) newValue);
	}

	public abstract class ScalarProperty<T> : Property<T> where T : struct, IComparable<T>
	{
		protected ScalarProperty (object name, T defaultValue, T minValue, T maxValue, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValue, readOnly, vvfResult)
		{
			if (minValue.CompareTo (maxValue) > 0) throw new ArgumentOutOfRangeException (nameof (minValue), "minValue > maxValue");
			MinValue = minValue;
			MaxValue = maxValue;
		}

		public T MinValue { get; }
		public T MaxValue { get; }

		public static T Clamp (T value, T min, T max) => value.CompareTo (min) < 0 ? min : value.CompareTo (max) > 0 ? max : value;
		public T ClampPotentialValue (T newValue) => Clamp (newValue, MinValue, MaxValue);
		protected override T OnClampNewValueT (T newValue) => ClampPotentialValue (newValue);
		protected override bool ValidateNewValueT (T newValue) => newValue.CompareTo (MinValue) >= 0 && newValue.CompareTo (MaxValue) <= 0;

		public bool IsEqualTo (ScalarProperty<T> rhs) => IsEqualTo (this, rhs);
		public static bool IsEqualTo (ScalarProperty<T> lhs, ScalarProperty<T> rhs) => IsEqualTo (lhs.Value, rhs.Value);
		public static bool IsEqualTo (T lhs, T rhs) => lhs.CompareTo (rhs) == 0;
		public bool IsGreaterThan (ScalarProperty<T> rhs) => IsGreaterThan (this, rhs);
		public static bool IsGreaterThan (ScalarProperty<T> lhs, ScalarProperty<T> rhs) => IsGreaterThan (lhs.Value, rhs.Value);
		public static bool IsGreaterThan (T lhs, T rhs) => lhs.CompareTo (rhs) > 0;
		public bool IsLessThan (ScalarProperty<T> rhs) => IsLessThan (this, rhs);
		public static bool IsLessThan (ScalarProperty<T> lhs, ScalarProperty<T> rhs) => IsLessThan (lhs.Value, rhs.Value);
		public static bool IsLessThan (T lhs, T rhs) => lhs.CompareTo (rhs) < 0;
	}

	public sealed class Int32Property : ScalarProperty<int>
	{
		public Int32Property (object name) : this (name, 0) { }
		public Int32Property (object name, int defaultValue) : this (name, defaultValue, int.MinValue, int.MaxValue) { }
		public Int32Property (object name, int defaultValue, int minValue, int maxValue) : this (name, defaultValue, minValue, maxValue, false) { }
		public Int32Property (object name, int defaultValue, int minValue, int maxValue, bool readOnly) : this (name, defaultValue, minValue, maxValue, readOnly, DefaultValueValidationFailureResult) { }
		public Int32Property (object name, int defaultValue, int minValue, int maxValue, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValue, minValue, maxValue, readOnly, vvfResult) { }

		protected override int OnCoerceValueT (object newValue) => newValue is int i ? i : Convert.ToInt32 (newValue);
		public override Property Clone () => Copy (new Int32Property (Name, DefaultValue, MinValue, MaxValue, ReadOnly, ValueValidationFailureResult));

		private Property Copy (Property p) { p.CopyValueFrom (this); return p; }
	}

	public sealed class DoubleProperty : ScalarProperty<double>
	{
		public DoubleProperty (object name) : this (name, 0) { }
		public DoubleProperty (object name, double defaultValue) : this (name, defaultValue, double.MinValue, double.MaxValue) { }
		public DoubleProperty (object name, double defaultValue, double minValue, double maxValue) : this (name, defaultValue, minValue, maxValue, false) { }
		public DoubleProperty (object name, double defaultValue, double minValue, double maxValue, bool readOnly) : this (name, defaultValue, minValue, maxValue, readOnly, DefaultValueValidationFailureResult) { }
		public DoubleProperty (object name, double defaultValue, double minValue, double maxValue, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValue, minValue, maxValue, readOnly, vvfResult) { }

		protected override double OnCoerceValueT (object newValue) => newValue is double d ? d : Convert.ToDouble (newValue);

		public override Property Clone ()
		{
			DoubleProperty p = new (Name, DefaultValue, MinValue, MaxValue, ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	public sealed class BooleanProperty : ScalarProperty<bool>
	{
		public BooleanProperty (object name) : this (name, false) { }
		public BooleanProperty (object name, bool defaultValue) : this (name, defaultValue, false) { }
		public BooleanProperty (object name, bool defaultValue, bool readOnly) : this (name, defaultValue, readOnly, DefaultValueValidationFailureResult) { }
		public BooleanProperty (object name, bool defaultValue, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValue, false, true, readOnly, vvfResult) { }

		protected override bool OnCoerceValueT (object newValue) => newValue is bool b ? b : Convert.ToBoolean (newValue);

		public override Property Clone ()
		{
			BooleanProperty p = new (Name, DefaultValue, ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	public sealed class StringProperty : Property<string>
	{
		public StringProperty (object name) : this (name, string.Empty) { }
		public StringProperty (object name, string defaultValue) : this (name, defaultValue, MaxMaxLength) { }
		public StringProperty (object name, string defaultValue, int maxLength) : this (name, defaultValue, maxLength, false) { }
		public StringProperty (object name, string defaultValue, int maxLength, bool readOnly) : this (name, defaultValue, maxLength, readOnly, DefaultValueValidationFailureResult) { }
		public StringProperty (object name, string defaultValue, int maxLength, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValue ?? string.Empty, readOnly, vvfResult)
		{
			MaxLength = maxLength;
		}

		public int MaxLength { get; }
		public static int MaxMaxLength => 32767;

		protected override string OnCoerceValueT (object newValue) => newValue?.ToString () ?? string.Empty;
		protected override string OnClampNewValueT (string newValue) => newValue.Length > MaxLength ? newValue[..MaxLength] : newValue;
		protected override bool ValidateNewValueT (string newValue) => newValue.Length <= MaxLength;
		protected override string PropertyValueToStringT (string value) => value;

		public override Property Clone ()
		{
			StringProperty p = new (Name, DefaultValue, MaxLength, ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	public sealed class StaticListChoiceProperty : Property<object>
	{
		public StaticListChoiceProperty (object name, object[] valueChoices) : this (name, valueChoices, 0) { }
		public StaticListChoiceProperty (object name, object[] valueChoices, int defaultChoiceIndex) : this (name, valueChoices, defaultChoiceIndex, false) { }
		public StaticListChoiceProperty (object name, object[] valueChoices, int defaultChoiceIndex, bool readOnly) : this (name, valueChoices, defaultChoiceIndex, readOnly, DefaultValueValidationFailureResult) { }
		public StaticListChoiceProperty (object name, object[] valueChoices, int defaultChoiceIndex, bool readOnly, ValueValidationFailureResult vvfResult)
			// An out-of-range default (e.g. a font that is not installed here) falls back to the first choice.
			: base (name, valueChoices[Math.Clamp (defaultChoiceIndex, 0, Math.Max (0, valueChoices.Length - 1))], readOnly, vvfResult)
		{
			ValueChoices = (object[]) valueChoices.Clone ();
		}

		public object[] ValueChoices { get; }

		public static StaticListChoiceProperty CreateForEnum<TEnum> (object name, TEnum defaultValue) where TEnum : Enum
			=> CreateForEnum (typeof (TEnum), name, defaultValue, false);

		public static StaticListChoiceProperty CreateForEnum<TEnum> (object name, TEnum defaultValue, bool readOnly) where TEnum : Enum
			=> CreateForEnum (typeof (TEnum), name, defaultValue, readOnly);

		public static StaticListChoiceProperty CreateForEnum (Type enumType, object name, object defaultValue)
			=> CreateForEnum (enumType, name, defaultValue, false);

		public static StaticListChoiceProperty CreateForEnum (Type enumType, object name, object defaultValue, bool readOnly)
		{
			object[] choices = Enum.GetValues (enumType).Cast<object> ().ToArray ();
			int index = Math.Max (0, Array.IndexOf (choices, defaultValue));
			return new StaticListChoiceProperty (name, choices, index, readOnly);
		}

		protected override object OnCoerceValueT (object newValue) => newValue;
		protected override object OnClampNewValueT (object newValue) => ValueChoices.Contains (newValue) ? newValue : DefaultValue;
		protected override bool ValidateNewValueT (object newValue) => ValueChoices.Contains (newValue);

		public override Property Clone ()
		{
			StaticListChoiceProperty p = new (Name, ValueChoices, Math.Max (0, Array.IndexOf (ValueChoices, DefaultValue)), ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	public abstract class VectorProperty<T> : Property<Pair<T, T>> where T : struct, IComparable<T>
	{
		protected VectorProperty (object name, Pair<T, T> defaultValues, Pair<T, T> minValues, Pair<T, T> maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValues, readOnly, vvfResult)
		{
			MinValues = minValues;
			MaxValues = maxValues;
		}

		public Pair<T, T> MinValues { get; }
		public Pair<T, T> MaxValues { get; }
		public T MinValueX => MinValues.First;
		public T MinValueY => MinValues.Second;
		public T MaxValueX => MaxValues.First;
		public T MaxValueY => MaxValues.Second;
		public T DefaultValueX => DefaultValue.First;
		public T DefaultValueY => DefaultValue.Second;

		public T ValueX {
			get => Value.First;
			set => Value = new Pair<T, T> (value, ValueY);
		}

		public T ValueY {
			get => Value.Second;
			set => Value = new Pair<T, T> (ValueX, value);
		}

		public T ClampPotentialValueX (T newValue) => ScalarClamp (newValue, MinValueX, MaxValueX);
		public T ClampPotentialValueY (T newValue) => ScalarClamp (newValue, MinValueY, MaxValueY);
		public Pair<T, T> ClampPotentialValue (Pair<T, T> newValue) => new (ClampPotentialValueX (newValue.First), ClampPotentialValueY (newValue.Second));
		protected override Pair<T, T> OnClampNewValueT (Pair<T, T> newValue) => ClampPotentialValue (newValue);
		protected override bool ValidateNewValueT (Pair<T, T> newValue) => ClampPotentialValue (newValue).Equals (newValue);

		public bool IsEqualTo (VectorProperty<T> rhs) => IsEqualTo (this, rhs);
		public static bool IsEqualTo (VectorProperty<T> lhs, VectorProperty<T> rhs) => IsEqualTo (lhs.Value, rhs.Value);
		public static bool IsEqualTo (Pair<T, T> lhs, Pair<T, T> rhs) => lhs.Equals (rhs);

		private static T ScalarClamp (T v, T min, T max) => v.CompareTo (min) < 0 ? min : v.CompareTo (max) > 0 ? max : v;
	}

	public abstract class Vector2DoubleAsPairProperty : VectorProperty<double>
	{
		protected Vector2DoubleAsPairProperty (object name, Vector2Double defaultValues, Vector2Double minValues, Vector2Double maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValues, minValues, maxValues, readOnly, vvfResult) { }

		public new Vector2Double MinValues => base.MinValues;
		public new Vector2Double MaxValues => base.MaxValues;

		public new Vector2Double Value {
			get => base.Value;
			set => base.Value = value;
		}

		public Vector2Double ClampPotentialValue (Vector2Double newValue) => base.ClampPotentialValue (newValue);

		protected override Pair<double, double> OnCoerceValueT (object newValue) => newValue switch {
			Pair<double, double> p => p,
			Vector2Double v => v,
			Tuple<double, double> t => t,
			_ => (Pair<double, double>) newValue,
		};
	}

	public sealed class DoubleVectorProperty : Vector2DoubleAsPairProperty
	{
		public DoubleVectorProperty (object name) : this (name, default (Vector2Double)) { }
		public DoubleVectorProperty (object name, Vector2Double defaultValues) : this (name, defaultValues, new Vector2Double (double.MinValue, double.MinValue), new Vector2Double (double.MaxValue, double.MaxValue)) { }
		public DoubleVectorProperty (object name, Vector2Double defaultValues, Vector2Double minValues, Vector2Double maxValues) : this (name, defaultValues, minValues, maxValues, false) { }
		public DoubleVectorProperty (object name, Vector2Double defaultValues, Vector2Double minValues, Vector2Double maxValues, bool readOnly) : this (name, defaultValues, minValues, maxValues, readOnly, DefaultValueValidationFailureResult) { }
		public DoubleVectorProperty (object name, Vector2Double defaultValues, Vector2Double minValues, Vector2Double maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValues, minValues, maxValues, readOnly, vvfResult) { }

		// Paint.NET 3 and 4 signatures.
		public DoubleVectorProperty (object name, Pair<double, double> defaultValues, Pair<double, double> minValues, Pair<double, double> maxValues)
			: this (name, (Vector2Double) defaultValues, (Vector2Double) minValues, (Vector2Double) maxValues) { }
		public DoubleVectorProperty (object name, Pair<double, double> defaultValues, Pair<double, double> minValues, Pair<double, double> maxValues, bool readOnly)
			: this (name, (Vector2Double) defaultValues, (Vector2Double) minValues, (Vector2Double) maxValues, readOnly) { }
		public DoubleVectorProperty (object name, Pair<double, double> defaultValues, Pair<double, double> minValues, Pair<double, double> maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: this (name, (Vector2Double) defaultValues, (Vector2Double) minValues, (Vector2Double) maxValues, readOnly, vvfResult) { }

		public override Property Clone ()
		{
			DoubleVectorProperty p = new (Name, (Vector2Double) DefaultValue, base.MinValues, base.MaxValues, ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	public abstract class Vector3DoubleAsTupleProperty : Property<Tuple<double, double, double>>
	{
		protected Vector3DoubleAsTupleProperty (object name, Vector3Double defaultValues, Vector3Double minValues, Vector3Double maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValues, readOnly, vvfResult)
		{
			MinValues = minValues;
			MaxValues = maxValues;
		}

		public Vector3Double MinValues { get; }
		public Vector3Double MaxValues { get; }
		public double MinValueX => MinValues.X;
		public double MinValueY => MinValues.Y;
		public double MinValueZ => MinValues.Z;
		public double MaxValueX => MaxValues.X;
		public double MaxValueY => MaxValues.Y;
		public double MaxValueZ => MaxValues.Z;
		public double ValueX => Value.Item1;
		public double ValueY => Value.Item2;
		public double ValueZ => Value.Item3;

		protected override Tuple<double, double, double> OnCoerceValueT (object newValue) => newValue switch {
			Vector3Double v => v,
			_ => (Tuple<double, double, double>) newValue,
		};

		protected override Tuple<double, double, double> OnClampNewValueT (Tuple<double, double, double> v)
			=> Tuple.Create (Math.Clamp (v.Item1, MinValueX, MaxValueX), Math.Clamp (v.Item2, MinValueY, MaxValueY), Math.Clamp (v.Item3, MinValueZ, MaxValueZ));

		protected override bool ValidateNewValueT (Tuple<double, double, double> v) => OnClampNewValueT (v).Equals (v);
	}

	public sealed class DoubleVector3Property : Vector3DoubleAsTupleProperty
	{
		public DoubleVector3Property (object name) : this (name, default (Vector3Double)) { }
		public DoubleVector3Property (object name, Vector3Double defaultValues) : this (name, defaultValues, new Vector3Double (double.MinValue, double.MinValue, double.MinValue), new Vector3Double (double.MaxValue, double.MaxValue, double.MaxValue)) { }
		public DoubleVector3Property (object name, Vector3Double defaultValues, Vector3Double minValues, Vector3Double maxValues) : this (name, defaultValues, minValues, maxValues, false) { }
		public DoubleVector3Property (object name, Vector3Double defaultValues, Vector3Double minValues, Vector3Double maxValues, bool readOnly) : this (name, defaultValues, minValues, maxValues, readOnly, DefaultValueValidationFailureResult) { }
		public DoubleVector3Property (object name, Vector3Double defaultValues, Vector3Double minValues, Vector3Double maxValues, bool readOnly, ValueValidationFailureResult vvfResult)
			: base (name, defaultValues, minValues, maxValues, readOnly, vvfResult) { }

		// Paint.NET 3 and 4 signatures.
		public DoubleVector3Property (object name, Tuple<double, double, double> defaultValues, Tuple<double, double, double> minValues, Tuple<double, double, double> maxValues)
			: this (name, (Vector3Double) defaultValues, (Vector3Double) minValues, (Vector3Double) maxValues) { }

		public override Property Clone ()
		{
			DoubleVector3Property p = new (Name, (Vector3Double) DefaultValue, MinValues, MaxValues, ReadOnly, ValueValidationFailureResult);
			p.CopyValueFrom (this);
			return p;
		}
	}

	/// <summary>A property holding an image (IndirectUI shows it read-only).</summary>
	public sealed class ImageProperty : Property<ImageResource>
	{
		public ImageProperty (object name) : this (name, null) { }
		public ImageProperty (object name, ImageResource defaultValue) : this (name, defaultValue, false) { }
		public ImageProperty (object name, ImageResource defaultValue, bool readOnly) : base (name, defaultValue, readOnly, ValueValidationFailureResult.Ignore) { }
		protected override ImageResource OnClampNewValueT (ImageResource newValue) => newValue;
		protected override ImageResource OnCoerceValueT (object newValue) => (ImageResource) newValue;
		public override Property Clone () { ImageProperty p = new (Name, DefaultValue, ReadOnly); p.CopyValueFrom (this); return p; }
	}

	public sealed class UriProperty : Property<Uri>
	{
		public UriProperty (object name) : this (name, null) { }
		public UriProperty (object name, Uri defaultValue) : this (name, defaultValue, false) { }
		public UriProperty (object name, Uri defaultValue, bool readOnly) : base (name, defaultValue, readOnly, ValueValidationFailureResult.Ignore) { }
		protected override Uri OnClampNewValueT (Uri newValue) => newValue;
		protected override Uri OnCoerceValueT (object newValue) => newValue as Uri ?? (newValue is string s ? new Uri (s) : null);
		public override Property Clone () { UriProperty p = new (Name, DefaultValue, ReadOnly); p.CopyValueFrom (this); return p; }
	}

	/// <summary>An ordered collection of properties plus the rules that tie them together.</summary>
	public sealed class PropertyCollection : INotifyPropertyChanged, IEnumerable<Property>, ICloneable
	{
		private readonly List<Property> properties = [];
		private readonly Dictionary<string, Property> by_name = [];
		private readonly List<PropertyCollectionRule> rules = [];

		public PropertyCollection () { }

		public PropertyCollection (IEnumerable<Property> properties) : this (properties, []) { }

		public PropertyCollection (IEnumerable<Property> properties, IEnumerable<PropertyCollectionRule> rules)
		{
			foreach (Property p in properties) {
				Property copy = p.Clone ();
				this.properties.Add (copy);
				by_name.Add (copy.Name, copy);
				copy.PropertyChanged += (s, e) => PropertyChanged?.Invoke (s, e);
			}
			foreach (PropertyCollectionRule rule in rules) {
				PropertyCollectionRule copy = rule.Clone ();
				this.rules.Add (copy);
				copy.Initialize (this);
			}
		}

		public static PropertyCollection CreateEmpty () => new ();

		public static PropertyCollection CreateMerged (PropertyCollection pc1, PropertyCollection pc2)
			=> new (pc1.properties.Concat (pc2.properties), pc1.rules.Concat (pc2.rules));

		public int Count => properties.Count;
		public IEnumerable<Property> Properties => properties;
		public IEnumerable<string> PropertyNames => properties.Select (p => p.Name);
		public IEnumerable<PropertyCollectionRule> Rules => rules;

		public Property this[object propertyName] => by_name.TryGetValue (propertyName.ToString (), out Property p) ? p : null;

		public bool TryGetProperty (object propertyName, out Property property)
		{
			property = this[propertyName];
			return property is not null;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public PropertyCollection Clone () => new (properties, rules);
		object ICloneable.Clone () => Clone ();

		public void CopyCompatibleValuesFrom (PropertyCollection srcProps) => CopyCompatibleValuesFrom (srcProps, false);

		public void CopyCompatibleValuesFrom (PropertyCollection srcProps, bool ignoreReadOnlyFlags)
		{
			foreach (Property src in srcProps) {
				Property dst = this[src.Name];
				if (dst is null || dst.ValueType != src.ValueType) continue;
				if (dst.ReadOnly && !ignoreReadOnlyFlags) continue;
				try {
					dst.SetValueCore (src.Value);
				} catch (Exception) {
				}
			}
		}

		public IEnumerator<Property> GetEnumerator () => properties.GetEnumerator ();
		IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();
	}
}
