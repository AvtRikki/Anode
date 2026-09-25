using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Kicad.Editing;
using Avalonia.Threading;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The bill of materials of a design, as a tab of its own: every part of every sheet, laid out by the preset the
/// project names — KiCad's Symbol Fields Table, for the whole design rather than one dialog.
///
/// It is a view, not a file. What it shows is read from the sheets as they are — the open ones as they stand in their
/// editors, the rest from disk — and read again whenever a sheet changes; a value written into it goes into the
/// sheets, through their own editors, so it is saved and undone there. What leaves it is a report: a snapshot of the
/// bill at the moment it is exported.
/// </summary>
internal sealed class BomDocument : DocumentBase
{
    public const string TypeId = "anode.bom";

    /// <summary>One bill per design: asking again for the same root brings up the one already made.</summary>
    private static readonly Dictionary<(IWorkbench, string), BomDocument> Made = [];

    private readonly IWorkbench _workbench;
    private readonly Dictionary<string, (DateTime Written, Anode.Kicad.Schematic? Sheet)> _disk = new(StringComparer.Ordinal);
    private readonly List<BomEdit> _done = [];
    private readonly List<BomEdit> _undone = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _refreshPosted;

    /// <summary>
    /// One change made from the bill: the step it came to on each sheet it touched. It is undone and redone whole,
    /// on every one of those sheets, or not at all.
    /// </summary>
    private sealed record BomEdit(string Name, IReadOnlyList<(SchematicDocument Sheet, IEditCommand Step)> Steps);

    private BomDocument(IWorkbench workbench, string rootFile)
    {
        _workbench = workbench;
        RootFile = rootFile;
        (Project, Saved) = BomPreset.Read(SchBom.ProjectOf(rootFile));
        Preset = Project;
        Table = BomTable.Build([], Preset);

        SchematicDocument.SheetEdited += OnSheetEdited;
        workbench.ActiveDocumentChanged += PostRefresh;
        Refresh();
    }

    /// <summary>The bill of the design rooted at <paramref name="rootFile"/>, made the first time it is asked for.</summary>
    public static BomDocument For(IWorkbench workbench, string rootFile)
    {
        string root = Path.GetFullPath(rootFile);
        if (!Made.TryGetValue((workbench, root), out var document))
        {
            Made[(workbench, root)] = document = new BomDocument(workbench, root);
        }

        return document;
    }

    public string RootFile { get; }

    public override string? DocumentTypeId => TypeId;

    public override string Title => Tr.T("sch.bom.title", Path.GetFileNameWithoutExtension(RootFile));

    public override string Summary => Tr.T("sch.bom.count", Table.Rows.Count, Table.PartCount);

    /// <summary>How the bill is laid out: the project's own preset until another is chosen.</summary>
    public BomPreset Preset { get; private set; }

    /// <summary>The preset the project names — KiCad's default when it names none.</summary>
    public BomPreset Project { get; }

    /// <summary>The presets the project keeps besides its current one.</summary>
    public IReadOnlyList<BomPreset> Saved { get; }

    /// <summary>
    /// Every preset on offer, in an order that does not change: the project's own first, then KiCad's built-in ones,
    /// then the others it keeps. A built-in one the project's is a copy of is not offered twice.
    /// </summary>
    public IReadOnlyList<BomPreset> Presets =>
        [.. new[] { Project }.Concat(BomPreset.BuiltIn.Where(b => b != Project && !ReferenceEquals(b, Project))).Concat(Saved)];

    /// <summary>Designators to show, as KiCad's filter matches them; empty shows every part.</summary>
    public string Filter { get; private set; } = string.Empty;

    /// <summary>Every field is a column, not only those the preset shows.</summary>
    public bool AllFields { get; private set; }

    public BomTable Table { get; private set; }

    public override IReadOnlyList<StatusField> StatusFields => [new(Summary)];

    protected override Control CreateView() => new BomView(this);

    public void Choose(BomPreset preset)
    {
        Preset = preset;
        Refresh();
    }

    public void SetFilter(string filter)
    {
        Filter = filter.Trim();
        Refresh();
    }

    public void ShowAllFields(bool on)
    {
        AllFields = on;
        OnPropertiesChanged(nameof(Table));
    }

    /// <summary>Reads the design again and lays the bill out anew.</summary>
    public void Refresh()
    {
        _refreshPosted = false;
        var parts = SchBom.Parts(RootFile, Sheet);
        Table = BomTable.Build(parts, Preset, Filter);
        OnPropertiesChanged(nameof(Table), nameof(Summary), nameof(StatusFields));
    }

    /// <summary>
    /// Writes a value into a field of every part of a line. The sheets they are on are opened if they are not, so
    /// that each change is made — and saved, and undone — in the sheet it belongs to; the bill then comes back to
    /// the front.
    /// </summary>
    public Task WriteAsync(BomRow row, string field, string value) =>
        ChangeAsync(row, Tr.T("sch.bom.edit", field), (sheet, ids) => sheet.WriteField(ids, field, value));

    /// <summary>Sets or clears one of the parts' flags — do not place, keep off the bill, the board, simulation — for a line.</summary>
    public Task SetFlagAsync(BomRow row, string column, bool on) =>
        ChangeAsync(row, Tr.T("sch.bom.edit", column), (sheet, ids) => sheet.SetFlag(ids, column, on));

