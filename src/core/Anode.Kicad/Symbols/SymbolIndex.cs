using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>One library the index knows: where it came from, and what went wrong if anything did.</summary>
/// <param name="Nickname">The name a lib_id addresses it by.</param>
/// <param name="Path">The resolved path, which may not exist.</param>
/// <param name="IsProject">From the project's table rather than the installation's.</param>
public sealed record SymbolLibraryRef(string Nickname, string Path, bool IsProject)
{
    /// <summary>Why the library could not be read, or null while it has not been tried or was read.</summary>
    public string? Problem { get; internal set; }

    /// <summary>
    /// Whether this library is offered. A project may know of more libraries than it wants to see at once, and
    /// turning one off is not the same as removing it: the row stays, so it can be turned back on.
    /// </summary>
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>The library, loaded on first use; null when it could not be read.</summary>
    public SymbolLibrary? Library { get; internal set; }
}

/// <summary>
/// Every library a project can draw from: the installation's table and the project's own, merged the way KiCad
/// merges them — a nickname in the project's table wins, because a part kept beside the project is meant to be the
/// one that is used.
///
/// Libraries are opened on first use. A row whose file is missing is not an error that stops the rest: it is
/// remembered against that row, so the chooser can say which library is unreadable instead of showing nothing.
/// </summary>
public sealed class SymbolIndex
{
    private readonly List<SymbolLibraryRef> _libraries = [];

    private SymbolIndex(IEnumerable<SymbolLibraryRef> libraries)
    {
        foreach (var library in libraries)
        {
            // First nickname wins; the project's table is offered first, so its row is the one that stands.
            if (!_libraries.Any(l => string.Equals(l.Nickname, library.Nickname, StringComparison.Ordinal)))
            {
                _libraries.Add(library);
            }
        }
    }

    /// <summary>The libraries, project ones first, each nickname appearing once.</summary>
    public IReadOnlyList<SymbolLibraryRef> Libraries => _libraries;

    /// <summary>
    /// Builds the index from the two tables. <paramref name="variables"/> resolves the <c>${…}</c> of each uri;
    /// <c>KIPRJMOD</c> is the project's own folder and is added when <paramref name="projectDirectory"/> is given.
    /// </summary>
    public static SymbolIndex Build(
        SymLibTable? project,
        SymLibTable? global,
        IReadOnlyDictionary<string, string>? variables = null,
        string? projectDirectory = null)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in variables ?? new Dictionary<string, string>())
        {
            resolved[name] = value;
        }

        if (projectDirectory is { Length: > 0 })
        {
            resolved["KIPRJMOD"] = projectDirectory;
        }

        return new SymbolIndex([.. Rows(project, resolved, isProject: true), .. Rows(global, resolved, isProject: false)]);
    }

    private static IEnumerable<SymbolLibraryRef> Rows(SymLibTable? table, IReadOnlyDictionary<string, string> variables, bool isProject)
    {
        if (table is null)
        {
            return [];
        }

        return table.Entries
            .Where(entry => !entry.IsDisabled)
            .Select(entry => new SymbolLibraryRef(entry.Name, entry.Resolve(variables), isProject));
    }

    /// <summary>Turns a library on or off by nickname; unknown nicknames are ignored.</summary>
    public void SetEnabled(string nickname, bool enabled)
    {
        if (_libraries.FirstOrDefault(l => string.Equals(l.Nickname, nickname, StringComparison.Ordinal)) is { } row)
        {
            row.IsEnabled = enabled;
        }
    }

    /// <summary>The library of this nickname, read if it has not been read yet; null when there is no such row.</summary>
    public SymbolLibraryRef? Open(string nickname)
    {
        var row = _libraries.FirstOrDefault(l => string.Equals(l.Nickname, nickname, StringComparison.Ordinal));
        if (row is null)
        {
            return null;
        }

        Open(row);
        return row;
    }

    private static void Open(SymbolLibraryRef row)
    {
        if (row.Library is not null || row.Problem is not null)
        {
            return;
        }

        try
        {
            // Through the cache: the same libraries are wanted again every time a sheet is opened.
            row.Library = SymbolLibraryCache.Load(row.Path);
        }
        catch (Exception ex) when (ex is IOException or KiCadFormatException or UnauthorizedAccessException)
        {
            // Remembered against the row rather than thrown: one unreadable library must not empty the chooser.
            row.Problem = ex.Message;
        }
    }

    /// <summary>The definition a <c>lib_id</c> names, e.g. <c>Device:R</c>; null when nothing answers to it.</summary>
    public LibSymbol? Find(string libId)
    {
        int colon = libId.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            // Unqualified: the first library that has such a symbol, in search order.
            foreach (var row in _libraries.Where(l => l.IsEnabled))
            {
                Open(row);
                if (row.Library?.Find(libId) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        return Open(libId[..colon]) is { IsEnabled: true } named ? named.Library?.Find(libId[(colon + 1)..]) : null;
    }

    /// <summary>
    /// Symbols whose name, library or description contains <paramref name="text"/>, case-insensitively. An empty
    /// query lists everything, which is what a chooser shows before anything is typed.
    /// </summary>
    public IEnumerable<(string LibId, LibSymbol Symbol)> Search(string text, int limit = 200)
    {
        int found = 0;
        foreach (var row in _libraries.Where(l => l.IsEnabled))
        {
            Open(row);
            if (row.Library is not { } library)
            {
                continue;
            }

            foreach (var symbol in library.Symbols)
            {
                string libId = $"{row.Nickname}:{symbol.Name}";
                if (text.Length == 0
                    || libId.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || Describes(symbol, text))
                {
                    yield return (libId, symbol);
                    if (++found >= limit)
                    {
                        yield break;
                    }
                }
            }
        }
    }

    private static bool Describes(LibSymbol symbol, string text) =>
        symbol.Node.Lists().Any(l =>
            l.Head == "property"
            && l.Str(1) is "Description" or "Keywords"
            && l.Str(2)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
}
