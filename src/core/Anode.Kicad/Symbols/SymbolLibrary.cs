using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// A <c>.kicad_sym</c> file: the definitions a schematic draws from. The same <see cref="LibSymbol"/> that reads a
/// definition carried inside a sheet reads one here — a library is the same shape, one level up, which is why a
/// symbol can be copied from a library into a sheet's <c>lib_symbols</c> without being rewritten.
///
/// Nothing is copied out of the tree, so a library that is read and written back is byte for byte what it was.
/// </summary>
public sealed class SymbolLibrary
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

        foreach (var child in Root.Lists().Where(l => l.Head == "symbol"))
        {
            var symbol = new LibSymbol(child);
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
