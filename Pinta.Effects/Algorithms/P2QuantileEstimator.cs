using System;

namespace Pinta.Effects;

/// <summary>
/// The P² algorithm (Jain and Chlamtac, 1985): estimates a quantile of a stream with
/// five markers, without storing the stream. The estimate depends on the order the
/// values arrive in, which is what gives Sketch Blur its brush-stroke look.
/// </summary>
public struct P2QuantileEstimator
{
	private readonly double p;
	private int count;
	private double q0, q1, q2, q3, q4; // marker heights
	private int n1, n2, n3; // marker positions (n0 = 0, n4 = count - 1)
	private double d1, d2, d3; // desired positions

	public P2QuantileEstimator (double probability)
	{
		p = Math.Clamp (probability, 0, 1);
	}

	public void Add (double x)
	{
		if (count < 5) {
			switch (count) {
				case 0: q0 = x; break;
				case 1: q1 = x; break;
				case 2: q2 = x; break;
				case 3: q3 = x; break;
				default: q4 = x; break;
			}
			if (++count == 5) {
				Span<double> init = [q0, q1, q2, q3, q4];
				init.Sort ();
				q0 = init[0]; q1 = init[1]; q2 = init[2]; q3 = init[3]; q4 = init[4];
				n1 = 1; n2 = 2; n3 = 3;
				d1 = 2 * p; d2 = 4 * p; d3 = 2 + 2 * p;
			}
			return;
		}

		// Find the cell x falls in, moving the extreme markers if needed.
		int k;
		if (x < q0) { q0 = x; k = 0; } else if (x < q1) k = 0;
		else if (x < q2) k = 1;
		else if (x < q3) k = 2;
		else if (x <= q4) k = 3;
		else { q4 = x; k = 3; }

		if (k < 1) ++n1;
		if (k < 2) ++n2;
		if (k < 3) ++n3;
		++count;

		int n4 = count - 1;
		d1 += p / 2;
		d2 += p;
		d3 += (1 + p) / 2;

		Adjust (ref q1, ref n1, d1, q0, 0, q2, n2);
		Adjust (ref q2, ref n2, d2, q1, n1, q3, n3);
		Adjust (ref q3, ref n3, d3, q2, n2, q4, n4);
	}

	private static void Adjust (ref double q, ref int n, double desired, double qPrev, int nPrev, double qNext, int nNext)
	{
		double d = desired - n;
		if ((d >= 1 && nNext - n > 1) || (d <= -1 && nPrev - n < -1)) {
			int s = Math.Sign (d);
			double candidate = q + (double) s / (nNext - nPrev) * (
				(n - nPrev + s) * (qNext - q) / (nNext - n) +
				(nNext - n - s) * (q - qPrev) / (n - nPrev));
			if (qPrev < candidate && candidate < qNext)
				q = candidate;
			else
				q += s * ((s > 0 ? qNext : qPrev) - q) / ((s > 0 ? nNext : nPrev) - n);
			n += s;
		}
	}

	public readonly double Estimate {
		get {
			if (count >= 5) {
				if (p <= 0) return q0;
				if (p >= 1) return q4;
				return q2;
			}
			if (count == 0)
				return 0;
			Span<double> values = [q0, q1, q2, q3, q4];
			values = values[..count];
			values.Sort ();
			return values[(int) Math.Round (p * (count - 1))];
		}
	}
}
