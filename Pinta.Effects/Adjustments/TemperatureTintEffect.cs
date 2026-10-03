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
	/// +tint leans green, -tint leans magenta. Fitted by eye to the Paint.NET 5 documentation example,
	/// where Temperature 24 moves red and blue by about 11% each.
	/// </summary>
	public static (double B, double G, double R) GetGains (int temperature, int tint)
	{
		double t = 1.34 * temperature / 100d; // stops
		double n = 0.5 * tint / 100d;
		return (
			B: Math.Pow (2, -t - n / 2),
			G: Math.Pow (2, n),
			R: Math.Pow (2, t - n / 2));
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
