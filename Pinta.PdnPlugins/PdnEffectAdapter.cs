using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PaintDotNet;
using PaintDotNet.Effects;
using Pinta.Core;
using ColorBgra = PaintDotNet.ColorBgra;

namespace Pinta.PdnPlugins;

/// <summary>
/// A loaded Paint.NET effect class and what Pinta needs to know about it. It is either a classic
/// effect (<see cref="Effect"/>, Paint.NET 3 and 4) or a Paint.NET 5 <see cref="BitmapEffect"/>.
/// </summary>
internal sealed class PdnPluginInfo
{
	public required Type EffectType { get; init; }
	public required string File { get; init; }
	public required string Name { get; init; }
	public required string MenuCategory { get; init; }
	public required string IconName { get; init; }
	public required EffectCategory Category { get; init; }

	/// <summary>Options of a classic effect; null for a BitmapEffect.</summary>
	public EffectOptions? ClassicOptions { get; init; }

	/// <summary>Whether a BitmapEffect asked for a settings dialog.</summary>
	public bool BitmapEffectConfigurable { get; init; }

	public bool IsBitmapEffect => typeof (BitmapEffect).IsAssignableFrom (EffectType);
	public bool IsPropertyBased => typeof (IPropertyBasedEffect).IsAssignableFrom (EffectType);

	public bool IsConfigurable => IsPropertyBased && (IsBitmapEffect ? BitmapEffectConfigurable : (ClassicOptions!.Flags & EffectFlags.Configurable) != 0);

	/// <summary>One Render call with every region (EffectRenderingSchedule.None or the old SingleRenderCall flag).</summary>
	public bool SingleRenderCall => ClassicOptions is EffectOptions o && (o.RenderingSchedule == EffectRenderingSchedule.None || (o.Flags & EffectFlags.LegacySingleRenderCall) != 0);

	public object CreateInstance () => Activator.CreateInstance (EffectType)!;

	/// <summary>A new instance that can already see the image and colors, as plugins expect before they build their properties.</summary>
	public object CreateInstance (RenderEnvironment environment, Surface source)
	{
		object instance = CreateInstance ();
		switch (instance) {
			case Effect effect:
				effect.EnvironmentParameters = environment.CreateParameters (source);
				effect.Services = PdnServices.Instance;
				break;
			case BitmapEffect bitmapEffect:
				bitmapEffect.SetEnvironment (environment.CreateBitmapEnvironment (source), PdnServices.Instance);
				break;
		}
		return instance;
	}
}

/// <summary>The settings Pinta keeps for a plugin: its last config token (also used by Repeat).</summary>
internal sealed class PdnEffectData : EffectData
{
	public EffectConfigToken? Token;

	public override EffectData Clone ()
		=> new PdnEffectData { Token = (EffectConfigToken?) Token?.Clone () };
}

/// <summary>
/// Runs a Paint.NET effect as a Pinta effect. Each render (each clone) gets a fresh plugin instance,
/// straight-alpha copies of the source and destination, one SetRenderInfo call, and then concurrent
/// Render calls from Pinta's tile threads, clipped to the selection like Paint.NET does.
/// </summary>
internal sealed class PdnEffectAdapter : BaseEffect
{
	private RenderSession? session;

	public PdnEffectAdapter (PdnPluginInfo info)
	{
		Info = info;
		EffectData = new PdnEffectData ();
	}

	public PdnPluginInfo Info { get; }
	public PdnEffectData Data => (PdnEffectData) EffectData!;

	public override string Name => Info.Name;
	public override string Icon => Info.IconName;
	public override bool IsConfigurable => Info.IsConfigurable;
	public override string EffectMenuCategory => Info.MenuCategory;
	public override bool IsTileable => !Info.SingleRenderCall;

	public override Task<bool> LaunchConfiguration () => PdnEffectDialog.Run (this);

