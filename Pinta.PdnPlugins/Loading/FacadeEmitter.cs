using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Pinta.PdnPlugins;

/// <summary>
/// Writes tiny facade assemblies that contain nothing but type forwarders. A plugin that references
/// "PaintDotNet.Core, Version=4.x" is given a facade with exactly that name, whose types forward to
/// Pinta.PdnShim, so all Paint.NET assembly names (and their moves between versions) resolve to one implementation.
/// </summary>
internal static class FacadeEmitter
{
	private const TypeAttributes Forwarder = (TypeAttributes) 0x00200000;

	/// <summary>Emits a facade named <paramref name="name"/> forwarding every public type of the given assemblies.</summary>
	public static byte[] Emit (string name, Version version, IEnumerable<Assembly> targets)
	{
		MetadataBuilder md = new ();
		md.AddModule (0, md.GetOrAddString (name + ".dll"), md.GetOrAddGuid (Guid.NewGuid ()), default, default);
		md.AddAssembly (md.GetOrAddString (name), version, default, default, 0, AssemblyHashAlgorithm.Sha1);
		md.AddTypeDefinition (default, default, md.GetOrAddString ("<Module>"), default, MetadataTokens.FieldDefinitionHandle (1), MetadataTokens.MethodDefinitionHandle (1));

		HashSet<string> seen = [];
		foreach (Assembly target in targets) {
			AssemblyName an = target.GetName ();
			AssemblyReferenceHandle asmRef = md.AddAssemblyReference (md.GetOrAddString (an.Name!), new Version (0, 0, 0, 0), default, default, 0, default);
			foreach (Type t in target.GetExportedTypes ().Where (t => t.DeclaringType is null)) {
				if (!seen.Add (t.FullName!))
					continue; // the first target wins (the shim before System.Drawing.Primitives)
				ExportedTypeHandle h = md.AddExportedType (Forwarder, md.GetOrAddString (t.Namespace ?? ""), md.GetOrAddString (t.Name), asmRef, 0);
				AddNested (md, t, h);
			}
		}

		ManagedPEBuilder pe = new (PEHeaderBuilder.CreateLibraryHeader (), new MetadataRootBuilder (md), new BlobBuilder ());
		BlobBuilder blob = new ();
		pe.Serialize (blob);
		return blob.ToArray ();
	}

	private static void AddNested (MetadataBuilder md, Type parent, ExportedTypeHandle parentHandle)
	{
		foreach (Type n in parent.GetNestedTypes (BindingFlags.Public)) {
			ExportedTypeHandle h = md.AddExportedType (TypeAttributes.NestedPublic, default, md.GetOrAddString (n.Name), parentHandle, 0);
			AddNested (md, n, h);
		}
	}
}
