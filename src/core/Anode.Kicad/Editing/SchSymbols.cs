using System.Text;
using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Putting a part on a sheet. Two things have to happen together: the sheet gains an instance that says where the
/// part sits and what it is called, and it gains a copy of the definition so the file can still be drawn by someone
/// who does not have the library.
///
/// The definition is keyed differently in the two places — a library names its symbol <c>R</c>, a sheet names the
/// same definition <c>Device:R</c> — so the copy is renamed as it is adopted.
/// </summary>
public static class SchSymbols
{
    /// <summary>Fields KiCad writes on every placed symbol, in this order.</summary>
    private static readonly string[] Ordered = ["Reference", "Value", "Footprint", "Datasheet", "Description"];

    /// <summary>
    /// Copies <paramref name="definition"/> into the sheet's <c>lib_symbols</c> under <paramref name="libId"/>,
    /// unless a definition of that name is already there. Returns true when the sheet gained one.
    /// </summary>
    public static bool Ensure(Schematic sheet, string libId, LibSymbol definition)
    {
        var library = sheet.Root.Find("lib_symbols");
        if (library is null)
        {
            library = new SList("lib_symbols");
            sheet.Root.Insert(LibrarySymbolsPosition(sheet.Root), library);
        }

        bool known = library.Lists().Any(l => l.Head == "symbol" && string.Equals(l.Str(1), libId, StringComparison.Ordinal));
        if (known)
        {
            return false;
        }

        var copy = SchNodes.Adopt(definition.Node);

        // Renamed before it is read back, so the LibSymbol carries the name the sheet knows it by.
        (copy.AtomAt(1) ?? throw new KiCadFormatException("The definition has no name.")).SetString(libId);
        library.Add(copy);
        sheet.Register(new LibSymbol(copy));
        return true;
    }

    /// <summary>
    /// A placed instance of <paramref name="libId"/>. The fields come from the definition so the part arrives
    /// carrying its own reference and value, moved to where it was dropped; the instance block is what annotation
    /// later writes into, and is filled with what is known now.
    /// </summary>
    /// <param name="reference">The designator, e.g. "R1"; KiCad writes "R?" until the sheet is annotated.</param>
    /// <param name="projectName">The project the sheet belongs to, as its .kicad_pro is named.</param>
    /// <param name="sheetPath">The path of this sheet in the hierarchy: "/" plus the root sheet's uuid.</param>
    public static SymbolInstance Place(
        Schematic sheet,
        string libId,
        LibSymbol definition,
        Vector2L at,
        string reference,
        string projectName,
        string sheetPath,
        double angle = 0,
        int unit = 1)
    {
        var text = new StringBuilder();
        text.Append("(symbol (lib_id ").Append(SEscape.Quote(libId)).Append(')')
            .Append(" (at ").Append(KiCadNumber.FormatMm(at.X)).Append(' ').Append(KiCadNumber.FormatMm(at.Y))
            .Append(' ').Append(KiCadNumber.FormatAngle(angle)).Append(')')
            .Append(" (unit ").Append(unit).Append(')')
            .Append(" (body_style 1) (exclude_from_sim no) (in_bom yes) (on_board yes) (dnp no)")
            .Append(" (uuid \"").Append(Guid.NewGuid()).Append("\")");

        foreach (string field in Ordered)
        {
            string value = field switch
            {
                "Reference" => reference,
                _ => definition.Node.Lists().FirstOrDefault(l =>
                    l.Head == "property" && string.Equals(l.Str(1), field, StringComparison.Ordinal))?.Str(2) ?? string.Empty,
            };

            // A field nobody filled in is still written, hidden, because KiCad writes it and expects to find it.
            bool hide = field is "Datasheet" or "Description" || value.Length == 0;
            text.Append(" (property ").Append(SEscape.Quote(field)).Append(' ').Append(SEscape.Quote(value))
                .Append(" (at ").Append(KiCadNumber.FormatMm(at.X)).Append(' ').Append(KiCadNumber.FormatMm(at.Y)).Append(" 0)")
                .Append(hide ? " (hide yes)" : string.Empty)
                .Append(" (effects (font (size 1.27 1.27))))");
        }

        // Every pin of the part, not only this section's: KiCad writes the whole list on each placed section — the
        // four sections of the 74LS125 in the demos each carry all fourteen. Writing just the section's pins looked
        // right for as long as everything we placed had a single section.
        foreach (var pin in definition.Pins)
        {
            text.Append(" (pin ").Append(SEscape.Quote(pin.Number)).Append(" (uuid \"").Append(Guid.NewGuid()).Append("\"))");
        }

        text.Append(" (instances (project ").Append(SEscape.Quote(projectName))
            .Append(" (path ").Append(SEscape.Quote(sheetPath))
            .Append(" (reference ").Append(SEscape.Quote(reference)).Append(')')
            .Append(" (unit ").Append(unit).Append(")))))");

        var node = SchNodes.Adopt(SDocument.Parse(text.ToString()).Root);
        return sheet.Wrap(node) as SymbolInstance
            ?? throw new KiCadFormatException("The placed symbol did not read back as a symbol.");
    }

    /// <summary>The path of a flat sheet: the root's own uuid, which is what KiCad writes for a single-sheet design.</summary>
    public static string PathOf(Schematic sheet) => "/" + (sheet.Uuid ?? string.Empty);

    /// <summary>KiCad writes lib_symbols after the header and before the drawing.</summary>
    private static int LibrarySymbolsPosition(SList root) =>
        root.Find("paper") is { } paper && root.IndexOf(paper) is >= 0 and var index ? index + 1 : root.Count;
}