	public override BaseEffect Clone ()
	{
		RenderEnvironment env = RenderEnvironment.Capture ();
		// Remember the defaults like a used token, so later renders (and Repeat) skip building them again.
		Data.Token ??= DefaultToken (env);
		PdnEffectAdapter clone = (PdnEffectAdapter) base.Clone ();
		clone.session = new RenderSession (clone.Info, clone.Data.Token, env);
		// A new render replaces the previous one (live preview restarts): let plugins that poll IsCancelRequested stop.
		latest_session?.Cancel ();
		latest_session = clone.session;
		return clone;
	}

	private RenderSession? latest_session;

	private EffectConfigToken? DefaultToken (RenderEnvironment env)
	{
		if (!Info.IsPropertyBased)
			return null;
		try {
			object e = Info.CreateInstance (env, RenderEnvironment.CurrentLayer ());
			using (e as IDisposable)
				return new PropertyBasedEffectConfigToken (((IPropertyBasedEffect) e).CreatePropertyCollection ());
		} catch (Exception ex) {
			PluginRegistry.AddRuntimeError (Info.File, Info.EffectType.FullName!, Info.Name, ex);
			return null;
		}
	}

	public override void Render (Cairo.ImageSurface src, Cairo.ImageSurface dst, ReadOnlySpan<RectangleI> rois)
	{
		RenderSession s = session ?? throw new InvalidOperationException ("Render called on an effect that was not cloned for rendering");
		foreach (RectangleI roi in rois)
			s.Render (src, dst, new Rectangle (roi.X, roi.Y, roi.Width, roi.Height));
	}
}

/// <summary>The colors and selection at the moment a render starts (captured on the UI thread).</summary>
internal sealed record RenderEnvironment (ColorBgra Primary, ColorBgra Secondary, float BrushWidth, List<Rectangle>? SelectionScans)
{
	public static RenderEnvironment Capture ()
	{
		static ColorBgra ToBgra (Cairo.Color c) => ColorBgra.FromBgra (
			(byte) Math.Round (Math.Clamp (c.B, 0, 1) * 255),
			(byte) Math.Round (Math.Clamp (c.G, 0, 1) * 255),
			(byte) Math.Round (Math.Clamp (c.R, 0, 1) * 255),
			(byte) Math.Round (Math.Clamp (c.A, 0, 1) * 255));

		List<Rectangle>? scans = null;
		if (PintaCore.Workspace.HasOpenDocuments && PintaCore.Workspace.ActiveDocument.Selection.Visible)
			scans = RasterizeSelection (PintaCore.Workspace.ActiveDocument);

		return new RenderEnvironment (ToBgra (PintaCore.Palette.PrimaryColor), ToBgra (PintaCore.Palette.SecondaryColor), 2f, scans);
	}

	public EffectEnvironmentParameters CreateParameters (Surface source)
		=> new (Primary, Secondary, BrushWidth, source, SelectionRegion ());

	public BitmapEffectEnvironment CreateBitmapEnvironment (Surface source)
		=> new (source, Primary, Secondary, BrushWidth, SelectionRegion ());

	private PdnRegion? SelectionRegion () => SelectionScans is null ? null : PdnRegion.FromRectangles (SelectionScans.ToArray ());

	/// <summary>A straight-alpha copy of the current layer, for plugins that look at the image while building their UI.</summary>
	public static Surface CurrentLayer ()
	{
		Cairo.ImageSurface layer = PintaCore.Workspace.ActiveDocument.Layers.CurrentUserLayer.Surface;
		return ToSurface (layer);
	}

	public static Surface ToSurface (Cairo.ImageSurface src)
	{
		int w = src.Width, h = src.Height;
		Surface surface = new (w, h);
		src.Flush ();
		ReadOnlySpan<byte> data = src.GetData ();
		for (int y = 0; y < h; y++)
			PixelConvert.ToStraight (
				MemoryMarshal.Cast<byte, uint> (data.Slice (y * src.Stride, w * 4)),
				MemoryMarshal.Cast<ColorBgra, uint> (surface.GetRowSpan (y)));
		return surface;
	}

