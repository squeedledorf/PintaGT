using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Pinta.PdnPlugins;

/// <summary>What the metadata says about one plugin class, before any of its code runs.</summary>
internal sealed record ScannedClass (string FullName, string BaseType, string? UnsupportedReason);

/// <summary>What the metadata says about one DLL.</summary>
internal sealed record ScannedAssembly (string Path, bool ReferencesPaintDotNet, IReadOnlyList<ScannedClass> Classes);

/// <summary>
/// Reads plugin DLL metadata (assembly references, type definitions and their base types) with
/// System.Reflection.Metadata, to find effect classes and to rule out kinds Pinta cannot run
/// (GPU/Direct2D effects, custom WinForms dialogs, file types) without loading them.
/// </summary>
internal static class PluginScanner
{
	public static ScannedAssembly Scan (string path)
	{
		using FileStream fs = File.OpenRead (path);
		using PEReader pe = new (fs);
		if (!pe.HasMetadata)
			return new ScannedAssembly (path, false, []);

		MetadataReader md = pe.GetMetadataReader ();
		string[] references = md.AssemblyReferences.Select (h => md.GetString (md.GetAssemblyReference (h).Name)).ToArray ();
		if (!references.Any (n => n.StartsWith ("PaintDotNet", StringComparison.OrdinalIgnoreCase)))
			return new ScannedAssembly (path, false, []);

		// Plugins that bring their own Direct3D wrapper run their effect on the GPU even when they derive from a CPU base.
		string? directX = references.FirstOrDefault (n => n.StartsWith ("SharpDX", StringComparison.OrdinalIgnoreCase) || n.StartsWith ("Vortice", StringComparison.OrdinalIgnoreCase) || n.StartsWith ("ComputeSharp", StringComparison.OrdinalIgnoreCase));

		// P/Invoke into a native DLL of the plugin's own (a Windows build of FFTW, LCMS, ...) can never load here.
		// Calls into Windows system DLLs are left alone: they are often optional (CodeLab's string lookups).
		string? nativeLibrary = md.MethodDefinitions
			.Select (h => md.GetMethodDefinition (h))
			.Where (m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0 && !m.GetImport ().Module.IsNil)
			.Select (m => md.GetString (md.GetModuleReference (m.GetImport ().Module).Name))
			.FirstOrDefault (module => !IsWindowsSystemLibrary (module));

		// Local types that derive from a WinForms form or a Paint.NET config dialog mean a custom settings UI.
		bool hasCustomDialog = md.TypeDefinitions.Any (h => {
			string b = ExternalBase (md, h);
			return b.Contains ("EffectConfigDialog") || b.Contains ("EffectConfigForm") || b.EndsWith ("PdnBaseForm") || b == "System.Windows.Forms.Form";
		});

		List<ScannedClass> classes = [];
		foreach (TypeDefinitionHandle h in md.TypeDefinitions) {
			TypeDefinition td = md.GetTypeDefinition (h);
			if ((td.Attributes & TypeAttributes.Abstract) != 0)
				continue;
			string baseType = ExternalBase (md, h);
			string? reason = Classify (baseType);
			if (reason == "not a plugin")
				continue;
			// Classic effects with their own CreateConfigDialog show a WinForms dialog Pinta cannot host.
			if (reason is null && hasCustomDialog && baseType is "PaintDotNet.Effects.Effect" or "PaintDotNet.Effects.Effect`1" && DeclaresMethod (md, h, "CreateConfigDialog"))
				reason = "uses its own Windows Forms settings dialog";
			if (reason is null && baseType.Contains ("BitmapEffect") && hasCustomDialog)
				reason = "uses its own Windows Forms settings dialog";
			if (reason is null && directX is not null)
				reason = $"renders with Direct3D ({directX})";
			if (reason is null && nativeLibrary is not null)
				reason = $"needs the Windows native library {nativeLibrary}";
			string ns = md.GetString (td.Namespace);
			classes.Add (new ScannedClass ((ns.Length > 0 ? ns + "." : "") + md.GetString (td.Name), baseType, reason));
		}
		return new ScannedAssembly (path, true, classes);
	}

	private static bool IsWindowsSystemLibrary (string module)
	{
		string m = module.ToLowerInvariant ();
		if (m.EndsWith (".dll")) m = m[..^4];
		return m is "kernel32" or "user32" or "gdi32" or "gdiplus" or "shell32" or "ole32" or "oleaut32" or "advapi32" or "comctl32" or "uxtheme" or "dwmapi" or "msvcrt" or "ntdll" or "combase" or "shlwapi" or "winmm";
	}

	/// <summary>Null if the base type is runnable, otherwise why not.</summary>
	private static string? Classify (string baseType) => baseType switch {
		"PaintDotNet.Effects.Effect" or "PaintDotNet.Effects.Effect`1" or "PaintDotNet.Effects.PropertyBasedEffect" => null,
		_ when baseType.Contains ("Gpu") => "is a GPU (Direct2D) effect",
		_ when baseType.StartsWith ("PaintDotNet.Effects.") && baseType.Contains ("BitmapEffect") => "is a Paint.NET 5 BitmapEffect, which is not supported yet",
		_ when baseType.Contains ("FileType") => "is a file type plugin, which Pinta does not load",
		_ => "not a plugin",
	};

	/// <summary>The first non-local type in the base chain, e.g. "PaintDotNet.Effects.PropertyBasedEffect".</summary>
	private static string ExternalBase (MetadataReader md, TypeDefinitionHandle h)
	{
		EntityHandle b = md.GetTypeDefinition (h).BaseType;
		for (int guard = 0; !b.IsNil && b.Kind == HandleKind.TypeDefinition && guard < 32; guard++)
			b = md.GetTypeDefinition ((TypeDefinitionHandle) b).BaseType;
		return b.IsNil ? string.Empty : Name (md, b);
	}

	private static bool DeclaresMethod (MetadataReader md, TypeDefinitionHandle h, string name)
	{
		for (int guard = 0; !h.IsNil && guard < 32; guard++) {
			TypeDefinition td = md.GetTypeDefinition (h);
			if (td.GetMethods ().Any (m => md.GetString (md.GetMethodDefinition (m).Name) == name))
				return true;
			if (td.BaseType.Kind != HandleKind.TypeDefinition)
				return false;
			h = (TypeDefinitionHandle) td.BaseType;
		}
		return false;
	}

	private static string Name (MetadataReader md, EntityHandle h)
	{
		switch (h.Kind) {
			case HandleKind.TypeReference: {
					TypeReference tr = md.GetTypeReference ((TypeReferenceHandle) h);
					string ns = md.GetString (tr.Namespace);
					string n = (ns.Length > 0 ? ns + "." : "") + md.GetString (tr.Name);
					return tr.ResolutionScope.Kind == HandleKind.TypeReference ? Name (md, (TypeReferenceHandle) tr.ResolutionScope) + "+" + n : n;
				}
			case HandleKind.TypeSpecification: {
					// A generic instantiation such as Effect<MyToken>: report the generic definition.
					BlobReader br = md.GetBlobReader (md.GetTypeSpecification ((TypeSpecificationHandle) h).Signature);
					if (br.ReadSignatureTypeCode () != SignatureTypeCode.GenericTypeInstance)
						return string.Empty;
					br.ReadCompressedInteger ();
					return Name (md, br.ReadTypeHandle ());
				}
			case HandleKind.TypeDefinition: {
					TypeDefinition td = md.GetTypeDefinition ((TypeDefinitionHandle) h);
					return md.GetString (td.Namespace) + "." + md.GetString (td.Name);
				}
			default:
				return string.Empty;
		}
	}
}
