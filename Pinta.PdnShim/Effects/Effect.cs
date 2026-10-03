using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using PaintDotNet.ComponentModel;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace PaintDotNet
{
	public record PluginOptions
	{
	}
}

namespace PaintDotNet.Effects
{
	[Flags]
	public enum EffectFlags : ulong
	{
		None = 0,
		Configurable = 1,
		/// <summary>Paint.NET 3/4 value of SingleThreaded.</summary>
		LegacySingleThreaded = 2,
		/// <summary>Paint.NET 3/4 value of SingleRenderCall.</summary>
		LegacySingleRenderCall = 4,
		FirstTileIsNotRenderedWithBarrier = 1UL << 60,
		ForceAliasedSelectionQuality = 1UL << 61,
		SingleThreaded = 1UL << 63,
	}

	public enum EffectRenderingSchedule : long
	{
		DefaultTilesForCpuRendering = -2,
		DefaultTilesForGpuRendering = -1,
		SmallHorizontalStrips = 0,
		None = 5,
		Tiles128x128 = 4103,
		Tiles256x256 = 4104,
		Tiles512x512 = 4105,
		Tiles1024x1024 = 4106,
		Tiles2048x2048 = 4107,
		Tiles4096x4096 = 4108,
	}

	public enum EffectCategory
	{
		Effect = 0,
		Adjustment = 1,
		DoNotDisplay = 2,
	}

