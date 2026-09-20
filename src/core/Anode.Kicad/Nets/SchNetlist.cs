using System.Globalization;
using System.Text;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// The design as a netlist, in KiCad's own <c>(export (version "E") …)</c> form — the file pcbnew reads to pull a
/// board from a schematic, and the one other tools have learnt.
///
/// The rules that decide what lands in it are KiCad's (netlist_exporter_xml.cpp): nets in order of their names,
/// nodes in order of designator and pin, a pin appearing twice on one net written once, power symbols — whose
/// designator starts with "#" — never a node, a net left with no nodes not written at all, and the net's code its
/// place in the sorted list, so the numbers may have gaps where such a net was dropped.
/// </summary>
public static class SchNetlist
{
    /// <summary>
    /// Writes the netlist of the design rooted at <paramref name="rootFile"/>.
    /// </summary>
    /// <param name="open">Where a sheet comes from when not straight from disk: an open tab has the newer text.</param>
    /// <param name="tool">What made the file, as KiCad writes its own name and version there.</param>
    /// <param name="when">The moment to record; the current time when not given.</param>
    public static string Write(string rootFile, Func<string, Schematic?>? open = null, string tool = "Anode", DateTime? when = null)
    {
        var places = SchHierarchy.Walk(rootFile, open);
        var nets = SchDesignNets.Build(rootFile, open);
        var sheets = new Dictionary<string, Schematic>(StringComparer.Ordinal);

        foreach (var place in places)
        {
            if (!sheets.ContainsKey(place.File) && Read(place.File, open) is { } sheet)
            {
                sheets[place.File] = sheet;
            }
        }

        var text = new StringBuilder();
        text.Append("(export (version \"E\")\n");
        Design(text, rootFile, places, sheets, tool, when ?? DateTime.Now);
        Components(text, places, sheets);
        LibParts(text, places, sheets);
        Nets(text, nets);
        text.Append(')');
        return text.ToString();
    }

