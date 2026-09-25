using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// The title block of a sheet or a board — both files keep it the same way, as <c>(title_block …)</c> under the root —
/// shown in the frame around the drawing.
/// </summary>
public sealed record SchTitleBlock(string? Title, string? Date, string? Revision, string? Company)
{
    public static readonly SchTitleBlock Empty = new(null, null, null, null);

    /// <summary>The numbered comment lines, 1 to 9, by number; a missing one is empty.</summary>
    public IReadOnlyDictionary<int, string> Comments { get; init; } = new Dictionary<int, string>();

    public string Comment(int number) => Comments.TryGetValue(number, out var text) ? text : string.Empty;

    /// <summary>Reads the title block under a file's root; <see cref="Empty"/> when there is none.</summary>
    public static SchTitleBlock Read(SList root) => root.Find("title_block") is { } block
        ? new SchTitleBlock(block.ChildString("title"), block.ChildString("date"), block.ChildString("rev"), block.ChildString("company"))
        {
            Comments = block.Lists()
                .Where(l => l.Head == "comment" && l.AtomAt(1)?.TryGetDouble(out _) == true)
                .GroupBy(l => (int)l.AtomAt(1)!.AsDouble())
                .ToDictionary(g => g.Key, g => g.First().Str(2) ?? string.Empty),
        }
        : Empty;
}

/// <summary>
/// A <c>.kicad_sch</c> file: the lossless CST plus typed views over its items. Like <see cref="Board"/>, nothing is
/// copied out of the tree — the file stays the source of truth, so saving an unedited schematic is byte-identical.
/// </summary>
public sealed class Schematic : INodeHost
{
    private readonly Dictionary<string, LibSymbol> _librarySymbols = new(StringComparer.Ordinal);
    private readonly List<SymbolInstance> _symbols = [];
    private readonly List<SchWire> _wires = [];
    private readonly List<SchBusEntry> _busEntries = [];
    private readonly List<SchJunction> _junctions = [];
    private readonly List<SchNoConnect> _noConnects = [];
    private readonly List<SchLabel> _labels = [];
    private readonly List<SchText> _texts = [];
    private readonly List<SchGraphic> _graphics = [];
    private readonly List<SchSheet> _sheets = [];
    private readonly List<SchImage> _images = [];
    private readonly List<SchRuleArea> _ruleAreas = [];
    private readonly List<SchTable> _tables = [];
    private readonly List<SchGroup> _groups = [];
    private readonly List<SList> _other = [];

    private Schematic(SDocument document)
    {
        Document = document;
        Root = document.Roots.OfType<SList>().FirstOrDefault()
            ?? throw new KiCadFormatException("File contains no S-expression.");

        if (Root.Head != "kicad_sch")
        {
            throw new KiCadFormatException($"Expected (kicad_sch ...), found ({Root.Head} ...).");
        }

        Version = Root.Find("version") is { } v && int.TryParse(v.AtomAt(1)?.Raw, out int version) ? version : 0;

        if (Root.Find("lib_symbols") is { } library)
        {
            foreach (var symbol in library.Lists().Where(l => l.Head == "symbol"))
            {
                var parsed = new LibSymbol(symbol);
                _librarySymbols[parsed.Name] = parsed;
            }
        }

        foreach (var child in Root.Lists())
        {
            switch (child.Head)
            {
                case "symbol":
                    _symbols.Add(new SymbolInstance(child, this));
                    break;
                case "wire" or "bus":
                    _wires.Add(new SchWire(child));
                    break;
                case "bus_entry":
                    _busEntries.Add(new SchBusEntry(child));
                    break;
                case "junction":
                    _junctions.Add(new SchJunction(child));
                    break;
                case "no_connect":
                    _noConnects.Add(new SchNoConnect(child));
                    break;
                case "label" or "global_label" or "hierarchical_label" or "netclass_flag":
                    _labels.Add(new SchLabel(child));
                    break;
                case "text" or "text_box":
                    _texts.Add(new SchText(child));
                    break;
                case "sheet":
                    _sheets.Add(new SchSheet(child));
                    break;
                case "image":
                    _images.Add(new SchImage(child));
                    break;
                case "rule_area":
                    _ruleAreas.Add(new SchRuleArea(child));
                    break;
                case "table":
                    _tables.Add(new SchTable(child));
                    break;
                case "group":
                    _groups.Add(new SchGroup(child));
                    break;
                case var head when SchGraphic.IsGraphicHead(head):
                    _graphics.Add(new SchGraphic(child));
                    break;
                case "version" or "generator" or "generator_version" or "uuid" or "paper" or "title_block"
                    or "lib_symbols" or "sheet_instances" or "symbol_instances" or "embedded_fonts" or "bus_alias":
                    break;
                default:
                    _other.Add(child);
                    break;
            }
        }
    }

