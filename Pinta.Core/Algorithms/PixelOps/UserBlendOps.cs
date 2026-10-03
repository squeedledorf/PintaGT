/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System.Collections.Generic;
using System.Linq;

namespace Pinta.Core;

/// <summary>
/// This class contains all the render ops that can be used by the user
/// to configure a layer's blending mode. It also contains helper
/// functions to aid in enumerating and using these blend ops.
///
/// Credit for mathematical descriptions of many of the blend modes goes to
/// a page on Pegtop Software's website called, "Blend Modes"
/// http://www.pegtop.net/delphi/articles/blendmodes/
/// </summary>
public sealed partial class UserBlendOps
{
	// Paint.NET's 14 layer blend modes, in its order. These are the ones offered in the UI.
	private static readonly (BlendMode Mode, string Name)[] pdn_blend_modes = [
		(BlendMode.Normal, Translations.GetString ("Normal")),
		(BlendMode.Multiply, Translations.GetString ("Multiply")),
		(BlendMode.Additive, Translations.GetString ("Additive")),
		(BlendMode.ColorBurn, Translations.GetString ("Color Burn")),
		(BlendMode.ColorDodge, Translations.GetString ("Color Dodge")),
		(BlendMode.Reflect, Translations.GetString ("Reflect")),
		(BlendMode.Glow, Translations.GetString ("Glow")),
		(BlendMode.Overlay, Translations.GetString ("Overlay")),
		(BlendMode.Difference, Translations.GetString ("Difference")),
		(BlendMode.Negation, Translations.GetString ("Negation")),
		(BlendMode.Lighten, Translations.GetString ("Lighten")),
		(BlendMode.Darken, Translations.GetString ("Darken")),
		(BlendMode.Screen, Translations.GetString ("Screen")),
		(BlendMode.Xor, Translations.GetString ("Xor")),
	];

	// Pinta's other modes still load from .ora files and render, but are not offered.
	private static readonly (BlendMode Mode, string Name)[] other_blend_modes = [
		(BlendMode.HardLight, Translations.GetString ("Hard Light")),
		(BlendMode.SoftLight, Translations.GetString ("Soft Light")),
		(BlendMode.Color, Translations.GetString ("Color")),
		(BlendMode.Luminosity, Translations.GetString ("Luminosity")),
		(BlendMode.Hue, Translations.GetString ("Hue")),
		(BlendMode.Saturation, Translations.GetString ("Saturation")),
	];

	private UserBlendOps ()
	{
	}

	/// <summary>The blend modes offered to the user: Paint.NET's 14, in its order.</summary>
	public static IEnumerable<BlendMode> GetAllBlendModes ()
		=> pdn_blend_modes.Select (p => p.Mode);

	public static IEnumerable<string> GetAllBlendModeNames ()
		=> pdn_blend_modes.Select (p => p.Name);

	public static BlendMode GetBlendModeByName (string name)
		=> pdn_blend_modes.Concat (other_blend_modes).First (p => p.Name == name).Mode;

	public static string GetBlendModeName (BlendMode mode)
		=> pdn_blend_modes.Concat (other_blend_modes).First (p => p.Mode == mode).Name;

	/// <summary>
	/// The pixel op for the Paint.NET modes that Cairo has no operator for, or null for the others.
	/// Xor is here too: Paint.NET's Xor is a bitwise XOR of the colors, while Cairo's XOR is the
	/// Porter-Duff operator, which turns opaque-over-opaque transparent.
	/// </summary>
	public static UserBlendOp? GetSoftwareBlendOp (BlendMode mode)
		=> mode switch {
			BlendMode.Additive => new AdditiveBlendOp (),
			BlendMode.Reflect => new ReflectBlendOp (),
			BlendMode.Glow => new GlowBlendOp (),
			BlendMode.Negation => new NegationBlendOp (),
			BlendMode.Xor => new XorBlendOp (),
			_ => null,
		};
}
