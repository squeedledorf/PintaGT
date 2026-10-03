using System;

namespace Pinta.Effects;

/// <summary>
/// The "Gamma Boost" control of the blur effects. Channels are blurred in a v^e space,
/// with e = 2^boost: a positive boost lets the light tones dominate ("high key"), a negative
/// one lets the dark tones dominate ("low key"). A boost of 0 blurs the stored values as-is.
/// </summary>
public sealed class GammaBoost
{
	public const double Min = -1;
	public const double Max = 2;

	private readonly double exponent;
	private readonly double[] forward = new double[256];

	public GammaBoost (double boost)
	{
		exponent = Math.Pow (2, boost);
		for (int i = 0; i < 256; ++i)
			forward[i] = IsIdentity ? i : 255 * Math.Pow (i / 255.0, exponent);
	}

	public bool IsIdentity => exponent == 1;

	/// <summary>Maps a channel value into the blurring space (still 0-255).</summary>
	public double this[byte value] => forward[value];

	/// <summary>Maps an averaged value from the blurring space back to a channel value.</summary>
	public byte Inverse (double value)
	{
		double v = IsIdentity ? value : 255 * Math.Pow (Math.Max (0, value) / 255, 1 / exponent);
		return (byte) Math.Clamp (Math.Round (v), 0, 255);
	}
}
