using NUnit.Framework;

namespace Pinta.Core.Tests;

[TestFixture]
public sealed class PdnMenusTests
{
	[TestCase ("Ctrl+Z", "Ctrl+Z")]
	[TestCase ("Shift+Ctrl+C", "Ctrl+Shift+C")]
	[TestCase ("Shift+Ctrl+Alt+C", "Ctrl+Alt+Shift+C")]
	[TestCase ("Ctrl+Alt+V", "Ctrl+Alt+V")]
	[TestCase ("Delete", "Del")]
	[TestCase ("Shift+Ctrl+Delete", "Ctrl+Shift+Del")]
	[TestCase ("Alt+Page Up", "Alt+PgUp")]
	[TestCase ("Ctrl+Alt+Page Down", "Ctrl+Alt+PgDn")]
	[TestCase ("Backspace", "Backspace")]
	[TestCase ("Ctrl++", "Ctrl++")]
	[TestCase ("Ctrl+-", "Ctrl+-")]
	[TestCase ("+", "+")]
	[TestCase ("F11", "F11")]
	[TestCase ("Super+Shift+X", "Shift+Super+X")]
	[TestCase ("", "")]
	public void FormatAccelerator (string gtk, string expected)
		=> Assert.That (PdnMenus.FormatAccelerator (gtk), Is.EqualTo (expected));

	// Menus are restyled on every open, so formatting an already formatted label must not change it.
	[TestCase ("Shift+Ctrl+Alt+Page Up")]
	[TestCase ("Shift+Ctrl+Delete")]
	[TestCase ("Ctrl++")]
	public void FormatAcceleratorIsIdempotent (string gtk)
	{
		string once = PdnMenus.FormatAccelerator (gtk);
		Assert.That (PdnMenus.FormatAccelerator (once), Is.EqualTo (once));
	}

	// The same goes for access letters: a second pass keeps every letter the first pass chose.
	[Test]
	public void AssignMnemonicsIsIdempotent ()
	{
		string[] once = PdnMenus.AssignMnemonics (["Zoom In", "Zoom Out", "Zoom to Window", "Pixel Grid", "Pixels"]);
		Assert.That (PdnMenus.AssignMnemonics (once), Is.EqualTo (once));
	}

	[Test]
	public void MnemonicsAreUniqueAndPreferFirstLetters ()
	{
		string[] labels = ["Undo", "Redo", "Cut", "Copy", "Copy Flattened", "Paste", "Paste into New Layer",
			"Erase Selection", "Fill Selection", "Select All"];
		Assert.That (PdnMenus.AssignMnemonics (labels), Is.EqualTo (new[] {
			"_Undo", "_Redo", "_Cut", "C_opy", "Cop_y Flattened", "_Paste", "Paste _into New Layer",
			"_Erase Selection", "_Fill Selection", "_Select All" }));
	}

	[Test]
	public void ExistingMnemonicsAreKeptAndReserved ()
	{
		Assert.That (
			PdnMenus.AssignMnemonics (["E_xit", "Export", "Extra"]),
			Is.EqualTo (new[] { "E_xit", "_Export", "Ex_tra" }));
	}

	[Test]
	public void LiteralUnderscoresSurvive ()
	{
		Assert.That (PdnMenus.AssignMnemonics (["a__b"]), Is.EqualTo (new[] { "_a__b" }));
		Assert.That (PdnMenus.AssignMnemonics (["A", "a__b"]), Is.EqualTo (new[] { "_A", "a___b" }));
	}

	[Test]
	public void NoFreeLetterLeavesLabelAlone ()
		=> Assert.That (PdnMenus.AssignMnemonics (["A", "a", "..."]), Is.EqualTo (new[] { "_A", "a", "..." }));
}
