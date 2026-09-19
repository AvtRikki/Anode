using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writing a value of a sheet item back into the file — what the inspector does when a field is committed. The
/// counterpart of <see cref="SchEdits"/>, which moves items about: this changes what they say.
///
/// Every write goes through the atoms of the existing tree, so an item that is edited and then undone leaves the
/// file byte for byte as it was, and the parts we never modelled travel untouched.
/// </summary>
public static class SchWrites
{
    /// <summary>The words of a label or a piece of free text.</summary>
    public static void SetText(SchItem item, string text)
    {
        if (item is not (SchLabel or SchText))
        {
            throw new NotSupportedException($"{item.GetType().Name} carries no text.");
        }

        (item.Node.AtomAt(1) ?? throw new KiCadFormatException($"({item.Node.Head} ...) has no text.")).SetString(text);
    }

    /// <summary>A symbol's or a child sheet's field, by name: Reference, Value, Footprint, Sheetname, Sheetfile.</summary>
    public static void SetField(SchItem owner, string field, string value)
    {
        var property = owner.Node.Lists().FirstOrDefault(l =>
            l.Head == "property" && string.Equals(l.Str(1), field, StringComparison.OrdinalIgnoreCase))
            ?? throw new KiCadFormatException($"The item has no field \"{field}\".");

        (property.AtomAt(2) ?? throw new KiCadFormatException($"Field \"{field}\" has no value.")).SetString(value);
    }

    /// <summary>
    /// The designator of a placed symbol. KiCad keeps it twice — in the <c>Reference</c> property, which is what is
    /// drawn, and in the instance block, which is what the rest of the project reads — so both are written or the
    /// file contradicts itself.
    ///
    /// With a <paramref name="sheetPath"/>, only that appearance of the sheet is renamed. A sheet placed twice keeps a
    /// designator per appearance, and writing one name into every path would give both copies of the part the same
    /// designator — exactly the clash annotation exists to prevent. Without one, every path is written, which is
    /// right for a sheet that appears once.
    /// </summary>
    public static void SetReference(SymbolInstance symbol, string reference, string? sheetPath = null)
    {
        SetField(symbol, "Reference", reference);

        foreach (var path in symbol.Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in path.Lists().Where(l => l.Head == "path"))
            {
                if (sheetPath is not null && !string.Equals(entry.Str(1), sheetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Find("reference") is { } written)
                {
                    (written.AtomAt(1) ?? throw new KiCadFormatException("An instance has no reference.")).SetString(reference);
                }
            }
        }
    }

    /// <summary>
    /// Which section of a multi-unit part a placement is: gate B of a quad gate rather than gate A. Like the
    /// designator, the number is kept twice — on the symbol and in the instance block — so both are written.
    ///
    /// The symbol's pin list is deliberately left alone. KiCad writes every pin of the whole part on each placed
    /// section, which the four sections of the 74LS125 in the demo designs confirm: all fourteen pins on each.
    /// </summary>
    /// <param name="sheetPath">Only that appearance of the sheet, as for <see cref="SetReference"/>; null writes every path.</param>
    public static void SetUnit(SymbolInstance symbol, int unit, string? sheetPath = null)
    {
        if (unit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Sections are numbered from one.");
        }

        if (symbol.Node.Find("unit") is { } own)
        {
            (own.AtomAt(1) ?? throw new KiCadFormatException("A symbol has no unit number.")).SetNumber(unit);
        }

        foreach (var path in symbol.Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in path.Lists().Where(l => l.Head == "path"))
            {
                if (sheetPath is not null && !string.Equals(entry.Str(1), sheetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Find("unit") is { } written)
                {
                    (written.AtomAt(1) ?? throw new KiCadFormatException("An instance has no unit number.")).SetNumber(unit);
                }
            }
        }
    }

    /// <summary>Moves the item to a point, keeping whatever angle it has.</summary>
    public static void SetPosition(SchItem item, Vector2L at)
    {
        if (item.Node.Find("at") is not { } node)
        {
            throw new NotSupportedException($"{item.GetType().Name} has no position of its own.");
        }

        node.SetPoint(at);
    }

    /// <summary>Turns the item about its own point. Sheets do not turn, as in KiCad.</summary>
    public static void SetAngle(SchItem item, double degrees)
    {
        if (item.Node.Find("at") is not { } node)
        {
            throw new NotSupportedException($"{item.GetType().Name} has no angle.");
        }

        node.SetAngle(3, KiCadNumber.Normalize360(degrees), omitWhenZero: false);
    }
}
