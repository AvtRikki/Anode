using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writing one field of many parts at once — what the bill of materials does when a value is typed into a line —
/// by KiCad's rules for its Symbol Fields Table (<c>FIELDS_EDITOR_GRID_DATA_MODEL::ApplyData</c>).
/// </summary>
public static class SchFields
{
    /// <summary>
    /// Writes <paramref name="value"/> into <paramref name="field"/> of every symbol. A designator is not written from
    /// here, as in KiCad. A symbol without the field is given one when there is something to put in it, hidden and at
    /// the part, the way KiCad adds a field no template names; an empty value adds nothing. Answers whether anything
    /// changed.
    /// </summary>
    public static bool Write(IEnumerable<SymbolInstance> symbols, string field, string value)
    {
        if (string.Equals(field, "Reference", StringComparison.OrdinalIgnoreCase) || BomPreset.IsGenerated(field))
        {
            return false;
        }

        bool changed = false;
        foreach (var symbol in symbols)
        {
            if (Property(symbol, field) is { } property)
            {
                if (!string.Equals(property.Str(2), value, StringComparison.Ordinal))
                {
                    (property.AtomAt(2) ?? throw new KiCadFormatException($"Field \"{field}\" has no value.")).SetString(value);
                    changed = true;
                }
            }
            else if (value.Length > 0)
            {
                AddField(symbol, field, value);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>The value a part gives a field, found without regard to case, as written; empty when it has none.</summary>
    public static string Read(SymbolInstance symbol, string field) => Property(symbol, field)?.Str(2) ?? string.Empty;

    private static SList? Property(SymbolInstance symbol, string field) =>
        symbol.Node.Lists().FirstOrDefault(l => l.Head == "property" && string.Equals(l.Str(1), field, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A field the part does not have yet. It is written as a copy of a field the part already hides — which is how
    /// this file's version writes a hidden field, whether that is <c>(hide yes)</c> on the field or inside its
    /// effects — renamed, standing at the part and turned as the designator is, as KiCad places a new field.
    /// </summary>
    private static void AddField(SymbolInstance symbol, string field, string value)
    {
        var properties = symbol.Node.Lists().Where(l => l.Head == "property").ToList();
        var model = properties.FirstOrDefault(p => new SchField(p).IsHidden)
            ?? throw new KiCadFormatException("The part has no hidden field to write a new one after.");

        var fresh = SchNodes.Adopt(model);
        (fresh.AtomAt(1) ?? throw new KiCadFormatException("A field has no name.")).SetString(field);
        (fresh.AtomAt(2) ?? throw new KiCadFormatException("A field has no value.")).SetString(value);

        double angle = properties.FirstOrDefault(p => p.Str(1) == "Reference") is { } reference ? new SchField(reference).Angle : 0;
        if (fresh.Find("at") is { } at)
        {
            at.SetPoint(symbol.Position);
            at.SetAngle(3, angle, omitWhenZero: false);
        }

        // Written after the last field, where KiCad keeps a symbol's fields together.
        symbol.Node.Insert(symbol.Node.IndexOf(properties[^1]) + 1, fresh);
        symbol.AfterRestore();
    }
}
