using Anode.Editing;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// A definition the sheet carries and the library it was taken from: where that library is, the symbol as the
/// library now has it, and how the sheet's copy differs.
/// </summary>
/// <param name="Row">The library's row in the tables; null when no table names the nickname.</param>
/// <param name="Fresh">The library's symbol, flattened as a sheet carries it — from the library's tab when it is open,
/// changes not yet saved included; null when there is none to be had.</param>
/// <param name="Drift">What differs between the sheet's copy and <paramref name="Fresh"/>; null when nothing does.</param>
/// <param name="Unsaved">The library is open in a tab with changes not yet written.</param>
internal sealed record LibraryLink(string LibId, SymbolLibraryRef? Row, LibSymbol? Fresh, SymbolDrift? Drift, bool Unsaved)
{
    public string Nickname => LibId.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0 ? LibId[..colon] : string.Empty;

    public string Name => LibId[(LibId.IndexOf(':', StringComparison.Ordinal) + 1)..];

    /// <summary>Why there is nothing to compare with: <c>notInTable</c>, <c>unreadable</c>, <c>missing</c>; or null.</summary>
    public string? Problem => Fresh is not null ? null
        : Row is null ? "notInTable"
        : Row.Problem is not null ? "unreadable"
        : "missing";
}

/// <summary>
/// The sheet and the symbol libraries it draws from, both ways. From a placed part to its symbol in the library, on
/// the unit it is placed as — one command, as KiCad's Edit with Symbol Editor. And back: a symbol changed in the
/// library is noticed on every sheet that carries it, told in what changed, and taken with one step to undo.
///
/// The library a sheet compares with is the one on screen: when the library is open in a tab its changes count
/// before they are saved, so a symbol fixed there can be taken onto the sheet straight away — and a sheet that took
/// something later thrown away says so again, and is put back the same way.
/// </summary>
public sealed partial class SchematicDocument
{
    /// <summary>What was found for each definition the sheet carries; forgotten on every edit and every return to the tab.</summary>
    private Dictionary<string, LibraryLink>? _links;

    /// <summary>
    /// Whether the libraries the sheet uses have been read. Reading a large one takes a moment, and it is done off
    /// the interface's thread the first time: until then the sheet simply says nothing about its libraries.
    /// </summary>
    private bool _librariesRead;
    private Task? _readingLibraries;

    /// <summary>The reading of the sheet's libraries, for tests that must wait for what it finds.</summary>
    internal Task LibrariesRead => _readingLibraries ?? Task.CompletedTask;

    /// <summary>The library a placed definition came from and how it stands; read now if it was not read yet.</summary>
    internal LibraryLink LinkOf(string libId)
    {
        _links ??= new Dictionary<string, LibraryLink>(StringComparer.Ordinal);
        if (!_links.TryGetValue(libId, out var link))
        {
            _links[libId] = link = Link(libId);
        }

        return link;
    }

    private LibraryLink Link(string libId)
    {
        var row = Libraries.RowOf(libId);
        LibSymbol? fresh;
        bool unsaved = false;
        if (row is not null && OpenLibrary(row.Path) is { } open)
        {
            string name = libId[(libId.IndexOf(':', StringComparison.Ordinal) + 1)..];
            fresh = open.Library.Find(name) is { } symbol ? open.Library.Flatten(symbol) : null;
            unsaved = open.IsDirty;
        }
        else
        {
            fresh = Libraries.FindPlaced(libId);
        }

        var drift = Sheet.LibrarySymbols.GetValueOrDefault(libId) is { } placed && fresh is not null
            ? SymbolDrift.Between(placed, fresh)
            : null;
        return new LibraryLink(libId, row, fresh, drift, unsaved);
    }

    /// <summary>The library's tab, when it is open in this window.</summary>
    private SymbolLibraryDocument? OpenLibrary(string path)
    {
        string full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return _context?.Workbench.Documents.OfType<SymbolLibraryDocument>()
            .FirstOrDefault(d => d.FilePath is { } file && string.Equals(Path.GetFullPath(file), full, comparison));
    }