    public SDocument Document { get; }

    public SList Root { get; }

    public int Version { get; }

    public string? Generator => Root.ChildString("generator");

    public string? GeneratorVersion => Root.ChildString("generator_version");

    public string? Uuid => Root.ChildString("uuid");

    /// <summary>Sheet size name: "A4", "USLetter"…</summary>
    public string Paper => Root.ChildString("paper") ?? "A4";

    /// <summary>Sheets are landscape unless the file says <c>(paper "A4" portrait)</c>.</summary>
    public bool IsPortrait => Root.Find("paper")?.Str(2) == "portrait";

    public bool IsSupportedVersion => Version >= KiCadFormat.OldestSupported;

    public bool IsNewerThanKnown => Version > KiCadFormat.NewestKnown;

    public SchTitleBlock TitleBlock => SchTitleBlock.Read(Root);

    /// <summary>Symbol definitions carried inside the file, keyed by their <c>lib_id</c>.</summary>
    public IReadOnlyDictionary<string, LibSymbol> LibrarySymbols => _librarySymbols;

    public IReadOnlyList<SymbolInstance> Symbols => _symbols;

    /// <summary>Wires and buses; a bus reports <see cref="SchWire.IsBus"/>.</summary>
    public IReadOnlyList<SchWire> Wires => _wires;

    public IReadOnlyList<SchBusEntry> BusEntries => _busEntries;

    public IReadOnlyList<SchJunction> Junctions => _junctions;

    public IReadOnlyList<SchNoConnect> NoConnects => _noConnects;

    public IReadOnlyList<SchLabel> Labels => _labels;

    public IReadOnlyList<SchText> Texts => _texts;

    /// <summary>Free graphics drawn on the sheet itself, outside any symbol.</summary>
    public IReadOnlyList<SchGraphic> Graphics => _graphics;

    /// <summary>Child sheets of the hierarchy.</summary>
    public IReadOnlyList<SchSheet> Sheets => _sheets;

    /// <summary>Pictures the sheet carries — a logo, a scan, a note drawn elsewhere.</summary>
    public IReadOnlyList<SchImage> Images => _images;

    /// <summary>Areas the design rules are told about.</summary>
    public IReadOnlyList<SchRuleArea> RuleAreas => _ruleAreas;

    /// <summary>Tables drawn on the sheet.</summary>
    public IReadOnlyList<SchTable> Tables => _tables;

    /// <summary>
    /// Groups: items that select and move as one. A group is not drawn — it names its members by id — and a group
    /// may be a member of another.
    /// </summary>
    public IReadOnlyList<SchGroup> Groups => _groups;

    /// <summary>Top-level lists this model does not interpret. Kept in the file untouched.</summary>
    public IReadOnlyList<SList> OtherItems => _other;

