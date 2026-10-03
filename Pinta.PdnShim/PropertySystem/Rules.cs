using System;
using System.Collections.Generic;
using System.Linq;

namespace PaintDotNet.PropertySystem;

/// <summary>
/// A rule that keeps properties of a <see cref="PropertyCollection"/> consistent, for example by
/// making one property read-only depending on another. Rules are cloned into each collection.
/// </summary>
public abstract class PropertyCollectionRule : ICloneable
{
	protected PropertyCollectionRule () { }

	protected PropertyCollection Owner { get; private set; }

	internal void Initialize (PropertyCollection owner)
	{
		Owner = owner;
		OnInitialized ();
	}

	protected abstract void OnInitialized ();
	public abstract PropertyCollectionRule Clone ();
	object ICloneable.Clone () => Clone ();
}

/// <summary>Makes the target read-only while the boolean source is true (or false when inverse).</summary>
public sealed class ReadOnlyBoundToBooleanRule : PropertyCollectionRule
{
	private readonly string target_name;
	private readonly string source_name;
	private readonly bool inverse;

	public ReadOnlyBoundToBooleanRule (Property targetProperty, BooleanProperty sourceProperty, bool inverse)
		: this (targetProperty.Name, sourceProperty.Name, inverse) { }

	public ReadOnlyBoundToBooleanRule (object targetPropertyName, object sourceBooleanPropertyName, bool inverse)
	{
		target_name = targetPropertyName.ToString ();
		source_name = sourceBooleanPropertyName.ToString ();
		this.inverse = inverse;
	}

	protected override void OnInitialized ()
	{
		Property source = Owner[source_name];
		Property target = Owner[target_name];
		if (source is null || target is null) return;
		void Sync () => target.ReadOnly = (bool) source.Value ^ inverse;
		source.ValueChanged += (_, _) => Sync ();
		Sync ();
	}

	public override PropertyCollectionRule Clone () => new ReadOnlyBoundToBooleanRule (target_name, source_name, inverse);
}

/// <summary>Makes the target read-only while the source has one of the given values (or none of them when inverse).</summary>
public sealed class ReadOnlyBoundToValueRule<TValue, TProperty> : PropertyCollectionRule where TProperty : Property<TValue>
{
	private readonly string target_name;
	private readonly string source_name;
	private readonly TValue[] values;
	private readonly bool inverse;

	public ReadOnlyBoundToValueRule (Property targetProperty, TProperty sourceProperty, TValue valueForReadOnly, bool inverse = false)
		: this (targetProperty.Name, sourceProperty.Name, [valueForReadOnly], inverse) { }

	public ReadOnlyBoundToValueRule (Property targetProperty, TProperty sourceProperty, TValue[] valuesForReadOnly, bool inverse = false)
		: this (targetProperty.Name, sourceProperty.Name, valuesForReadOnly, inverse) { }

	public ReadOnlyBoundToValueRule (object targetPropertyName, object sourcePropertyName, TValue valueForReadOnly, bool inverse = false)
		: this (targetPropertyName, sourcePropertyName, [valueForReadOnly], inverse) { }

	public ReadOnlyBoundToValueRule (object targetPropertyName, object sourcePropertyName, TValue[] valuesForReadOnly, bool inverse = false)
	{
		target_name = targetPropertyName.ToString ();
		source_name = sourcePropertyName.ToString ();
		values = (TValue[]) valuesForReadOnly.Clone ();
		this.inverse = inverse;
	}

	protected override void OnInitialized ()
	{
		Property source = Owner[source_name];
		Property target = Owner[target_name];
		if (source is null || target is null) return;
		void Sync () => target.ReadOnly = values.Any (v => Equals (v, source.Value)) ^ inverse;
		source.ValueChanged += (_, _) => Sync ();
		Sync ();
	}

	public override PropertyCollectionRule Clone () => new ReadOnlyBoundToValueRule<TValue, TProperty> (target_name, source_name, values, inverse);
}

/// <summary>Makes the target read-only while the source has one of the given values (by name/value pairs).</summary>
public sealed class ReadOnlyBoundToNameValuesRule : PropertyCollectionRule
{
	private readonly string target_name;
	private readonly bool inverse;
	private readonly (string Name, object Value)[] pairs;

	public ReadOnlyBoundToNameValuesRule (object targetPropertyName, bool inverse, params Pair<object, object>[] sourcePropertyNameValuePairs)
	{
		target_name = targetPropertyName.ToString ();
		this.inverse = inverse;
		pairs = sourcePropertyNameValuePairs.Select (p => (p.First.ToString (), p.Second)).ToArray ();
	}

	private ReadOnlyBoundToNameValuesRule (string target, bool inverse, (string, object)[] pairs)
	{
		target_name = target;
		this.inverse = inverse;
		this.pairs = pairs;
	}

	protected override void OnInitialized ()
	{
		Property target = Owner[target_name];
		if (target is null) return;
		void Sync () => target.ReadOnly = pairs.Any (p => Equals (Owner[p.Name]?.Value, p.Value)) ^ inverse;
		foreach (var (name, _) in pairs)
			if (Owner[name] is Property p) p.ValueChanged += (_, _) => Sync ();
		Sync ();
	}

	public override PropertyCollectionRule Clone () => new ReadOnlyBoundToNameValuesRule (target_name, inverse, pairs);
}