	[AttributeUsage (AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class EffectCategoryAttribute : Attribute
	{
		public EffectCategoryAttribute (EffectCategory category) { Category = category; }
		public EffectCategory Category { get; }
	}

	public static class SubmenuNames
	{
		public static string Artistic => "Artistic";
		public static string Blurs => "Blurs";
		public static string Color => "Color";
		public static string Distort => "Distort";
		public static string Noise => "Noise";
		public static string Object => "Object";
		public static string Photo => "Photo";
		public static string Render => "Render";
		public static string Stylize => "Stylize";
	}

	public record EffectOptionsBase : PluginOptions
	{
		public bool IsConfigurable { get; set; }
	}

	public record EffectOptions : PluginOptions
	{
		public EffectOptions () { }
		public EffectFlags Flags { get; set; }
		public EffectRenderingSchedule RenderingSchedule { get; set; } = EffectRenderingSchedule.DefaultTilesForCpuRendering;
	}

	/// <summary>Parameters of one effect invocation, as a classic config dialog would produce.</summary>
	public abstract class EffectConfigToken : ICloneable
	{
		protected EffectConfigToken () { }
		protected EffectConfigToken (EffectConfigToken copyMe) { }
		public abstract object Clone ();
	}

	public sealed class PropertyBasedEffectConfigToken : EffectConfigToken
	{
		public PropertyBasedEffectConfigToken (PropertyCollection propertyCollection)
		{
			Properties = propertyCollection.Clone ();
		}

		private PropertyBasedEffectConfigToken (PropertyBasedEffectConfigToken copyMe) : base (copyMe)
		{
			Properties = copyMe.Properties.Clone ();
		}

		public PropertyCollection Properties { get; }
		public IEnumerable<string> PropertyNames => Properties.PropertyNames;

		public Property GetProperty (object propertyName) => Properties[propertyName];
		public T GetProperty<T> (object propertyName) where T : Property => (T) Properties[propertyName];

		public bool SetPropertyValue (object propertyName, object newValue)
		{
			Property p = Properties[propertyName];
			if (p is null) return false;
			p.SetValueCore (newValue);
			return true;
		}

		public override object Clone () => new PropertyBasedEffectConfigToken (this);
	}

	/// <summary>
	/// Placeholder for Paint.NET's WinForms config dialog base. Plugins that subclass it
	/// cannot be shown on Linux; the host reports them as unsupported.
	/// </summary>
	public class EffectConfigDialog : IDisposable
	{
		protected EffectConfigToken theEffectToken;
		public EffectConfigDialog () { }
		public Effect Effect { get; set; }
		public EffectConfigToken EffectToken { get => theEffectToken; set => theEffectToken = value; }
		public Surface EffectSourceSurface { get; set; }
		public PdnRegion Selection { get; set; }
		public IServiceProvider Services { get; set; }
		public event EventHandler EffectTokenChanged;
		public void FinishTokenUpdate () { InitTokenFromDialog (); EffectTokenChanged?.Invoke (this, EventArgs.Empty); }
		protected virtual void InitDialogFromToken (EffectConfigToken effectToken) { }
		protected void InitDialogFromToken () => InitDialogFromToken (theEffectToken);
		protected virtual void InitialInitToken () { }
		protected virtual void InitTokenFromDialog () { }
		protected virtual void OnEffectTokenChanged () { }
		public void Dispose () => Dispose (true);
		protected virtual void Dispose (bool disposing) { }
	}

	/// <summary>Colors, selection and source surface the host passes to a classic effect.</summary>
	public sealed class EffectEnvironmentParameters : RefTrackedObject
	{
		private readonly PdnRegion selection;

		internal EffectEnvironmentParameters (ColorBgra primaryColor, ColorBgra secondaryColor, float brushWidth, Surface sourceSurface, PdnRegion selection)
		{
			PrimaryColor = primaryColor;
			SecondaryColor = secondaryColor;
			BrushWidth = brushWidth;
			SourceSurface = sourceSurface;
			this.selection = selection;
		}

		public static EffectEnvironmentParameters DefaultParameters { get; } = new (ColorBgra.Black, ColorBgra.White, 2, null, null);

		public ColorBgra PrimaryColor { get; }
		public ColorBgra SecondaryColor { get; }
		public float BrushWidth { get; }
		public Surface SourceSurface { get; }

		public Rectangle SelectionBounds => selection?.GetBoundsInt () ?? SourceSurface?.Bounds ?? Rectangle.Empty;

		/// <summary>The selection, or <paramref name="boundingRect"/> when nothing is selected.</summary>
		public PdnRegion GetSelection (Rectangle boundingRect)
		{
			if (selection is null) return new PdnRegion (boundingRect);
			PdnRegion r = selection.Clone ();
			r.Intersect (boundingRect);
			return r;
		}

		public PdnRegion GetSelectionAsPdnRegion () => selection?.Clone () ?? new PdnRegion (SourceSurface?.Bounds ?? Rectangle.Empty);
		public IReadOnlyList<Rectangle> GetSelectionAsScans () => GetSelectionAsPdnRegion ().GetRegionScansReadOnlyInt ();
	}

	/// <summary>Name, submenu, category and options shared by classic effects.</summary>
	public abstract class ClassicEffectBase : RefTrackedObject
	{
		private protected ClassicEffectBase (string name, Image image, string subMenuName, EffectOptions options)
		{
			Name = name;
			Image = image;
			SubMenuName = subMenuName;
			Options = options ?? new EffectOptions ();
			Category = GetType ().GetCustomAttribute<EffectCategoryAttribute> (true)?.Category ?? EffectCategory.Effect;
		}

		public string Name { get; }
		public Image Image { get; }
		public string SubMenuName { get; }
		public EffectCategory Category { get; }
		public EffectOptions Options { get; }

		protected sealed override void Dispose (bool disposing)
		{
			OnDispose (disposing);
			base.Dispose (disposing);
		}

		protected virtual void OnDispose (bool disposing) { }
	}

	/// <summary>A classic (Paint.NET 3/4 style) effect: SetRenderInfo once, then Render from several threads.</summary>
	public abstract class Effect : ClassicEffectBase
	{
		private volatile bool cancel_requested;

		protected Effect (string name, Image image, EffectFlags flags) : this (name, image, null, flags) { }
		protected Effect (string name, Image image, string subMenuName, EffectFlags flags) : base (name, image, subMenuName, new EffectOptions { Flags = flags }) { }
		protected Effect (string name, Image image, string subMenuName, EffectOptions options) : base (name, image, subMenuName, options) { }

		public EffectEnvironmentParameters EnvironmentParameters { get; set; } = EffectEnvironmentParameters.DefaultParameters;
		public IServiceProvider Services { get; set; }

		/// <summary>Set by the host so cancellation reaches plugins that poll <see cref="IsCancelRequested"/>.</summary>
		internal Func<bool> HostCancelCheck { get; set; }

		public bool IsCancelRequested => cancel_requested || (HostCancelCheck?.Invoke () ?? false);
		public void SignalCancelRequest () => cancel_requested = true;

		public virtual EffectConfigDialog CreateConfigDialog () => null;

		public void SetRenderInfo (EffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
			=> OnSetRenderInfo (newToken, dstArgs, srcArgs);

		protected virtual void OnSetRenderInfo (EffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs) { }

		public abstract void Render (EffectConfigToken token, RenderArgs dstArgs, RenderArgs srcArgs, Rectangle[] rois, int startIndex, int length);

		public void Render (EffectConfigToken token, RenderArgs dstArgs, RenderArgs srcArgs, Rectangle[] rois)
			=> Render (token, dstArgs, srcArgs, rois, 0, rois.Length);
	}

	public abstract class Effect<TToken> : Effect where TToken : EffectConfigToken
	{
		protected Effect (string name, Image image, string subMenuName, EffectFlags flags) : base (name, image, subMenuName, flags) { }
		protected Effect (string name, Image image, string subMenuName, EffectOptions options) : base (name, image, subMenuName, options) { }

		protected RenderArgs DstArgs { get; private set; }
		protected RenderArgs SrcArgs { get; private set; }
		protected TToken Token { get; private set; }

		protected sealed override void OnSetRenderInfo (EffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
		{
			Token = (TToken) newToken;
			DstArgs = dstArgs;
			SrcArgs = srcArgs;
			OnSetRenderInfo ((TToken) newToken, dstArgs, srcArgs);
		}

		protected virtual void OnSetRenderInfo (TToken newToken, RenderArgs dstArgs, RenderArgs srcArgs) { }

		public void Render (Rectangle[] renderRects, int startIndex, int length)
		{
			if (!IsCancelRequested)
				OnRender (renderRects, startIndex, length);
		}

		public sealed override void Render (EffectConfigToken token, RenderArgs dstArgs, RenderArgs srcArgs, Rectangle[] rois, int startIndex, int length)
		{
			if (!ReferenceEquals (token, Token) || dstArgs != DstArgs || srcArgs != SrcArgs) {
				Token = (TToken) token;
				DstArgs = dstArgs;
				SrcArgs = srcArgs;
			}
			Render (rois, startIndex, length);
		}

		protected abstract void OnRender (Rectangle[] renderRects, int startIndex, int length);
	}

	/// <summary>An effect whose settings are a <see cref="PropertyCollection"/> shown through IndirectUI.</summary>
	public abstract class PropertyBasedEffect : Effect<PropertyBasedEffectConfigToken>
	{
		protected PropertyBasedEffect (string name, Image image, string subMenuName, EffectFlags flags) : base (name, image, subMenuName, flags) { }
		protected PropertyBasedEffect (string name, Image image, string subMenuName, EffectOptions options) : base (name, image, subMenuName, options) { }

		public sealed override EffectConfigDialog CreateConfigDialog () => null;

		public PropertyCollection CreatePropertyCollection () => OnCreatePropertyCollection ();
		public ControlInfo CreateConfigUI (PropertyCollection props) => OnCreateConfigUI (props);
		public static ControlInfo CreateDefaultConfigUI (IEnumerable<Property> props) => ControlInfo.CreateDefaultConfigUI (props);

		protected abstract PropertyCollection OnCreatePropertyCollection ();
		protected virtual ControlInfo OnCreateConfigUI (PropertyCollection props) => CreateDefaultConfigUI (props);
		protected virtual void OnCustomizeConfigUIWindowProperties (PropertyCollection props) { }

		/// <summary>Builds the window settings (title, help text, size) and lets the plugin customize them.</summary>
		internal PropertyCollection CreateWindowProperties ()
		{
			PropertyCollection props = new ([
				new StringProperty (ControlInfoPropertyNames.WindowTitle, Name ?? string.Empty),
				new BooleanProperty (ControlInfoPropertyNames.WindowIsSizable, false),
				new DoubleProperty (ControlInfoPropertyNames.WindowWidthScale, 1.0, 0.25, 4.0),
				StaticListChoiceProperty.CreateForEnum (ControlInfoPropertyNames.WindowHelpContentType, WindowHelpContentType.None),
				new StringProperty (ControlInfoPropertyNames.WindowHelpContent, string.Empty),
				new BooleanProperty (ControlInfoPropertyNames.WindowShowBottomSeparatorLine, true),
			]);
			OnCustomizeConfigUIWindowProperties (props);
			return props;
		}
	}
}

namespace PaintDotNet.AppModel
{
	public interface IShellService
	{
		bool LaunchUrl (System.Windows.Forms.IWin32Window owner, string url);
	}

	public interface IPalettesService
	{
		IReadOnlyList<ColorBgra> CurrentPalette { get; }
		IReadOnlyList<ColorBgra> DefaultPalette { get; }
	}

	public interface IAppInfoService
	{
		string UserDataDirectory { get; }
		string InstallDirectory { get; }
		Version AppVersion { get; }
	}

	public interface IUserFilesService
	{
		string UserFilesPath { get; }
	}

	public interface IEnumLocalizerFactory
	{
		IEnumLocalizer Create (Type enumType);
	}

	public interface IEnumLocalizer
	{
		Type EnumType { get; }
		IList<ILocalizedEnumValue> GetLocalizedEnumValues ();
		ILocalizedEnumValue GetLocalizedEnumValue (object enumValue);
	}

	public interface ILocalizedEnumValue
	{
		Type EnumType { get; }
		object EnumValue { get; }
		string LocalizedName { get; }
	}

	/// <summary>Enum "localization" that turns member names into words: "MotionBlur" becomes "Motion Blur".</summary>
	public sealed class EnumLocalizerFactory : IEnumLocalizerFactory
	{
		public IEnumLocalizer Create (Type enumType) => new Localizer (enumType);

		private sealed class Localizer (Type enumType) : IEnumLocalizer
		{
			public Type EnumType => enumType;
			public IList<ILocalizedEnumValue> GetLocalizedEnumValues () => Enum.GetValues (enumType).Cast<object> ().Select (GetLocalizedEnumValue).ToList ();
			public ILocalizedEnumValue GetLocalizedEnumValue (object enumValue) => new Value (enumType, enumValue);
		}

		private sealed class Value (Type enumType, object value) : ILocalizedEnumValue
		{
			public Type EnumType => enumType;
			public object EnumValue => value;
			public string LocalizedName => System.Text.RegularExpressions.Regex.Replace (value.ToString () ?? string.Empty, "(?<=[a-z0-9])(?=[A-Z])", " ");
		}
	}
}