    /// <summary>
    /// Makes one change to the parts of a line, sheet by sheet, and remembers it as one thing to undo from the bill.
    /// Each sheet keeps its own step too, so the change can equally be undone on the sheet itself.
    /// </summary>
    private async Task ChangeAsync(BomRow row, string name, Func<SchematicDocument, IReadOnlyCollection<string>, IEditCommand?> change)
    {
        var steps = new List<(SchematicDocument, IEditCommand)>();
        foreach (var byFile in row.Parts.GroupBy(p => p.File, StringComparer.Ordinal))
        {
            if (await SheetDocument(byFile.Key) is { } sheet && change(sheet, Ids(byFile)) is { } step)
            {
                steps.Add((sheet, step));
            }
        }

        if (steps.Count > 0)
        {
            _done.Add(new BomEdit(name, steps));
            _undone.Clear();
        }

        _workbench.Show(this);
        Refresh();
    }

    /// <summary>
    /// Whether the last change made from the bill can be taken back: every sheet it touched is still open and has
    /// done nothing since. A sheet edited after it has its own later steps on top, and undoing under them would
    /// take the drawing apart; the sheet's own undo is the way back then.
    /// </summary>
    public bool CanUndo => _done.Count > 0 && _done[^1].Steps.All(s => IsOpen(s.Sheet) && s.Sheet.IsLastDone(s.Step));

    public bool CanRedo => _undone.Count > 0 && _undone[^1].Steps.All(s => IsOpen(s.Sheet) && s.Sheet.IsNextRedo(s.Step));

    public string? UndoName => _done.Count > 0 ? _done[^1].Name : null;

    public string? RedoName => _undone.Count > 0 ? _undone[^1].Name : null;

    /// <summary>Takes back the last change made from the bill, on every sheet it touched.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var edit = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        foreach (var (sheet, _) in edit.Steps.Reverse())
        {
            sheet.UndoLast();
        }

        _undone.Add(edit);
        Refresh();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var edit = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        foreach (var (sheet, _) in edit.Steps)
        {
            sheet.RedoNext();
        }

        _done.Add(edit);
        Refresh();
    }

    public override void Activate(IPluginContext context)
    {
        // The bill's own undo: what it changed, it takes back, across every sheet the change reached.
        CommandDescriptor[] commands =
        [
            new("edit.undo", "sch.command.undo")
            {
                ScopeKey = "scope.bom", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                MenuKey = "menu.edit", MenuOrder = 0,
                CanExecute = () => CanUndo,
                Execute = Undo,
            },
            new("edit.redo", "sch.command.redo")
            {
                ScopeKey = "scope.bom", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 10,
                CanExecute = () => CanRedo,
                Execute = Redo,
            },
        ];

        foreach (var command in commands)
        {
            _registrations.Add(context.Commands.Register(command));
        }

        Refresh();
    }

    public override void Deactivate()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    private static KeyGesture Shortcut(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | extra);

    private bool IsOpen(SchematicDocument sheet) => _workbench.Documents.Contains(sheet);

    /// <summary>Turns to the first sheet the line's parts are on, at the place they stand in, and selects them there.</summary>
    public async Task ShowAsync(BomRow row)
    {
        if (row.Parts.FirstOrDefault() is not { } first)
        {
            return;
        }

        var there = row.Parts.Where(p => p.File == first.File && p.Path == first.Path).ToList();
        if (await _workbench.OpenAsync(first.File, first.Path) is SchematicDocument sheet)
        {
            sheet.ShowSymbols(Ids(there));
        }
    }

    /// <summary>
    /// Writes the bill as it is now — the shown columns, laid out as KiCad's CSV export lays them out — where the
    /// person asks. It is a report of this moment; the bill itself goes on following the design.
    /// </summary>
    public async Task ExportAsync()
    {
        string suggested = Path.GetFileNameWithoutExtension(RootFile) + "-bom.csv";
        try
        {
            if (await _workbench.AskWhereToWriteAsync(suggested, ".csv", "sch.bom.export", Path.GetDirectoryName(RootFile))
                is not { Length: > 0 } target)
            {
                return;
            }

            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temp, Table.Csv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, target, overwrite: true);
            _workbench.Log.Info(Tr.English("sch.log.exported", Path.GetFileName(target)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _workbench.Log.Error(ex.Message, ex);
            _workbench.ShowBanner(new Banner(ex.Message));
        }
    }

    public override void Dispose()
    {
        Deactivate();
        SchematicDocument.SheetEdited -= OnSheetEdited;
        _workbench.ActiveDocumentChanged -= PostRefresh;
        Made.Remove((_workbench, RootFile));
        base.Dispose();
    }

    private static HashSet<string> Ids(IEnumerable<BomPart> parts) =>
        parts.Select(p => p.Symbol.Uuid).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A sheet of the design as it is now: the open document's own when the sheet is open — the parts it shows are
    /// the ones its editor writes — and otherwise read from disk, again only when the file changes.
    /// </summary>
    private Anode.Kicad.Schematic? Sheet(string file)
    {
        if (Opened(file) is { } open)
        {
            return open.Sheet;
        }

        DateTime written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
        if (!_disk.TryGetValue(file, out var known) || known.Written != written)
        {
            Anode.Kicad.Schematic? sheet = null;
            try
            {
                sheet = written == default ? null : Anode.Kicad.Schematic.Load(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException or Sexpr.SexprParseException or DecoderFallbackException)
            {
            }

            _disk[file] = known = (written, sheet);
        }

        return known.Sheet;
    }

    private SchematicDocument? Opened(string file) =>
        _workbench.Documents.OfType<SchematicDocument>().FirstOrDefault(d =>
            d.FilePath is { } path && string.Equals(Path.GetFullPath(path), file, StringComparison.Ordinal));

    private async Task<SchematicDocument?> SheetDocument(string file) =>
        Opened(file) ?? await _workbench.OpenAsync(file) as SchematicDocument;

    private void OnSheetEdited(SchematicDocument sheet) => PostRefresh();

    /// <summary>A burst of edits reads the design once, after the last of them.</summary>
    private void PostRefresh()
    {
        if (_refreshPosted)
        {
            return;
        }

        _refreshPosted = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }
}
