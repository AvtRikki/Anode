using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// A <c>.kicad_sym</c> file: the definitions a schematic draws from. The same <see cref="LibSymbol"/> that reads a
/// definition carried inside a sheet reads one here — a library is the same shape, one level up, which is why a
/// symbol can be copied from a library into a sheet's <c>lib_symbols</c> without being rewritten.
///
/// Nothing is copied out of the tree, so a library that is read and written back is byte for byte what it was.
/// </summary>
public sealed class SymbolLibrary : INodeHost
{
    private readonly Dictionary<string, LibSymbol> _byName = new(StringComparer.Ordinal);
    private readonly List<LibSymbol> _symbols = [];

    private SymbolLibrary(SDocument document)
    {
        Document = document;
        Root = document.Roots.OfType<SList>().FirstOrDefault()
            ?? throw new KiCadFormatException("File contains no S-expression.");

        if (Root.Head != "kicad_symbol_lib")
        {
            throw new KiCadFormatException($"Expected (kicad_symbol_lib ...), found ({Root.Head} ...).");
        }

        Reindex();
    }

    /// <summary>The version a new library is written in: KiCad 9's symbol library format.</summary>
    public const int NewVersion = 20241209;

    /// <summary>An empty library, written as KiCad 9 writes one.</summary>
    public static string EmptyText =>
        $"(kicad_symbol_lib\n\t(version {NewVersion})\n\t(generator \"anode\")\n\t(generator_version \"0.1\")\n)\n";

    /// <summary>
    /// Why a name will not do for a new symbol, or null when it will: it must be something, must not be taken, and
    /// must not carry what a <c>lib_id</c> or the file's own syntax would read as something else.
    /// </summary>
    public string? NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "empty";
        }

        if (name.IndexOfAny([':', '/', '\\', '"', '\n', '\r', '\t']) >= 0)
        {
            return "characters";
        }

        return _byName.ContainsKey(name) ? "taken" : null;
    }

    /// <summary>
    /// A new symbol, as KiCad's New Symbol makes one with its defaults (<c>LIB_SYMBOL</c>'s constructor and
    /// <c>CreateSymbol</c>): the reference and value shown, the value the symbol's name; footprint, datasheet and
    /// description there but hidden; pin names 20 mil in from the body; in the bill and on the board; one unit and
    /// one body, with an empty body to draw in. KiCad puts both shown fields on the origin, on top of each other;
    /// here they stand a grid apart, reference above and value below, so both can be read and taken hold of.
    /// Not yet in the library: <see cref="Attach"/> puts it there.
    /// </summary>
    public static LibSymbol NewSymbol(string name, string reference = "U")
    {
        static string Q(string text) => SEscape.Quote(text);
        const string font = "(effects (font (size 1.27 1.27)))";
        const string hidden = "(effects (font (size 1.27 1.27)) (hide yes))";

        string text =
            $"(symbol {Q(name)} (pin_names (offset 0.508)) (exclude_from_sim no) (in_bom yes) (on_board yes)"
            + $" (property \"Reference\" {Q(reference)} (at 0 2.54 0) {font})"
            + $" (property \"Value\" {Q(name)} (at 0 -2.54 0) {font})"
            + $" (property \"Footprint\" \"\" (at 0 0 0) {hidden})"
            + $" (property \"Datasheet\" \"\" (at 0 0 0) {hidden})"
            + $" (property \"Description\" \"\" (at 0 0 0) {hidden})"
            + $" (symbol {Q(name + "_1_1")})"
            + " (embedded_fonts no))";

        return new LibSymbol(Editing.SchNodes.Adopt(SDocument.Parse(text).Root));
    }

    /// <summary>
    /// Takes a symbol out of the library, answering where it stood so that <see cref="Attach"/> can put it back
    /// there — what makes deleting a symbol a step to undo.
    /// </summary>
    public int Detach(INodeItem item)
    {
        int index = Root.IndexOf(item.Node);
        if (index < 0)
        {
            throw new InvalidOperationException("The symbol is not in this library.");
        }

        Root.RemoveAt(index);
        Reindex();
        return index;
    }

    /// <summary>Puts a symbol into the library at a place in the file; past the end, it goes last, as KiCad adds one.</summary>
    public void Attach(INodeItem item, int index)
    {
        Root.Insert(Math.Clamp(index, 0, Root.Count), item.Node);
        Reindex(item as LibSymbol);
    }

    /// <summary>
    /// Reads which symbols the file holds, keeping the wrappers of those that were already there — what is selected
    /// or shown goes on being the same object across an add or a delete.
    /// </summary>
    private void Reindex(LibSymbol? arriving = null)
    {
        var known = new Dictionary<SList, LibSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var symbol in _symbols.Append(arriving).OfType<LibSymbol>())
        {
            known[symbol.Node] = symbol;
        }

        _symbols.Clear();
        _byName.Clear();
        foreach (var child in Root.Lists().Where(l => l.Head == "symbol"))
        {
            var symbol = known.TryGetValue(child, out var existing) ? existing : new LibSymbol(child);
            _symbols.Add(symbol);

            // A library with two symbols of one name is malformed; the first is the one KiCad would find.
            _byName.TryAdd(symbol.Name, symbol);
        }
    }

    public SDocument Document { get; }

    public SList Root { get; }

    public int Version => Root.Find("version") is { } v && int.TryParse(v.AtomAt(1)?.Raw, out int version) ? version : 0;

    public string? Generator => Root.ChildString("generator");

    public string? GeneratorVersion => Root.ChildString("generator_version");

    public bool IsSupportedVersion => Version >= KiCadFormat.OldestSupported;

    public bool IsNewerThanKnown => Version > KiCadFormat.NewestKnown;

    /// <summary>The definitions, in the order the file lists them.</summary>
    public IReadOnlyList<LibSymbol> Symbols => _symbols;

    /// <summary>A definition by its name, without the library nickname: the <c>R</c> of <c>Device:R</c>.</summary>
    public LibSymbol? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// A definition by its full <c>lib_id</c>. The nickname is checked against <paramref name="nickname"/> when one
    /// is given, so asking the wrong library for <c>Device:R</c> answers nothing rather than the wrong symbol.
    /// </summary>
    public LibSymbol? FindByLibId(string libId, string? nickname = null)
    {
        int colon = libId.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return Find(libId);
        }

        string prefix = libId[..colon];
        return nickname is not null && !string.Equals(prefix, nickname, StringComparison.Ordinal)
            ? null
            : Find(libId[(colon + 1)..]);
    }

    public static SymbolLibrary Load(string path) => new(SDocument.Load(path));

    public static SymbolLibrary Parse(string text) => new(SDocument.Parse(text));

    public void Save(string path) => Document.Save(path);
}
