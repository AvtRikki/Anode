using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>Header fields of a sheet, shown in the frame around the drawing.</summary>
public sealed record SchTitleBlock(string? Title, string? Date, string? Revision, string? Company);

/// <summary>
/// A <c>.kicad_sch</c> file: the lossless CST plus typed views over its items. Like <see cref="Board"/>, nothing is
/// copied out of the tree — the file stays the source of truth, so saving an unedited schematic is byte-identical.
/// </summary>
public sealed class Schematic
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

    public SchTitleBlock TitleBlock => Root.Find("title_block") is { } block
        ? new SchTitleBlock(block.ChildString("title"), block.ChildString("date"), block.ChildString("rev"), block.ChildString("company"))
        : new SchTitleBlock(null, null, null, null);

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

    /// <summary>Top-level lists this model does not interpret. Kept in the file untouched.</summary>
    public IReadOnlyList<SList> OtherItems => _other;

    /// <summary>Everything drawn on the sheet, in a stable order.</summary>
    public IEnumerable<SchItem> Items =>
        _graphics.Cast<SchItem>().Concat(_wires).Concat(_busEntries).Concat(_sheets).Concat(_symbols)
            .Concat(_junctions).Concat(_noConnects).Concat(_labels).Concat(_texts);

    public static Schematic Load(string path) => new(SDocument.Load(path));

    public static Schematic Parse(string text) => new(SDocument.Parse(text));

    /// <summary>Definition for a placed symbol, if the file carries one.</summary>
    public LibSymbol? Definition(SymbolInstance symbol) => _librarySymbols.GetValueOrDefault(symbol.LibId);

    public void Save(string path) => Document.Save(path);
}
