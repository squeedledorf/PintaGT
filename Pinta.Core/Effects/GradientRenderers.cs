/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
/////////////////////////////////////////////////////////////////////////////////

using System;

namespace Pinta.Core;

public static class GradientRenderers
{
	public abstract class LinearBase : GradientRenderer
	{
		protected double dtdx;
		protected double dtdy;

		public override void BeforeRender ()
		{
			PointD vector = EndPoint - StartPoint;
			double magnitudeSquared = vector.MagnitudeSquared ();
			dtdx = EndPoint.X == StartPoint.X ? 0 : vector.X / magnitudeSquared;
			dtdy = EndPoint.Y == StartPoint.Y ? 0 : vector.Y / magnitudeSquared;
			base.BeforeRender ();
		}

		protected internal LinearBase (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}
	}

	public abstract class LinearStraight : LinearBase
	{
		private int start_y;
		private int start_x;

		protected internal LinearStraight (bool alphaOnly, BinaryPixelOp normalBlendOp)
			: base (alphaOnly, normalBlendOp)
		{
		}

		protected virtual byte BoundLerp (double t)
			=> ToByteLerp (t);

		public override void BeforeRender ()
		{
			base.BeforeRender ();

			start_x = (int) StartPoint.X;
			start_y = (int) StartPoint.Y;
		}

		public override byte ComputeByteLerp (int x, int y)
		{
			// If the start and end point are the same, use the end color everywhere.
			if (dtdx == 0 && dtdy == 0)
				return byte.MaxValue;

			int dx = x - start_x;
			int dy = y - start_y;

			double lerp = (dx * dtdx) + (dy * dtdy);

			return BoundLerp (lerp);
		}
	}

	public sealed class LinearReflected : LinearStraight
	{
		public LinearReflected (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}

		protected override byte BoundLerp (double t)
			=> ToByteLerp (Math.Abs (t));
	}

	public sealed class LinearClamped : LinearStraight
	{
		public LinearClamped (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}
	}

	public sealed class LinearDiamond : LinearStraight
	{
		public LinearDiamond (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}

		public override byte ComputeByteLerp (int x, int y)
		{
			// If the start and end point are the same, use the end color everywhere.
			if (dtdx == 0 && dtdy == 0)
				return byte.MaxValue;

			double dx = x - StartPoint.X;
			double dy = y - StartPoint.Y;

			double lerp1 = (dx * dtdx) + (dy * dtdy);
			double lerp2 = (dx * dtdy) - (dy * dtdx);

			double absLerp1 = Math.Abs (lerp1);
			double absLerp2 = Math.Abs (lerp2);

			return BoundLerp (absLerp1 + absLerp2);
		}
	}

	public sealed class Radial : GradientRenderer
	{
		private double inv_distance_scale;
		private int start_x;
		private int start_y;

		public Radial (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}

		public override void BeforeRender ()
		{
			double distanceScaleSquared = StartPoint.DistanceSquared (EndPoint);

			start_x = (int) StartPoint.X;
			start_y = (int) StartPoint.Y;
			inv_distance_scale = distanceScaleSquared switch {
				0 => 0,
				_ => 1d / Math.Sqrt (distanceScaleSquared),
			};

			base.BeforeRender ();
		}

		public override byte ComputeByteLerp (int x, int y)
		{
			int dx = x - start_x;
			int dy = y - start_y;

			if (inv_distance_scale == 0)
				return byte.MaxValue;

			return ToByteLerp (Mathematics.Magnitude<double> (dx, dy) * inv_distance_scale);
		}
	}

	public sealed class Conical : GradientRenderer
	{
		private const double InvPi = 1.0 / Math.PI;
		private double t_offset;

		public Conical (bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
		}

		public override void BeforeRender ()
		{
			double ax = EndPoint.X - StartPoint.X;
			double ay = EndPoint.Y - StartPoint.Y;

			double theta = Math.Atan2 (ay, ax);

			double t = theta * InvPi;

			t_offset = -t;

			base.BeforeRender ();
		}

		public override byte ComputeByteLerp (int x, int y)
		{
			double ax = x - StartPoint.X;
			double ay = y - StartPoint.Y;

			double theta = Math.Atan2 (ay, ax);

			double t = theta * InvPi;

			return (byte) (BoundLerp (t + t_offset) * 255f);
		}

		public double BoundLerp (double t)
		{
			double effective = t switch {
				> 1 => t - 2,
				< -1 => t + 2,
				_ => t,
			};

			return Math.Clamp (Math.Abs (effective), 0, 1);
		}
	}
	/// <summary>
	/// Paint.NET's Spiral gradients: one turn around the start point plus the distance from it (in units of the
	/// start-end distance) gives the position along the gradient, so the colours wind outwards in a spiral.
	/// </summary>
	public sealed class Spiral : GradientRenderer
	{
		private readonly bool clockwise;
		private double inv_length;
		private double start_angle;

		public Spiral (bool clockwise, bool alphaOnly, BinaryPixelOp normalBlendOp) : base (alphaOnly, normalBlendOp)
		{
			this.clockwise = clockwise;
		}

		public override void BeforeRender ()
		{
			double length = StartPoint.Distance (EndPoint);
			inv_length = length == 0 ? 0 : 1 / length;
			start_angle = Math.Atan2 (EndPoint.Y - StartPoint.Y, EndPoint.X - StartPoint.X);

			base.BeforeRender ();
		}

		public override byte ComputeByteLerp (int x, int y)
		{
			if (inv_length == 0)
				return byte.MaxValue;

			double dx = x - StartPoint.X;
			double dy = y - StartPoint.Y;

			// Screen y points down, so atan2 grows clockwise on screen.
			double turn = (Math.Atan2 (dy, dx) - start_angle) / (2 * Math.PI);
			if (clockwise)
				turn = -turn;
			turn -= Math.Floor (turn);

			double t = turn + Math.Sqrt (dx * dx + dy * dy) * inv_length;

			// One turn shifts t by 1, which a period-2 reflection turns into a seam; reflect twice per length instead.
			if (RepeatMode == GradientRepeatMode.RepeatReflected)
				t *= 2;

			return ToByteLerp (t);
		}
	}
}