/// <summary>Keeps min &lt;= max: raising min above max pushes max up, and lowering max below min pushes min down.</summary>
public sealed class SoftMutuallyBoundMinMaxRule<TValue, TProperty> : PropertyCollectionRule
	where TValue : struct, IComparable<TValue>
	where TProperty : ScalarProperty<TValue>
{
	private readonly string min_name;
	private readonly string max_name;

	public SoftMutuallyBoundMinMaxRule (Property minProperty, Property maxProperty) : this (minProperty.Name, maxProperty.Name) { }

	public SoftMutuallyBoundMinMaxRule (object minPropertyName, object maxPropertyName)
	{
		min_name = minPropertyName.ToString ();
		max_name = maxPropertyName.ToString ();
	}

	protected override void OnInitialized ()
	{
		if (Owner[min_name] is not TProperty min || Owner[max_name] is not TProperty max) return;
		min.ValueChanged += (_, _) => { if (min.Value.CompareTo (max.Value) > 0) max.SetValueCore (min.Value); };
		max.ValueChanged += (_, _) => { if (max.Value.CompareTo (min.Value) < 0) min.SetValueCore (max.Value); };
	}

	public override PropertyCollectionRule Clone () => new SoftMutuallyBoundMinMaxRule<TValue, TProperty> (min_name, max_name);
}

/// <summary>While the boolean source is true (false when inverse), changing any target sets all targets to that value.</summary>
public sealed class LinkValuesBasedOnBooleanRule<TValue, TProperty> : PropertyCollectionRule
	where TValue : struct, IComparable<TValue>
	where TProperty : ScalarProperty<TValue>
{
	private readonly string[] target_names;
	private readonly string source_name;
	private readonly bool inverse;
	private bool syncing;

	public LinkValuesBasedOnBooleanRule (IEnumerable<TProperty> targetProperties, BooleanProperty sourceProperty, bool inverse)
		: this (targetProperties.Select (p => (object) p.Name).ToArray (), sourceProperty.Name, inverse) { }

	public LinkValuesBasedOnBooleanRule (object[] targetPropertyNames, object sourcePropertyName, bool inverse)
	{
		target_names = targetPropertyNames.Select (n => n.ToString ()).ToArray ();
		source_name = sourcePropertyName.ToString ();
		this.inverse = inverse;
	}

	protected override void OnInitialized ()
	{
		if (Owner[source_name] is not BooleanProperty source) return;
		TProperty[] targets = target_names.Select (n => Owner[n]).OfType<TProperty> ().ToArray ();
		bool Linked () => source.Value ^ inverse;
		foreach (TProperty t in targets) {
			t.ValueChanged += (_, _) => {
				if (syncing || !Linked ()) return;
				syncing = true;
				try {
					foreach (TProperty other in targets)
						if (other != t) other.SetValueCore (t.Value);
				} finally {
					syncing = false;
				}
			};
		}
		source.ValueChanged += (_, _) => {
			if (Linked () && targets.Length > 0)
				foreach (TProperty other in targets.Skip (1)) other.SetValueCore (targets[0].Value);
		};
	}

	public override PropertyCollectionRule Clone () => new LinkValuesBasedOnBooleanRule<TValue, TProperty> (target_names, source_name, inverse);
}

/// <summary>When the source takes any of the given values (none of them when inverse), the target is set to the target value.</summary>
public sealed class SetTargetWhenSourceEqualsAnyValueRule : PropertyCollectionRule
{
	private readonly string target_name;
	private readonly object target_value;
	private readonly string source_name;
	private readonly object[] source_values;
	private readonly bool inverse;

	public SetTargetWhenSourceEqualsAnyValueRule (Property targetProperty, object targetValue, Property sourceProperty, object[] sourceValues, bool inverse = false)
		: this (targetProperty.Name, targetValue, sourceProperty.Name, sourceValues, inverse) { }

	public SetTargetWhenSourceEqualsAnyValueRule (Property targetProperty, object targetValue, Property sourceProperty, object sourceValue, bool inverse = false)
		: this (targetProperty.Name, targetValue, sourceProperty.Name, [sourceValue], inverse) { }

	public SetTargetWhenSourceEqualsAnyValueRule (object targetPropertyName, object targetValue, object sourcePropertyName, object sourceValue, bool inverse = false)
		: this (targetPropertyName, targetValue, sourcePropertyName, [sourceValue], inverse) { }

	public SetTargetWhenSourceEqualsAnyValueRule (object targetPropertyName, object targetValue, object sourcePropertyName, object[] sourceValues, bool inverse = false)
	{
		target_name = targetPropertyName.ToString ();
		target_value = targetValue;
		source_name = sourcePropertyName.ToString ();
		source_values = sourceValues;
		this.inverse = inverse;
	}

	protected override void OnInitialized ()
	{
		Property source = Owner[source_name];
		Property target = Owner[target_name];
		if (source is null || target is null) return;
		source.ValueChanged += (_, _) => {
			if (source_values.Any (v => Equals (v, source.Value)) ^ inverse) target.SetValueCore (target_value);
		};
	}

	public override PropertyCollectionRule Clone () => new SetTargetWhenSourceEqualsAnyValueRule (target_name, target_value, source_name, source_values, inverse);
}