	/// <summary>Rasterizes the selection (any coverage counts) into rectangles.</summary>
	private static List<Rectangle> RasterizeSelection (Document doc)
	{
		RectangleI bounds = doc.Selection.GetBounds ().ToInt ();
		bounds = bounds.Intersect (new RectangleI (0, 0, doc.ImageSize.Width, doc.ImageSize.Height));
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return [];
		using Cairo.ImageSurface mask = new (Cairo.Format.A8, bounds.Width, bounds.Height);
		using (Cairo.Context cr = new (mask)) {
			cr.Translate (-bounds.X, -bounds.Y);
			doc.Selection.Clip (cr);
			cr.Paint ();
		}
		mask.Flush ();
		return RoiUtil.ScansFromMask (mask.GetData (), bounds.Width, bounds.Height, mask.Stride, bounds.X, bounds.Y);
	}
}

/// <summary>One render of one plugin instance, shared by Pinta's tile threads.</summary>
internal sealed class RenderSession
{
	private readonly PdnPluginInfo info;
	private readonly EffectConfigToken? token;
	private readonly RenderEnvironment env;

	private readonly object init_lock = new ();
	private readonly object first_tile_lock = new ();
	private readonly object serial_lock = new ();
	private bool initialized;
	private Exception? init_error;
	private volatile bool first_tile_done;

	private volatile object? effect;
	private bool single_threaded;
	private bool first_tile_barrier;
	private Surface dst_surface = null!;
	private RenderArgs dst_args = null!;
	private RenderArgs src_args = null!;

	public RenderSession (PdnPluginInfo info, EffectConfigToken? token, RenderEnvironment env)
	{
		this.info = info;
		this.token = token;
		this.env = env;
	}

	private volatile bool cancelled;

	/// <summary>Called on the UI thread; never waits for the plugin.</summary>
	public void Cancel ()
	{
		cancelled = true;
		Signal (effect);
	}

	private static void Signal (object? instance)
	{
		switch (instance) {
			case Effect classic: classic.SignalCancelRequest (); break;
			case BitmapEffect bitmap: bitmap.SignalCancel (); break;
		}
	}

	public void Render (Cairo.ImageSurface src, Cairo.ImageSurface dst, Rectangle tile)
	{
		if (cancelled)
			return;
		EnsureInitialized (src);

		Rectangle[] rois = RoiUtil.Clip (tile, env.SelectionScans);
		if (rois.Length == 0)
			return;

		if (!first_tile_done && first_tile_barrier) {
			lock (first_tile_lock) {
				if (!first_tile_done) {
					RenderRois (rois);
					first_tile_done = true;
					CopyOut (dst, rois);
					return;
				}
			}
		}

		RenderRois (rois);
		CopyOut (dst, rois);
	}

	private void RenderRois (Rectangle[] rois)
	{
		try {
			if (single_threaded) {
				lock (serial_lock)
					RenderCore (rois);
			} else {
				RenderCore (rois);
			}
		} catch (Exception ex) {
			PluginRegistry.AddRuntimeError (info.File, info.EffectType.FullName!, info.Name, ex);
			throw;
		}
	}

	private void EnsureInitialized (Cairo.ImageSurface src)
	{
		lock (init_lock) {
			if (!initialized) {
				initialized = true;
				try {
					Initialize (src);
				} catch (Exception ex) {
					init_error = ex;
					PluginRegistry.AddRuntimeError (info.File, info.EffectType.FullName!, info.Name, ex);
				}
			}
			if (init_error is not null)
				throw new InvalidOperationException ($"{info.Name} could not start", init_error);
		}
	}