    /// <summary>
    /// Group buses the sheet declares: a name and the members it stands for, as
    /// <c>(bus_alias "DPHY" (members "D0_N" "D0_P" …))</c>. A bus named after one of these carries exactly these
    /// nets, where a vector bus spells its members out in its own name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> BusAliases
    {
        get
        {
            var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var alias in Root.Lists().Where(l => l.Head == "bus_alias"))
            {
                if (alias.Str(1) is not { Length: > 0 } name)
                {
                    continue;
                }

                var members = alias.Find("members") is { } list
                    ? list.Skip(1).OfType<SAtom>().Select(a => a.Value).Where(m => m.Length > 0).ToList()
                    : [];

                aliases[name] = members;
            }

            return aliases;
        }
    }

    /// <summary>Everything drawn on the sheet, in a stable order.</summary>
    public IEnumerable<SchItem> Items =>
        // Pictures come first: KiCad draws them behind everything, and a logo over the wiring would hide it.
        _images.Cast<SchItem>().Concat(_ruleAreas).Concat(_graphics).Concat(_wires).Concat(_busEntries).Concat(_sheets).Concat(_symbols)
            .Concat(_junctions).Concat(_noConnects).Concat(_labels).Concat(_texts).Concat(_tables);

    /// <summary>
    /// Types one top-level list of a sheet the way loading does. Null for the header lists that are not drawn items
    /// — the version, the title block, the library. A copied item is typed through here, so a clone is the same kind
    /// of thing as what it was copied from, decided in one place rather than two.
    /// </summary>
    public SchItem? Wrap(SList node) => node.Head switch
    {
        "symbol" => new SymbolInstance(node, this),
        "wire" or "bus" => new SchWire(node),
        "bus_entry" => new SchBusEntry(node),
        "junction" => new SchJunction(node),
        "no_connect" => new SchNoConnect(node),
        "label" or "global_label" or "hierarchical_label" or "netclass_flag" => new SchLabel(node),
        "text" or "text_box" => new SchText(node),
        "sheet" => new SchSheet(node),
        "image" => new SchImage(node),
        "rule_area" => new SchRuleArea(node),
        "table" => new SchTable(node),
        "group" => new SchGroup(node),
        var head when SchGraphic.IsGraphicHead(head) => new SchGraphic(node),
        _ => null,
    };

    /// <summary>
    /// Removes a top-level item from the sheet and returns its index among the root's children, which
    /// <see cref="Attach"/> uses to put it back exactly where it was.
    /// </summary>
    public int Detach(SchItem item)
    {
        int index = Root.IndexOf(item.Node);
        if (index < 0)
        {
            throw new InvalidOperationException("Item is not a top-level child of this sheet.");
        }

        Root.RemoveAt(index);
        _ = item switch
        {
            SymbolInstance s => _symbols.Remove(s),
            SchWire w => _wires.Remove(w),
            SchBusEntry b => _busEntries.Remove(b),
            SchJunction j => _junctions.Remove(j),
            SchNoConnect n => _noConnects.Remove(n),
            SchLabel l => _labels.Remove(l),
            SchText t => _texts.Remove(t),
            SchGraphic g => _graphics.Remove(g),
            SchSheet sh => _sheets.Remove(sh),
            SchImage im => _images.Remove(im),
            SchRuleArea ra => _ruleAreas.Remove(ra),
            SchTable tb => _tables.Remove(tb),
            SchGroup gr => _groups.Remove(gr),
            _ => false,
        };

        return index;
    }

    /// <summary>Re-inserts an item previously removed with <see cref="Detach"/>.</summary>
    public void Attach(SchItem item, int index)
    {
        Root.Insert(Math.Clamp(index, 0, Root.Count), item.Node);
        switch (item)
        {
            case SymbolInstance s: _symbols.Add(s); break;
            case SchWire w: _wires.Add(w); break;
            case SchBusEntry b: _busEntries.Add(b); break;
            case SchJunction j: _junctions.Add(j); break;
            case SchNoConnect n: _noConnects.Add(n); break;
            case SchLabel l: _labels.Add(l); break;
            case SchText t: _texts.Add(t); break;
            case SchGraphic g: _graphics.Add(g); break;
            case SchSheet sh: _sheets.Add(sh); break;
            case SchImage im: _images.Add(im); break;
            case SchRuleArea ra: _ruleAreas.Add(ra); break;
            case SchTable tb: _tables.Add(tb); break;
            case SchGroup gr: _groups.Add(gr); break;
        }
    }

    int INodeHost.Detach(INodeItem item) => Detach((SchItem)item);

    void INodeHost.Attach(INodeItem item, int index) => Attach((SchItem)item, index);

    public static Schematic Load(string path) => new(SDocument.Load(path));

    public static Schematic Parse(string text) => new(SDocument.Parse(text));

    /// <summary>Definition for a placed symbol, if the file carries one.</summary>
    public LibSymbol? Definition(SymbolInstance symbol) => _librarySymbols.GetValueOrDefault(symbol.LibId);

    /// <summary>
    /// Registers a definition that was added to <c>lib_symbols</c> after the file was read. The lookup is built once
    /// while loading, so a part placed later would otherwise have a body in the file that the model cannot find —
    /// and a symbol whose definition cannot be found is a symbol that does not draw.
    /// </summary>
    internal void Register(LibSymbol definition) => _librarySymbols[definition.Name] = definition;

    /// <summary>Forgets a definition again, when the placement that brought it in is undone.</summary>
    internal void Unregister(string name) => _librarySymbols.Remove(name);

    public void Save(string path) => Document.Save(path);
}
