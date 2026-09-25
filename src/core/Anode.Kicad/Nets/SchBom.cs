using System.Text;
using System.Text.Json;

namespace Anode.Kicad;

/// <summary>A column of the bill: the field it shows, what the column is headed, whether it is shown, and whether lines group by it.</summary>
/// <param name="Name">A field's name, or one of KiCad's generated ones: <c>${QUANTITY}</c>, <c>${ITEM_NUMBER}</c>, <c>${DNP}</c>…</param>
public sealed record BomField(string Name, string Label, bool Show, bool GroupBy);

/// <summary>
/// How the bill is laid out — KiCad's <c>BOM_PRESET</c>, kept in the project file under <c>schematic.bom_settings</c>
/// so that KiCad and we lay a project's bill out the same way.
/// </summary>
public sealed record BomPreset(
    string Name,
    IReadOnlyList<BomField> Fields,
    string SortField = "Reference",
    bool SortAscending = true,
    string Filter = "",
    bool GroupSymbols = true,
    bool ExcludeDnp = false,
    bool IncludeExcludedFromBom = false)
{
    public const string Quantity = "${QUANTITY}";
    public const string ItemNumber = "${ITEM_NUMBER}";
    public const string Dnp = "${DNP}";
    public const string ExcludeFromBom = "${EXCLUDE_FROM_BOM}";
    public const string ExcludeFromBoard = "${EXCLUDE_FROM_BOARD}";
    public const string ExcludeFromSim = "${EXCLUDE_FROM_SIM}";

    /// <summary>
    /// KiCad's default when a project names none (<c>BOM_PRESET::DefaultEditing</c>): every placed part, grouped by
    /// value, footprint and the three flags, so that two parts on one line really are the same thing to place.
    /// </summary>
    public static BomPreset DefaultEditing { get; } = new(
        "Default Editing",
        [
            new("Reference", "Reference", true, false),
            new(Quantity, "Qty", true, false),
            new("Value", "Value", true, true),
            new(Dnp, "DNP", true, true),
            new(ExcludeFromBom, "Exclude from BOM", true, true),
            new(ExcludeFromBoard, "Exclude from Board", true, true),
            new("Footprint", "Footprint", true, true),
            new("Datasheet", "Datasheet", true, false),
        ],
        IncludeExcludedFromBom: true);

    /// <summary>KiCad's "Grouped By Value" (<c>BOM_PRESET::GroupedByValue</c>).</summary>
    public static BomPreset GroupedByValue { get; } = new(
        "Grouped By Value",
        [
            new("Reference", "Reference", true, false),
            new("Value", "Value", true, true),
            new("Datasheet", "Datasheet", true, false),
            new("Footprint", "Footprint", true, false),
            new(Quantity, "Qty", true, false),
            new(Dnp, "DNP", true, true),
        ]);

    /// <summary>KiCad's "Grouped By Value and Footprint" (<c>BOM_PRESET::GroupedByValueFootprint</c>).</summary>
    public static BomPreset GroupedByValueFootprint { get; } = new(
        "Grouped By Value and Footprint",
        [
            new("Reference", "Reference", true, false),
            new("Value", "Value", true, true),
            new("Datasheet", "Datasheet", true, false),
            new("Footprint", "Footprint", true, true),
            new(Quantity, "Qty", true, false),
            new(Dnp, "DNP", true, true),
        ]);

    /// <summary>KiCad's built-in presets, in the order its dialog lists them.</summary>
    public static IReadOnlyList<BomPreset> BuiltIn { get; } = [DefaultEditing, GroupedByValue, GroupedByValueFootprint];

    /// <summary>Whether a column is one of KiCad's generated ones rather than a field a part carries.</summary>
    public static bool IsGenerated(string name) => name.StartsWith("${", StringComparison.Ordinal) && name.EndsWith('}');

    /// <summary>Whether a column is one of the part's flags, which KiCad shows as a tick.</summary>
    public static bool IsAttribute(string name) => name is Dnp or ExcludeFromBom or ExcludeFromBoard or ExcludeFromSim;

    /// <summary>
    /// The preset a project lays its bill out with, and the ones it keeps besides — read from
    /// <c>schematic.bom_settings</c> and <c>schematic.bom_presets</c> as KiCad writes them. A project that says
    /// nothing, or cannot be read, gets KiCad's default.
    /// </summary>
    public static (BomPreset Current, IReadOnlyList<BomPreset> Saved) Read(string? projectFile)
    {
        if (projectFile is null || !File.Exists(projectFile))
        {
            return (DefaultEditing, []);
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(projectFile), new JsonDocumentOptions { AllowTrailingCommas = true });
            if (!json.RootElement.TryGetProperty("schematic", out var schematic) || schematic.ValueKind != JsonValueKind.Object)
            {
                return (DefaultEditing, []);
            }

            var current = schematic.TryGetProperty("bom_settings", out var settings) ? Parse(settings) : null;
            var saved = schematic.TryGetProperty("bom_presets", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(Parse).OfType<BomPreset>().ToList()
                : [];

            return (current ?? DefaultEditing, saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (DefaultEditing, []);
        }
    }

    /// <summary>One preset as KiCad writes it (<c>from_json( BOM_PRESET )</c>); null when it will not read.</summary>
    private static BomPreset? Parse(JsonElement preset)
    {
        if (preset.ValueKind != JsonValueKind.Object
            || !preset.TryGetProperty("fields_ordered", out var ordered) || ordered.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var fields = new List<BomField>();
        foreach (var field in ordered.EnumerateArray())
        {
            if (Text(field, "name") is { Length: > 0 } name)
            {
                fields.Add(new BomField(name, Text(field, "label") ?? name, Flag(field, "show", true), Flag(field, "group_by", false)));
            }
        }

        return new BomPreset(
            Text(preset, "name") ?? string.Empty,
            fields,
            Text(preset, "sort_field") ?? "Reference",
            Flag(preset, "sort_asc", true),
            Text(preset, "filter_string") ?? string.Empty,
            Flag(preset, "group_symbols", true),
            Flag(preset, "exclude_dnp", false),
            // Not written by KiCad 8.0's first releases, which read it as off.
            Flag(preset, "include_excluded_from_bom", false));

        static string? Text(JsonElement at, string name) =>
            at.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        static bool Flag(JsonElement at, string name, bool fallback) =>
            at.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    }
}

/// <summary>One placed part in one place of the design: a symbol on a sheet placed twice is two parts.</summary>
/// <param name="File">The sheet the symbol is written in.</param>
/// <param name="Path">The place of that sheet in the design, whose designators the part carries.</param>
public sealed record BomPart(SymbolInstance Symbol, string Reference, string File, string? Path);

/// <summary>One line of the bill: the parts that are shown as one.</summary>
public sealed class BomRow(IReadOnlyList<BomPart> parts)
{
    public IReadOnlyList<BomPart> Parts { get; } = parts;

    /// <summary>The designators of the line, each once — the units of one part are one designator — in KiCad's order.</summary>
    public IReadOnlyList<string> References { get; } =
        [.. parts.Select(p => p.Reference).Distinct(StringComparer.Ordinal).Order(KicadOrder.Instance)];

    /// <summary>How many parts: a four-gate package counts once.</summary>
    public int Quantity => References.Count;

    /// <summary>The line's number in the bill, counted from one after sorting — KiCad's <c>${ITEM_NUMBER}</c>.</summary>
    public int ItemNumber { get; internal set; }

    /// <summary>Every symbol the line reads from, once each, whatever places it stands in.</summary>
    public IReadOnlyList<SymbolInstance> Symbols => [.. Parts.Select(p => p.Symbol).Distinct()];
}

/// <summary>
/// The bill of materials of a design laid out by a preset: KiCad's Symbol Fields Table and its BOM export
/// (<c>fields_data_model.cpp</c>), for the whole design at once.
///
/// Parts are every placed symbol of every place in the design, less power symbols, and less what the preset keeps
/// out: parts not placed when it excludes them, parts kept off the bill unless it includes them. Units of one
/// designator are always one line (<c>unitMatch</c>); beyond that, with grouping on, parts are one line when every
/// column the preset groups by reads the same for both (<c>groupMatch</c>) — and a preset that groups by nothing
/// groups nothing. Lines sort by the preset's column, then by first designator.
/// </summary>
public sealed class BomTable
{
    private BomTable(BomPreset preset, IReadOnlyList<BomField> columns, IReadOnlyList<BomRow> rows)
    {
        Preset = preset;
        Columns = columns;
        Rows = rows;
    }

    public BomPreset Preset { get; }

    /// <summary>The preset's columns in its order, then every other field a part carries, hidden, as KiCad adds them.</summary>
    public IReadOnlyList<BomField> Columns { get; }

    public IReadOnlyList<BomRow> Rows { get; }

    /// <summary>How many parts the bill counts.</summary>
    public int PartCount => Rows.Sum(r => r.Quantity);

    public static BomTable Build(IEnumerable<BomPart> parts, BomPreset preset, string? filter = null)
    {
        var kept = parts.Where(p =>
            !(p.Symbol.Definition?.IsPower ?? false)
            && !p.Reference.StartsWith('#')
            && !(preset.ExcludeDnp && p.Symbol.IsDnp)
            && (preset.IncludeExcludedFromBom || p.Symbol.InBom)).ToList();

        // KiCad's filter looks at the designators only.
        string match = filter ?? preset.Filter;
        if (match.Length > 0)
        {
            kept = [.. kept.Where(p => p.Reference.Contains(match, StringComparison.OrdinalIgnoreCase))];
        }

        var grouping = preset.Fields.Where(f => f.GroupBy).Select(f => f.Name).ToList();
        var lines = new List<List<BomPart>>();
        foreach (var part in kept)
        {
            var line = lines.FirstOrDefault(l =>
                SameUnit(l[0].Reference, part.Reference)
                || (preset.GroupSymbols && grouping.Count > 0 && grouping.All(name => SameFor(name, l[0], part))));

            if (line is null)
            {
                lines.Add([part]);
            }
            else
            {
                line.Add(part);
            }
        }

        var rows = lines.Select(l => new BomRow(l)).ToList();
        var sortBy = preset.SortField;
        bool byReference = string.Equals(sortBy, "Reference", StringComparison.Ordinal) || preset.Fields.All(f => f.Name != sortBy);
        var sorted = byReference
            ? rows.OrderBy(r => r.References.FirstOrDefault() ?? string.Empty, KicadOrder.Instance).ToList()
            : rows.OrderBy(r => Text(r, sortBy, forExport: false) ?? string.Empty, KicadOrder.Instance)
                .ThenBy(r => r.References.FirstOrDefault() ?? string.Empty, KicadOrder.Instance).ToList();
        if (!preset.SortAscending)
        {
            sorted.Reverse();
        }

        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].ItemNumber = i + 1;
        }

        var columns = preset.Fields.ToList();
        var named = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var field in kept.SelectMany(p => p.Symbol.Fields))
        {
            if (named.Add(field.Name))
            {
                columns.Add(new BomField(field.Name, field.Name, Show: false, GroupBy: false));
            }
        }

        return new BomTable(preset, columns, sorted);
    }

    /// <summary>
    /// What a line says in a column. Where its parts disagree the table shows null — KiCad's "mixed values" — and the
    /// export lists each value once, comma-separated, as KiCad's does. Designators are shortened with ranges on screen
    /// (R1-R4) and listed one by one on export, KiCad's two defaults.
    /// </summary>
    public static string? Text(BomRow row, string column, bool forExport)
    {
        switch (column)
        {
            case "Reference":
                return forExport ? string.Join(",", row.References) : Shorthand(row.References);
            case BomPreset.Quantity:
                return row.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case BomPreset.ItemNumber:
                return row.ItemNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var values = row.Parts.Select(p => Value(p, column)).ToList();
        if (values.Count == 0 || values.All(v => string.Equals(v, values[0], StringComparison.Ordinal)))
        {
            return values.Count == 0 ? string.Empty : values[0];
        }

        return forExport
            ? string.Join(",", values.Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : null;
    }

    /// <summary>
    /// What one part says in a column: a field's text as shown, or a flag as KiCad's text variable resolves it —
    /// "DNP", "Excluded from BOM" — or nothing. A field is found without regard to case, as KiCad finds one.
    /// </summary>
    public static string Value(BomPart part, string column) => column switch
    {
        BomPreset.Dnp => part.Symbol.IsDnp ? "DNP" : string.Empty,
        BomPreset.ExcludeFromBom => part.Symbol.InBom ? string.Empty : "Excluded from BOM",
        BomPreset.ExcludeFromBoard => part.Symbol.OnBoard ? string.Empty : "Excluded from board",
        BomPreset.ExcludeFromSim => part.Symbol.ExcludedFromSim ? "Excluded from simulation" : string.Empty,
        "Reference" => part.Reference,
        _ => KicadText.Unescape(part.Symbol.Fields.FirstOrDefault(f => string.Equals(f.Name, column, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty),
    };

    /// <summary>
    /// The bill as KiCad's CSV export writes it (<c>FIELDS_EDITOR_GRID_DATA_MODEL::Export</c> with the "CSV" format
    /// preset): the shown columns under their labels, every value quoted, line breaks and tabs taken out.
    /// </summary>
    public string Csv()
    {
        var shown = Columns.Where(c => c.Show).ToList();
        var text = new StringBuilder();
        text.AppendJoin(',', shown.Select(c => Quote(c.Label))).Append('\n');
        foreach (var row in Rows)
        {
            text.AppendJoin(',', shown.Select(c => Quote(Text(row, c.Name, forExport: true) ?? string.Empty))).Append('\n');
        }

        return text.ToString();

        static string Quote(string value) =>
            "\"" + value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace("\t", string.Empty, StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

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

    /// <summary>
    /// Units of one part: the same designator. One not numbered yet — R? — cannot be told apart from another, so it
    /// is never taken for a unit of it, as in KiCad's <c>unitMatch</c>.
    /// </summary>
    private static bool SameUnit(string a, string b) => !a.EndsWith('?') && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>Two parts read the same in a column lines group by; grouping by designator compares its letters only.</summary>
    private static bool SameFor(string column, BomPart a, BomPart b) =>
        string.Equals(column, "Reference", StringComparison.Ordinal)
            ? Split(a.Reference).Prefix == Split(b.Reference).Prefix
            : string.Equals(Value(a, column), Value(b, column), StringComparison.Ordinal);

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
}

/// <summary>The design's parts, and its bill as KiCad would write it — what File → Export BOM writes.</summary>
public static class SchBom
{
    /// <summary>Every placed part of the design rooted at <paramref name="rootFile"/>, in every place its sheet stands.</summary>
    public static IReadOnlyList<BomPart> Parts(string rootFile, Func<string, Schematic?>? open = null)
    {
        var parts = new List<BomPart>();
        foreach (var place in SchHierarchy.Walk(rootFile, open))
        {
            if (Read(place.File, open) is not { } sheet)
            {
                continue;
            }

            foreach (var symbol in sheet.Symbols)
            {
                parts.Add(new BomPart(symbol, symbol.ReferenceAt(place.Path) ?? symbol.Reference ?? "?", place.File, place.Path));
            }
        }

        return parts;
    }

    /// <summary>The design's bill laid out by <paramref name="preset"/>, or by the one its project names.</summary>
    public static BomTable Build(string rootFile, Func<string, Schematic?>? open = null, BomPreset? preset = null) =>
        BomTable.Build(Parts(rootFile, open), preset ?? BomPreset.Read(ProjectOf(rootFile)).Current);

    /// <summary>The bill as KiCad's CSV export writes it.</summary>
    public static string Write(string rootFile, Func<string, Schematic?>? open = null, BomPreset? preset = null) =>
        Build(rootFile, open, preset).Csv();

    /// <summary>The project file beside a root sheet: the same name, <c>.kicad_pro</c>.</summary>
    public static string? ProjectOf(string rootFile)
    {
        try
        {
            string project = Path.ChangeExtension(Path.GetFullPath(rootFile), ".kicad_pro");
            return File.Exists(project) ? project : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

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