    private static Schematic? Read(string file, Func<string, Schematic?>? open)
    {
        try
        {
            return open?.Invoke(file) ?? (File.Exists(file) ? Schematic.Load(file) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException)
        {
            return null;
        }
    }

    private static void Design(
        StringBuilder text,
        string rootFile,
        IReadOnlyList<SheetInstance> places,
        Dictionary<string, Schematic> sheets,
        string tool,
        DateTime when)
    {
        text.Append("  (design\n");
        text.Append($"    (source {Quote(Path.GetFullPath(rootFile))})\n");
        text.Append($"    (date {Quote(when.ToString("ddd dd MMM yyyy hh:mm:ss tt", CultureInfo.InvariantCulture))})\n");
        text.Append($"    (tool {Quote(tool)})\n");

        for (int i = 0; i < places.Count; i++)
        {
            var place = places[i];
            var block = sheets.TryGetValue(place.File, out var sheet) ? sheet.TitleBlock : SchTitleBlock.Empty;
            text.Append($"    (sheet (number \"{i + 1}\") (name {Quote(place.Trail)}) (tstamps {Quote(Stamps(place))})\n");
            text.Append("      (title_block\n");
            Field(text, "title", block.Title);
            Field(text, "company", block.Company);
            Field(text, "rev", block.Revision);
            Field(text, "date", block.Date);
            text.Append($"        (source {Quote(Path.GetFileName(place.File))})\n");
            for (int number = 1; number <= Editing.TitleBlockWrites.CommentCount; number++)
            {
                text.Append($"        (comment (number \"{number}\") (value {Quote(block.Comment(number))}))");
                text.Append(number == Editing.TitleBlockWrites.CommentCount ? "))\n" : "\n");
            }
        }

        text.Append("  )\n");

        static void Field(StringBuilder text, string name, string? value) =>
            text.Append(string.IsNullOrEmpty(value) ? $"        ({name})\n" : $"        ({name} {Quote(value)})\n");
    }

    /// <summary>The parts of the design, one entry per place a part stands in, as KiCad lists them.</summary>
    private static void Components(StringBuilder text, IReadOnlyList<SheetInstance> places, Dictionary<string, Schematic> sheets)
    {
        text.Append("  (components\n");
        var written = new List<string>();

        foreach (var place in places)
        {
            if (!sheets.TryGetValue(place.File, out var sheet))
            {
                continue;
            }

            foreach (var symbol in sheet.Symbols.OrderBy(s => s.ReferenceAt(place.Path) ?? s.Reference ?? string.Empty, StringComparer.Ordinal))
            {
                string reference = symbol.ReferenceAt(place.Path) ?? symbol.Reference ?? "?";
                if (reference.StartsWith('#'))
                {
                    continue;
                }

                var entry = new StringBuilder();
                entry.Append($"    (comp (ref {Quote(reference)})\n");
                entry.Append($"      (value {Quote(symbol.Value ?? string.Empty)})\n");
                if (Field(symbol, "Footprint") is { Length: > 0 } footprint)
                {
                    entry.Append($"      (footprint {Quote(footprint)})\n");
                }

                if (Field(symbol, "Datasheet") is { Length: > 0 } datasheet && datasheet != "~")
                {
                    entry.Append($"      (datasheet {Quote(datasheet)})\n");
                }

                string libId = symbol.LibId;
                int colon = libId.IndexOf(':');
                entry.Append($"      (libsource (lib {Quote(colon < 0 ? string.Empty : libId[..colon])})"
                    + $" (part {Quote(colon < 0 ? libId : libId[(colon + 1)..])})"
                    + $" (description {Quote(symbol.Definition?.Description ?? string.Empty)}))\n");

                foreach (var field in symbol.Fields.Where(f => f.Name is not ("Reference" or "Value" or "Footprint" or "Datasheet")))
                {
                    entry.Append($"      (property (name {Quote(field.Name)}) (value {Quote(field.Value)}))\n");
                }

                entry.Append($"      (property (name \"Sheetname\") (value {Quote(place.Depth == 0 ? "Root" : place.Name)}))\n");
                entry.Append($"      (property (name \"Sheetfile\") (value {Quote(Path.GetFileName(place.File))}))\n");
                entry.Append($"      (sheetpath (names {Quote(place.Trail)}) (tstamps {Quote(Stamps(place))}))\n");
                entry.Append($"      (tstamps {Quote(symbol.Uuid ?? string.Empty)}))");
                written.Add(entry.ToString());
            }
        }

        text.Append(string.Join('\n', written));
        text.Append(written.Count > 0 ? ")\n" : "  )\n");
    }

    /// <summary>The definitions the parts were drawn from, with their pins, as a board's importer wants them.</summary>
    private static void LibParts(StringBuilder text, IReadOnlyList<SheetInstance> places, Dictionary<string, Schematic> sheets)
    {
        var definitions = new SortedDictionary<string, LibSymbol>(StringComparer.Ordinal);
        foreach (var place in places)
        {
            if (!sheets.TryGetValue(place.File, out var sheet))
            {
                continue;
            }

            foreach (var (name, definition) in sheet.LibrarySymbols)
            {
                definitions.TryAdd(name, definition);
            }
        }

        text.Append("  (libparts\n");
        var written = new List<string>();
        foreach (var (name, definition) in definitions)
        {
            int colon = name.IndexOf(':');
            var entry = new StringBuilder();
            entry.Append($"    (libpart (lib {Quote(colon < 0 ? string.Empty : name[..colon])}) (part {Quote(colon < 0 ? name : name[(colon + 1)..])})\n");
            if (definition.Description is { Length: > 0 } description)
            {
                entry.Append($"      (description {Quote(description)})\n");
            }

            var fields = definition.Node.Lists()
                .Where(l => l.Head == "property" && l.Str(2) is { Length: > 0 })
                .Select(l => (Name: l.Str(1) ?? string.Empty, Value: l.Str(2) ?? string.Empty))
                .ToList();
            if (fields.Count > 0)
            {
                entry.Append("      (fields\n");
                entry.Append(string.Join('\n', fields.Select(f => $"        (field (name {Quote(f.Name)}) {Quote(f.Value)})")));
                entry.Append(")\n");
            }

            var pins = definition.Pins.Where(p => p.Number.Length > 0)
                .OrderBy(p => p.Number, StringComparer.Ordinal)
                .ToList();
            if (pins.Count > 0)
            {
                entry.Append("      (pins\n");
                entry.Append(string.Join('\n', pins.Select(p =>
                    $"        (pin (num {Quote(p.Number)}) (name {Quote(p.Name)}) (type {Quote(p.ElectricalType)}))")));
                entry.Append(')');
            }

            entry.Append(')');
            written.Add(entry.ToString());
        }

        text.Append(string.Join('\n', written));
        text.Append(written.Count > 0 ? ")\n" : "  )\n");
    }

    private static void Nets(StringBuilder text, IReadOnlyList<DesignNet> nets)
    {
        text.Append("  (nets\n");

        var ordered = nets.OrderBy(n => n.Name, NaturalOrder.Instance).ToList();
        var written = new List<string>();

        for (int i = 0; i < ordered.Count; i++)
        {
            // A pin of a power symbol is not a node — the symbol is a name for the net, not a part on it — and the
            // same pin found twice, as a multi-unit part's shared pin is, is one node.
            var nodes = ordered[i].Pins
                .Where(p => !p.Reference.StartsWith('#'))
                .GroupBy(p => (p.Reference, p.Pin.Number))
                .Select(g => g.First())
                .OrderBy(p => p.Reference, StringComparer.Ordinal)
                .ThenBy(p => p.Pin.Number, StringComparer.Ordinal)
                .ToList();

            if (nodes.Count == 0)
            {
                continue;
            }

            var entry = new StringBuilder();
            entry.Append($"    (net (code \"{i + 1}\") (name {Quote(ordered[i].Name)})\n");
            entry.Append(string.Join('\n', nodes.Select(node =>
            {
                string function = node.Pin.Pin.Name is { Length: > 0 } pinName && pinName != "~"
                    ? $" (pinfunction {Quote(pinName)})"
                    : string.Empty;
                return $"      (node (ref {Quote(node.Reference)}) (pin {Quote(node.Pin.Number)}){function} (pintype {Quote(node.Pin.Pin.ElectricalType)}))";
            })));
            entry.Append(')');
            written.Add(entry.ToString());
        }

        text.Append(string.Join('\n', written));
        text.Append(written.Count > 0 ? ")" : "  )");
    }

    /// <summary>The path of uuids as KiCad writes it in a netlist: the root's own uuid is not part of it.</summary>
    private static string Stamps(SheetInstance place)
    {
        int slash = place.Path.IndexOf('/', 1);
        return slash < 0 ? "/" : place.Path[slash..] + "/";
    }

    private static string? Field(SymbolInstance symbol, string name) =>
        symbol.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))?.Value;

    private static string Quote(string? value) => SEscape.Quote(value ?? string.Empty);

    /// <summary>
    /// KiCad's own ordering of net names (<c>StrNumCmp</c>): digits inside a name compare as numbers, so /D2 comes
    /// before /D10.
    /// </summary>
    private sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            ReadOnlySpan<char> a = x ?? string.Empty, b = y ?? string.Empty;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int ia = i, jb = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        i++;
                    }

                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        j++;
                    }

                    var left = a[ia..i].TrimStart('0');
                    var right = b[jb..j].TrimStart('0');
                    if (left.Length != right.Length)
                    {
                        return left.Length - right.Length;
                    }

                    int digits = left.SequenceCompareTo(right);
                    if (digits != 0)
                    {
                        return digits;
                    }

                    continue;
                }

                if (a[i] != b[j])
                {
                    return a[i] - b[j];
                }

                i++;
                j++;
            }

            return (a.Length - i) - (b.Length - j);
        }
    }
}
