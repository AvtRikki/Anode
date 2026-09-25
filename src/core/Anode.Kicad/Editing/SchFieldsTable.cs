using System.Text;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// One line of the fields table: the placed symbols that are shown as one — the units of one part always, and the
/// parts that are the same thing when the table is grouped.
/// </summary>
public sealed class SchFieldsRow(IReadOnlyList<SymbolInstance> symbols, IReadOnlyList<string> references)
{
    /// <summary>Every placed symbol of the line, all units of every part in it.</summary>
    public IReadOnlyList<SymbolInstance> Symbols { get; } = symbols;

    /// <summary>The designators of the line, each once, in KiCad's order of names.</summary>
    public IReadOnlyList<string> References { get; } = references;

    /// <summary>How many parts: a four-gate package counts once.</summary>
    public int Quantity => References.Count;

    /// <summary>The designators as KiCad writes them in the table: runs shortened to a range, R1-R4, R7.</summary>
    public string Shorthand => SchFieldsTable.Shorthand(References);

    /// <summary>Every part of the line is marked do-not-place.</summary>
    public bool Dnp => Symbols.All(s => s.IsDnp);

    /// <summary>
    /// The value every symbol of the line gives the field, or null when they give different ones — KiCad's
    /// "-- mixed values --". A symbol without the field gives it as empty.
    /// </summary>
    public string? Value(string field)
    {
        string? common = null;
        foreach (var symbol in Symbols)
        {
            string value = SchFieldsTable.Read(symbol, field);
            if (common is null)
            {
                common = value;
            }
            else if (!string.Equals(common, value, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return common ?? string.Empty;
    }

    /// <summary>The different values the line gives a field, empty ones left out, as KiCad lists them on export.</summary>
    public string Mixed(string field) =>
        string.Join(",", Symbols.Select(s => SchFieldsTable.Read(s, field)).Where(v => v.Length > 0).Distinct().Order(StringComparer.Ordinal));
}

/// <summary>
/// KiCad's Symbol Fields Table (<c>fields_data_model.cpp</c>), for the sheet on screen: every part with every field
/// any of them carries, one line per part or per group of parts that are the same thing, and a value written once
/// for the whole line.
///
/// Grouping is KiCad's default preset, "Grouped By Value": parts group when they have the same value and are
/// alike in being placed or not. The units of one part are always one line. Power symbols are not parts and are
/// left out, as are the parts kept off the bill unless they are asked for — both as in KiCad.
/// </summary>
public static class SchFieldsTable
{
    /// <summary>The columns KiCad's default preset starts with, in its order; every other field follows them.</summary>
    public static readonly IReadOnlyList<string> Leading = ["Reference", "Value", "Datasheet", "Footprint"];

    /// <summary>
    /// The fields to show: the leading four, then every other field any part carries, in the order they are first
    /// met. Fields are named as they are written; two spellings that differ only in case are one column, as KiCad
    /// finds a field without regard to case.
    /// </summary>
    public static IReadOnlyList<string> Columns(IEnumerable<SymbolInstance> symbols)
    {
        var columns = new List<string>(Leading);
        var seen = new HashSet<string>(Leading, StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            foreach (var field in symbol.Fields)
            {
                if (seen.Add(field.Name))
                {
                    columns.Add(field.Name);
                }
            }
        }

        return columns;
    }

    /// <summary>The parts of the sheet the table shows: no power symbols, and none kept off the bill unless asked for.</summary>
    public static IReadOnlyList<SymbolInstance> Parts(Schematic sheet, string? path, bool includeExcluded = false) =>
    [
        .. sheet.Symbols.Where(s =>
            !(s.Definition?.IsPower ?? false)
            && !(s.ReferenceAt(path) ?? s.Reference ?? string.Empty).StartsWith('#')
            && (includeExcluded || s.InBom)),
    ];

    /// <summary>The lines of the table, sorted by designator as KiCad sorts them.</summary>
    /// <param name="path">The appearance of the sheet whose designators are shown.</param>
    public static IReadOnlyList<SchFieldsRow> Rows(IReadOnlyList<SymbolInstance> parts, string? path, bool group = true)
    {
        var lines = new List<List<SymbolInstance>>();
        foreach (var symbol in parts)
        {
            string reference = Designator(symbol, path);
            var line = lines.FirstOrDefault(l =>
                SameUnit(Designator(l[0], path), reference)
                || (group && SameThing(l[0], symbol)));

            if (line is null)
            {
                lines.Add([symbol]);
            }
            else
            {
                line.Add(symbol);
            }
        }

        return
        [
            .. lines
                .Select(l => new SchFieldsRow(l, [.. l.Select(s => Designator(s, path)).Distinct(StringComparer.Ordinal).Order(KicadOrder.Instance)]))
                .OrderBy(r => r.Shorthand, KicadOrder.Instance),
        ];
    }

    /// <summary>
    /// Writes one value into a field of every part of the line. A designator is not written from here, as in KiCad.
    /// A part without the field is given one when there is something to put in it, hidden and at the part, the way
    /// KiCad adds a field no template names; an empty value adds nothing.
    /// </summary>
    public static bool Write(SchFieldsRow row, string field, string value)
    {
        if (string.Equals(field, "Reference", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool changed = false;
        foreach (var symbol in row.Symbols)
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

    /// <summary>The value a part gives a field, found without regard to case; empty when it has none.</summary>
    public static string Read(SymbolInstance symbol, string field) => Property(symbol, field)?.Str(2) ?? string.Empty;

    /// <summary>KiCad's <c>SCH_REFERENCE_LIST::Shorthand</c> with the table's ", " and "-": R1-R4, R7, R9, R10.</summary>
    public static string Shorthand(IReadOnlyList<string> references)
    {
        var text = new StringBuilder();
        int i = 0;
        while (i < references.Count)
        {
            var (prefix, number) = Split(references[i]);
            int range = 1;
            while (number is { } n && i + range < references.Count
                && Split(references[i + range]) is var (p, m)
                && p == prefix && m == n + range)
            {
                range++;
            }

            if (text.Length > 0)
            {
                text.Append(", ");
            }

            text.Append(references[i]);
            if (range == 2)
            {
                text.Append(", ").Append(references[i + 1]);
            }
            else if (range > 2)
            {
                text.Append('-').Append(references[i + range - 1]);
            }

            i += range;
        }

        return text.ToString();
    }

    private static string Designator(SymbolInstance symbol, string? path) => symbol.ReferenceAt(path) ?? symbol.Reference ?? "?";

    /// <summary>
    /// Units of one part: the same designator. One not numbered yet — R? — cannot be told apart from another, so it
    /// is never taken for a unit of it, as in KiCad's <c>unitMatch</c>.
    /// </summary>
    private static bool SameUnit(string a, string b) => !a.EndsWith('?') && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>KiCad's default grouping: the same value, and both placed or both not.</summary>
    private static bool SameThing(SymbolInstance a, SymbolInstance b) =>
        string.Equals(Read(a, "Value"), Read(b, "Value"), StringComparison.Ordinal) && a.IsDnp == b.IsDnp;

    /// <summary>The designator's letters and its number: R12 is R and 12; a designator without a number has none.</summary>
    private static (string Prefix, int? Number) Split(string reference)
    {
        int at = reference.Length;
        while (at > 0 && char.IsAsciiDigit(reference[at - 1]))
        {
            at--;
        }

        return at < reference.Length && int.TryParse(reference.AsSpan(at), out int number)
            ? (reference[..at], number)
            : (reference, null);
    }

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
