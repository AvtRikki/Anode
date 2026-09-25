using System.Text;

namespace Anode.Kicad;

/// <summary>One line of a bill of materials: the parts that are the same thing, and what they are.</summary>
public sealed record BomLine(IReadOnlyList<string> References, string Value, string Datasheet, string Footprint, bool Dnp)
{
    public int Quantity => References.Count;
}

/// <summary>
/// The parts of a design as a bill of materials, grouped as KiCad groups them by default ("Grouped By Value",
/// <c>BOM_PRESET::GroupedByValue</c>): one line per value, its parts listed by designator, with the datasheets and
/// footprints they carry and how many there are. Only the value and do-not-place are grouped by — parts of one value
/// on two footprints are one line naming both, as KiCad lists mixed values on export.
///
/// A power symbol is not a part and is left out, and so is one the file keeps off the bill — which KiCad writes the
/// positive way round, as <c>(in_bom no)</c>. A part on a sheet placed twice is two parts, with the designator each
/// place gives it.
/// </summary>
public static class SchBom
{
    /// <summary>The lines of the design rooted at <paramref name="rootFile"/>, by designator.</summary>
    public static IReadOnlyList<BomLine> Build(string rootFile, Func<string, Schematic?>? open = null)
    {
        var parts = new List<(string Reference, string Value, string Datasheet, string Footprint, bool Dnp)>();

        foreach (var place in SchHierarchy.Walk(rootFile, open))
        {
            if (Read(place.File, open) is not { } sheet)
            {
                continue;
            }

            foreach (var symbol in sheet.Symbols)
            {
                string reference = symbol.ReferenceAt(place.Path) ?? symbol.Reference ?? string.Empty;
                if (reference.Length == 0 || reference.StartsWith('#')
                    || !symbol.InBom
                    || symbol.Definition is { IsPower: true })
                {
                    continue;
                }

                parts.Add((
                    reference,
                    symbol.Value ?? string.Empty,
                    Field(symbol, "Datasheet"),
                    Field(symbol, "Footprint"),
                    symbol.IsDnp));
            }
        }

        return
        [
            .. parts
                .GroupBy(p => (p.Value, p.Dnp))
                .Select(g => new BomLine(
                    [.. g.Select(p => p.Reference).Distinct(StringComparer.Ordinal).Order(KicadOrder.Instance)],
                    g.Key.Value,
                    Mixed(g.Select(p => p.Datasheet)),
                    Mixed(g.Select(p => p.Footprint)),
                    g.Key.Dnp))
                .OrderBy(line => line.References[0], KicadOrder.Instance),
        ];
    }

    /// <summary>
    /// The bill as KiCad writes a CSV of it: the columns of its own default preset, every field quoted, the
    /// designators of a line separated by commas inside their one field.
    /// </summary>
    public static string Write(string rootFile, Func<string, Schematic?>? open = null)
    {
        var text = new StringBuilder();
        text.Append("\"Reference\",\"Value\",\"Datasheet\",\"Footprint\",\"Qty\",\"DNP\"\n");

        foreach (var line in Build(rootFile, open))
        {
            text.Append(Csv(string.Join(",", line.References))).Append(',')
                .Append(Csv(line.Value)).Append(',')
                .Append(Csv(line.Datasheet)).Append(',')
                .Append(Csv(line.Footprint)).Append(',')
                .Append(Csv(line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(line.Dnp ? "DNP" : string.Empty))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A column the line does not group by, as KiCad writes it on export: each different value once, empty ones
    /// left out, joined by commas — two footprints for one value are both on the line rather than two lines.
    /// </summary>
    private static string Mixed(IEnumerable<string> values) =>
        string.Join(",", values.Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Field(SymbolInstance symbol, string name) =>
        symbol.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static Schematic? Read(string file, Func<string, Schematic?>? open)
    {
        try
        {
            return open?.Invoke(file) ?? (File.Exists(file) ? Schematic.Load(file) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
            or Sexpr.SexprParseException or DecoderFallbackException)
        {
            return null;
        }
    }
}