	private void Initialize (Cairo.ImageSurface src)
	{
		Surface src_surface = RenderEnvironment.ToSurface (src);
		dst_surface = src_surface.Clone ();
		src_args = new RenderArgs (src_surface);
		dst_args = new RenderArgs (dst_surface);

		effect = info.CreateInstance (env, src_surface);
		if (cancelled)
			Signal (effect);
		switch (effect) {
			case Effect classic:
				EffectFlags flags = classic.Options.Flags;
				single_threaded = (flags & (EffectFlags.SingleThreaded | EffectFlags.LegacySingleThreaded)) != 0;
				first_tile_barrier = (flags & EffectFlags.FirstTileIsNotRenderedWithBarrier) == 0; // classic default: barrier
				classic.SetRenderInfo (token, dst_args, src_args);
				break;
			case BitmapEffect bitmap:
				bitmap.Initialize (token);
				BitmapEffectRenderingFlags bflags = bitmap.RenderInfo.Flags;
				single_threaded = (bflags & BitmapEffectRenderingFlags.SingleThreaded) != 0;
				first_tile_barrier = (bflags & BitmapEffectRenderingFlags.FirstTileIsRenderedWithBarrier) != 0; // Paint.NET 5 default: none
				break;
		}
	}

	private void RenderCore (Rectangle[] rois)
	{
		if (effect is Effect classic) {
			classic.Render (token, dst_args, src_args, rois, 0, rois.Length);
			return;
		}
		BitmapEffect bitmap = (BitmapEffect) effect!;
		foreach (Rectangle roi in rois)
			bitmap.Render (dst_surface, roi);
	}

	/// <summary>Writes the rendered regions back to Cairo's premultiplied surface.</summary>
	private void CopyOut (Cairo.ImageSurface dst, Rectangle[] rois)
	{
		Span<byte> data = dst.GetData ();
		int stride = dst.Stride;
		foreach (Rectangle r in rois) {
			for (int y = r.Top; y < r.Bottom; y++)
				PixelConvert.ToPremultiplied (
					MemoryMarshal.Cast<ColorBgra, uint> (dst_surface.GetRowSpan (y).Slice (r.X, r.Width)),
					MemoryMarshal.Cast<byte, uint> (data.Slice (y * stride + r.X * 4, r.Width * 4)));
		}
	}
}

/// <summary>The services plugins look up through Effect.Services.</summary>
internal sealed class PdnServices : IServiceProvider, PaintDotNet.AppModel.IShellService, PaintDotNet.AppModel.IPalettesService, PaintDotNet.AppModel.IAppInfoService, PaintDotNet.AppModel.IEnumLocalizerFactory, PaintDotNet.AppModel.IClipboardService
{
	public static PdnServices Instance { get; } = new ();

	private static readonly PaintDotNet.AppModel.EnumLocalizerFactory enum_localizer = new ();

	public PaintDotNet.AppModel.IEnumLocalizer Create (Type enumType) => enum_localizer.Create (enumType);

	public object? GetService (Type serviceType) => serviceType.IsInstanceOfType (this) ? this : null;

	public bool LaunchUrl (System.Windows.Forms.IWin32Window owner, string url)
	{
		GLib.Functions.IdleAdd (0, () => { _ = PintaCore.System.LaunchUri (url); return false; });
		return true;
	}

	public IReadOnlyList<ColorBgra> CurrentPalette {
		get {
			List<ColorBgra> colors = [];
			foreach (Cairo.Color c in PintaCore.Palette.CurrentPalette.Colors)
				colors.Add (ColorBgra.FromBgra ((byte) (c.B * 255), (byte) (c.G * 255), (byte) (c.R * 255), (byte) (c.A * 255)));
			return colors;
		}
	}

	public IReadOnlyList<ColorBgra> DefaultPalette => CurrentPalette;
	public string UserDataDirectory => PintaCore.Settings.GetUserSettingsDirectory ();
	public string InstallDirectory => AppContext.BaseDirectory;
	public Version AppVersion => new (5, 2);
}
