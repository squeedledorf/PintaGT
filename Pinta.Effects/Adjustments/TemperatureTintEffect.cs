using System;
using System.Threading.Tasks;
using Cairo;
using Pinta.Core;

namespace Pinta.Effects;

public sealed class TemperatureTintEffect : BaseEffect
{
	public sealed override bool IsTileable
		=> true;

	public override string Icon
		=> Resources.Icons.AdjustmentsTemperatureTint;

	public override string Name
		=> Translations.GetString ("Temperature / Tint");

	public override bool IsConfigurable
		=> true;

	public TemperatureTintData Data
		=> (TemperatureTintData) EffectData!;  // NRT - Set in constructor

	private readonly IChromeService chrome;
	private readonly IWorkspaceService workspace;
	public TemperatureTintEffect (IServiceProvider services)
	{
		chrome = services.GetService<IChromeService> ();
		workspace = services.GetService<IWorkspaceService> ();
		EffectData = new TemperatureTintData ();
	}

	public override Task<bool> LaunchConfiguration ()
		=> chrome.LaunchSimpleEffectDialog (this, workspace);

	/// <summary>
	/// Linear-light channel gains. Warm (+temperature) raises red and lowers blue;
	/// +tint leans green, -tint leans magenta. Tuned by eye.
	/// </summary>
	public static (double B, double G, double R) GetGains (int temperature, int tint)
	{
		double t = temperature / 100d;
		double n = tint / 100d;
		double magenta = 1 - 0.15 * n;
		return (
			B: (1 - 0.45 * t) * magenta,
			G: 1 + 0.3 * n,
			R: (1 + 0.45 * t) * magenta);
	}

	protected override void Render (ImageSurface source, ImageSurface destination, RectangleI roi)
	{
		var (b, g, r) = GetGains (Data.Temperature, Data.Tint);
		ChannelTable.Apply (source, destination, roi, ChannelTable.Create (b), ChannelTable.Create (g), ChannelTable.Create (r));
	}

	public sealed class TemperatureTintData : EffectData
	{
		[Caption ("Temperature"), MinimumValue (-100), MaximumValue (100)]
		public int Temperature { get; set; } = 0;

		[Caption ("Tint"), MinimumValue (-100), MaximumValue (100)]
		public int Tint { get; set; } = 0;

		[Skip]
		public override bool IsDefault
			=> Temperature == 0 && Tint == 0;
	}
}