    /// <summary>The definitions placed on this sheet, each once.</summary>
    private IEnumerable<string> PlacedLibIds() =>
        Sheet.Symbols.Select(s => s.LibId).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Starts reading the sheet's libraries in the background, the first time anything asks about them; when they
    /// are in, the issues and the inspector are asked again.
    /// </summary>
    private void ReadLibrariesSoon()
    {
        if (_librariesRead || _readingLibraries is not null)
        {
            return;
        }

        string[] paths = [.. PlacedLibIds().Select(Libraries.RowOf).OfType<SymbolLibraryRef>().Select(r => r.Path).Distinct(StringComparer.Ordinal)];
        var scheduler = SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
        _readingLibraries = Task.Run(() =>
        {
            foreach (string path in paths)
            {
                try
                {
                    // Into the cache that the index reads through, so the lookups after this are quick.
                    SymbolLibraryCache.Load(path);
                }
                catch (Exception ex) when (ex is IOException or KiCadFormatException or UnauthorizedAccessException)
                {
                    // The index finds the same trouble when it looks, and remembers it against the row.
                }
            }
        }).ContinueWith(
            _ =>
            {
                _librariesRead = true;
                _links = null;
                OnPropertiesChanged(nameof(Issues), nameof(Selection));
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            scheduler);
    }

    /// <summary>Every definition on the sheet its library has changed, one issue each, with the update as its action.</summary>
    private IEnumerable<Issue> LibraryDrift()
    {
        if (!_librariesRead)
        {
            ReadLibrariesSoon();
            yield break;
        }

        foreach (string libId in PlacedLibIds())
        {
            if (LinkOf(libId) is not { Drift: { } drift } link)
            {
                continue;
            }

            int placed = Sheet.Symbols.Count(s => string.Equals(s.LibId, libId, StringComparison.Ordinal));
            string detail = Tr.T("sch.issue.libraryDrift.detail", libId, Describe(drift), placed);
            yield return new Issue(
                IssueSeverity.Warning,
                Tr.T("sch.issue.libraryDrift.title"),
                link.Unsaved ? detail + " " + Tr.T("sch.issue.libraryDrift.unsaved") : detail,
                libId,
                "sch.action.update",
                () => UpdateFromLibrary(libId));
        }
    }

    /// <summary>
    /// What the inspector says of a placed part's library — only when there is something to say: that it differs,
    /// and how, or that its library cannot be found. A part in step with its library adds nothing.
    /// </summary>
    private InspectorBlock? LibraryBlock(SymbolInstance symbol)
    {
        if (symbol.LibId.Length == 0)
        {
            return null;
        }

        if (!_librariesRead)
        {
            ReadLibrariesSoon();
            return null;
        }

        var link = LinkOf(symbol.LibId);
        string title = Tr.T("sch.block.library");
        if (link.Drift is { } drift)
        {
            return new InspectorBlock(title,
            [
                new InspectorRow(Tr.T("sch.lib.link.row.differs"), Describe(drift)) { IsUnresolved = true },
                .. link.Unsaved ? new[] { new InspectorRow(Tr.T("sch.lib.link.row.library"), Tr.T("sch.lib.link.unsaved")) } : [],
            ])
            {
                IsAlert = true,
            };
        }

        return link.Problem is { } problem
            ? new InspectorBlock(title,
                [new InspectorRow(Tr.T("sch.lib.link.row.library"), Tr.T("sch.lib.link.short." + problem, link.Nickname, link.Name)) { IsUnresolved = true }])
            : null;
    }

    /// <summary>What a placed part's library lets be done from the inspector: go and edit it, and — when it changed — take it.</summary>
    private IEnumerable<InspectorAction> LibraryActions(SymbolInstance symbol)
    {
        yield return new InspectorAction(Tr.T("sch.action.editSymbol"), () => _ = EditInLibraryAsync(symbol));
        if (_librariesRead && LinkOf(symbol.LibId).Drift is not null)
        {
            yield return new InspectorAction(Tr.T("sch.action.updateFromLibrary"), () => UpdateFromLibrary(symbol.LibId)) { IsPrimary = true };
        }
    }

    /// <summary>
    /// Opens the library a placed part came from, on its symbol, at the unit and body style it is placed as — or,
    /// when that cannot be done, says exactly why, since "nothing happened" teaches nothing.
    /// </summary>
    internal async Task EditInLibraryAsync(SymbolInstance symbol)
    {
        if (_context is not { } context || symbol.LibId.Length == 0)
        {
            return;
        }

        var link = new LibraryLink(symbol.LibId, Libraries.RowOf(symbol.LibId), null, null, false);
        if (link.Row is not { } row)
        {
            context.Workbench.ShowBanner(new Banner(Tr.T("sch.lib.link.banner.notInTable", link.Nickname, link.Name)));
            return;
        }

        if (!File.Exists(row.Path))
        {
            context.Workbench.ShowBanner(new Banner(Tr.T("sch.lib.link.banner.fileMissing", link.Nickname, row.Path)));
            return;
        }

        int unit = symbol.UnitAt(Instance);
        int style = symbol.BodyStyle;
        if (await context.Workbench.OpenAsync(row.Path) is not SymbolLibraryDocument library)
        {
            return;
        }

        if (library.Library.Find(link.Name) is null)
        {
            context.Workbench.ShowBanner(new Banner(Tr.T("sch.lib.link.banner.missing", link.Nickname, link.Name)));
            return;
        }

        library.Show(link.Name);
        library.ShowUnit(unit);
        library.ShowBodyStyle(style);
    }

    /// <summary>The placed part a library command is about: the one selected.</summary>
    private SymbolInstance? SelectedSymbol => _editor.Selection is [SymbolInstance symbol] ? symbol : null;

    /// <summary>
    /// Takes the sheet's copy of a definition from the library again — what a designer does once the part has been
    /// fixed there. Every placement drawing from it is named in the change, so they all redraw, and one undo takes
    /// the whole thing back.
    /// </summary>
    private void UpdateFromLibrary(string libId) => UpdateFromLibrary([libId], Tr.T("sch.action.updateFromLibrary"));

    /// <summary>Every definition on the sheet that differs from its library, taken again as one step.</summary>
    private void UpdateAllFromLibrary() =>
        UpdateFromLibrary([.. PlacedLibIds().Where(l => LinkOf(l).Drift is not null)], Tr.T("sch.command.updateFromLibrary"));

    private void UpdateFromLibrary(IReadOnlyList<string> libIds, string label)
    {
        var steps = new List<IEditCommand>();
        foreach (string libId in libIds)
        {
            if (!Sheet.LibrarySymbols.ContainsKey(libId))
            {
                continue;
            }

            // Read again: the library may have changed since the sheet last looked.
            _links?.Remove(libId);
            if (LinkOf(libId).Fresh is not { } fresh)
            {
                _context?.Log.Error(Tr.English("sch.log.updateMissing", libId));
                continue;
            }

            steps.Add(new ModifyNodesCommand(label, SchSymbols.Affected(Sheet, libId), () => SchSymbols.Update(Sheet, libId, fresh)));
        }

        if (steps.Count > 0)
        {
            _editor.Run(steps.Count == 1 ? steps[0] : new CompositeCommand(label, steps));
        }
    }

    /// <summary>
    /// The definition a part is placed with, whole: a symbol that extends another is flattened, as KiCad copies it
    /// into a sheet, or the sheet would carry a part with no body to draw.
    /// </summary>
    private LibSymbol Whole(string libId, LibSymbol definition) =>
        definition.Extends is { Length: > 0 } ? Libraries.FindPlaced(libId) ?? definition : definition;

    /// <summary>"pins: 2 added, 1 changed; fields: Value" — what differs, in the words a designer looks for.</summary>
    internal static string Describe(SymbolDrift drift)
    {
        var pins = new List<string>();
        if (drift.PinsAdded > 0)
        {
            pins.Add(Tr.T("sch.drift.added", drift.PinsAdded));
        }

        if (drift.PinsRemoved > 0)
        {
            pins.Add(Tr.T("sch.drift.removed", drift.PinsRemoved));
        }

        if (drift.PinsChanged > 0)
        {
            pins.Add(Tr.T("sch.drift.changed", drift.PinsChanged));
        }

        var parts = new List<string>();
        if (pins.Count > 0)
        {
            parts.Add(Tr.T("sch.drift.pins", string.Join(", ", pins)));
        }

        if (drift.ShapesChanged > 0)
        {
            parts.Add(Tr.T("sch.drift.shapes", drift.ShapesChanged));
        }

        if (drift.Fields.Count > 0)
        {
            parts.Add(Tr.T("sch.drift.fields", string.Join(", ", drift.Fields.Select(FieldName))));
        }

        if (drift.UnitsChanged)
        {
            parts.Add(Tr.T("sch.drift.units"));
        }

        if (drift.PowerChanged)
        {
            parts.Add(Tr.T("sch.drift.power"));
        }

        return string.Join("; ", parts);
    }

    /// <summary>A field by the name the inspector gives it: the file's <c>ki_keywords</c> is "Keywords".</summary>
    private static string FieldName(string field) => field switch
    {
        "Reference" => Tr.T("sch.lib.row.reference"),
        "Value" => Tr.T("sch.lib.row.value"),
        "Footprint" => Tr.T("sch.lib.row.footprint"),
        "Datasheet" => Tr.T("sch.lib.row.datasheet"),
        "Description" => Tr.T("sch.lib.row.description"),
        "ki_keywords" => Tr.T("sch.lib.row.keywords"),
        "ki_fp_filters" => Tr.T("sch.lib.row.fpFilters"),
        _ => field,
    };
}
