using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// One row of a library table: a nickname, where the library lives, and how to read it. The uri usually carries a
/// variable — <c>${KICAD9_SYMBOL_DIR}/Device.kicad_sym</c> for the libraries KiCad ships,
/// <c>${KIPRJMOD}/parts.kicad_sym</c> for one kept beside the project — so it is resolved rather than opened.
/// </summary>
public sealed class LibTableEntry(SList node)
{
    public SList Node { get; } = node;

    /// <summary>The nickname a symbol is addressed by: the <c>Device</c> of <c>Device:R</c>.</summary>
    public string Name => Node.ChildString("name") ?? string.Empty;

    /// <summary>"KiCad" for a .kicad_sym library; other kinds exist and are carried through untouched.</summary>
    public string Type => Node.ChildString("type") ?? string.Empty;

    public string Uri => Node.ChildString("uri") ?? string.Empty;

    public string Options => Node.ChildString("options") ?? string.Empty;

    public string Description => Node.ChildString("descr") ?? string.Empty;

    /// <summary>A row KiCad keeps but does not load.</summary>
    public bool IsDisabled => Node.Find("disabled") is not null;

    /// <summary>A row loaded but kept out of the chooser.</summary>
    public bool IsHidden => Node.Find("hidden") is not null;

    /// <summary>
    /// The uri with <c>${NAME}</c> replaced from <paramref name="variables"/>. A variable nobody knows is left as it
    /// stands: a path with <c>${KICAD9_SYMBOL_DIR}</c> still in it says plainly what is missing, where a blank or a
    /// guessed path would only say the file was not found.
    /// </summary>
    public string Resolve(IReadOnlyDictionary<string, string> variables)
    {
        string uri = Uri;
        if (uri.Length == 0 || !uri.Contains("${", StringComparison.Ordinal))
        {
            return uri;
        }

        foreach (var (name, value) in variables)
        {
            uri = uri.Replace("${" + name + "}", value, StringComparison.Ordinal);
        }

        return uri;
    }
}

/// <summary>
/// A <c>sym-lib-table</c> (or <c>fp-lib-table</c>): the list of libraries a project or an installation knows, in the
/// order they are searched. KiCad keeps two — one global, one beside the project — and a nickname in the project's
/// table wins over the same nickname globally.
///
/// Read through the lossless tree like everything else, so a table that is read and written back is unchanged.
/// </summary>
public sealed class SymLibTable
{
    private readonly List<LibTableEntry> _entries = [];

    private SymLibTable(SDocument document, string expectedHead)
    {
        Document = document;
        Root = document.Roots.OfType<SList>().FirstOrDefault()
            ?? throw new KiCadFormatException("File contains no S-expression.");

        if (Root.Head != expectedHead)
        {
            throw new KiCadFormatException($"Expected ({expectedHead} ...), found ({Root.Head} ...).");
        }

        foreach (var child in Root.Lists().Where(l => l.Head == "lib"))
        {
            _entries.Add(new LibTableEntry(child));
        }
    }

    public SDocument Document { get; }

    public SList Root { get; }

    /// <summary>Table format version; 7 is what KiCad 8 and later write.</summary>
    public int Version => Root.Find("version") is { } v && int.TryParse(v.AtomAt(1)?.Raw, out int version) ? version : 0;

    /// <summary>The rows, in the order the file lists them — which is the order they are searched.</summary>
    public IReadOnlyList<LibTableEntry> Entries => _entries;

    /// <summary>The row with this nickname, or null. Names are compared as KiCad compares them: exactly.</summary>
    public LibTableEntry? Find(string name) =>
        _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    public static SymLibTable Load(string path) => new(SDocument.Load(path), "sym_lib_table");

    public static SymLibTable Parse(string text) => new(SDocument.Parse(text), "sym_lib_table");

    /// <summary>The footprint side of the same file format, read the same way.</summary>
    public static SymLibTable LoadFootprints(string path) => new(SDocument.Load(path), "fp_lib_table");

    public void Save(string path) => Document.Save(path);
}
