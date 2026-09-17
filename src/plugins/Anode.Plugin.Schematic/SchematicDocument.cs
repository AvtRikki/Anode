using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Kicad;
using Anode.Sdk;
using Anode.Render;

namespace Anode.Plugin.Schematic;

/// <summary>Opens <c>.kicad_sch</c> files as document tabs.</summary>
public sealed class SchematicDocumentType(ILog log) : IDocumentType
{
    /// <summary>Panels and documents refer to the type by this id.</summary>
    public const string TypeId = "anode.schematic";

    public string Id => TypeId;

    public string Label => Tr.T("sch.document.label");

    public IReadOnlyList<string> Extensions { get; } = [".kicad_sch"];

    public bool CanCreate => true;

    /// <summary>
    /// A new sheet: the header KiCad writes and empty paper. It is parsed back before it is written, because the
    /// cheapest proof that we wrote a sheet is that we can read it.
    /// </summary>
    public Task CreateAsync(string path, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string text =
                "(kicad_sch\n" +
                $"\t(version {KiCadFormat.KiCad10})\n" +
                "\t(generator \"anode\")\n" +
                "\t(generator_version \"0.1\")\n" +
                $"\t(uuid \"{Guid.NewGuid():D}\")\n" +
                "\t(paper \"A4\")\n" +
                "\t(lib_symbols)\n" +
                "\t(sheet_instances\n" +
                "\t\t(path \"/\"\n" +
                "\t\t\t(page \"1\")\n" +
                "\t\t)\n" +
                "\t)\n" +
                "\t(embedded_fonts no)\n" +
                ")\n";

            _ = Anode.Kicad.Schematic.Parse(text);
            File.WriteAllText(path, text);
            log.Info(Tr.T("sch.log.created", Path.GetFileName(path)));
        },
        cancellationToken);

    public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var schematic = Anode.Kicad.Schematic.Load(path);
            var scene = SchematicSceneBuilder.Build(schematic);
            if (GraphicsOptions.Renderer == RendererKind.OpenGl)
            {
                SceneTriangulator.Triangulate(scene);
            }

            log.Info(Tr.T("sch.log.loaded", Path.GetFileName(path), schematic.Symbols.Count, scene.PrimitiveCount));
            return (IDocument)new SchematicDocument(schematic, scene, path);
        },
        cancellationToken);
}

/// <summary>One open sheet: the canvas and what the workbench shows around it.</summary>
public sealed class SchematicDocument : DocumentBase
{
    private readonly List<IDisposable> _registrations = [];
    private readonly IReadOnlyList<SheetCheck> _checks;
    private readonly SchematicEditor _editor;
    private SchematicCanvas? _canvas;
    private string _cursor = string.Empty;
    private string _frame = string.Empty;
    private double _zoom;

    internal SchematicDocument(Anode.Kicad.Schematic schematic, SchematicScene scene, string path)
    {
        Sheet = schematic;
        Scene = scene;
        FilePath = path;
        _editor = new SchematicEditor(scene) { TriangulateChanges = GraphicsOptions.Renderer == RendererKind.OpenGl };
        _editor.SelectionChanged += OnSelectionChanged;
        _editor.History.Changed += OnHistoryChanged;
        _checks = SheetChecks.Run(schematic);
        Tr.Changed += OnLanguageChanged;
    }

    public Anode.Kicad.Schematic Sheet { get; }

    public SchematicScene Scene { get; }

    public override string? DocumentTypeId => SchematicDocumentType.TypeId;

    public override string Title => Path.GetFileName(FilePath ?? Tr.T("sch.document.untitled"));

    public override bool CanSave => true;

    public override bool IsDirty => _editor.History.IsDirty;

    public override string Summary
    {
        get
        {
            string zoom = _zoom > 0 ? _zoom.ToString("0", CultureInfo.InvariantCulture) + "%" : Count();
            return Tr.T("sch.summary.sheet", zoom);
        }
    }

    public override IReadOnlyList<StatusField> StatusFields
    {
        get
        {
            List<StatusField> fields = [];
            if (_cursor.Length > 0)
            {
                fields.Add(new StatusField(_cursor));
            }

            fields.Add(new StatusField(Count()));
            if (_editor.Selection is [var single])
            {
                fields.Add(new StatusField(Tr.T("sch.status.selected", SchItemProperties.Header(single).Title)));
            }
            else if (_editor.Selection.Count > 1)
            {
                fields.Add(new StatusField(Tr.T("sch.status.selected", Tr.T("sch.selection.multi", _editor.Selection.Count))));
            }

            if (_frame.Length > 0)
            {
                fields.Add(new StatusField(_frame, AlignEnd: true));
            }

            return fields;
        }
    }

    public override SelectionInfo? Selection
    {
        get
        {
            if (_editor.Selection is not [var item])
            {
                return _editor.Selection.Count > 1
                    ? new SelectionInfo(Tr.T("sch.selection.multi", _editor.Selection.Count), null, [])
                    : null;
            }

            var (title, subtitle, tag) = SchItemProperties.Header(item);
            return new SelectionInfo(title, subtitle, [.. SchItemProperties.For(item)], tag);
        }
    }

    public override IReadOnlyList<Issue> Issues => [.. _checks.Select(c => c.ToIssue())];

    public override Task<bool> SaveAsync(string? path = null)
    {
        _editor.Save(path ?? FilePath ?? throw new InvalidOperationException("The sheet has no path to save to."));
        FilePath = path ?? FilePath;
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty));
        return Task.FromResult(true);
    }

    public override void Activate(IPluginContext context)
    {
        CommandDescriptor[] commands =
        [
            new("edit.undo", "sch.command.undo")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                CanExecute = () => _editor.History.CanUndo,
                Execute = () => Guard(() => _editor.Undo(), context),
            },
            new("edit.redo", "sch.command.redo")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                CanExecute = () => _editor.History.CanRedo,
                Execute = () => Guard(() => _editor.Redo(), context),
            },
            new("edit.delete", "sch.command.delete")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌫",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.DeleteSelection(), context),
            },
            new("sch.move", "sch.command.move")
            {
                ScopeKey = "scope.schematic", ShortcutText = "M",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => _canvas?.BeginMoveWithCursor(),
            },
            new("sch.rotate", "sch.command.rotate")
            {
                ScopeKey = "scope.schematic", ShortcutText = "R",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Rotate(90), context),
            },
            new("sch.mirror", "sch.command.mirror")
            {
                ScopeKey = "scope.schematic", ShortcutText = "X",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Mirror(horizontal: true), context),
            },
            new("sch.fit", "sch.command.fit")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Home",
                Execute = () => _canvas?.ZoomToFit(),
            },
        ];

        foreach (var command in commands)
        {
            _registrations.Add(context.Commands.Register(command));
        }
    }

    private static KeyGesture Shortcut(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | extra);

    private void Guard(Action action, IPluginContext context)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or KiCadFormatException)
        {
            context.Log.Warn(ex.Message);
            context.Workbench.ShowBanner(new Banner(ex.Message, IsAlert: false));
        }
    }

    public override void Deactivate()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    public override void Dispose()
    {
        Deactivate();
        _editor.SelectionChanged -= OnSelectionChanged;
        _editor.History.Changed -= OnHistoryChanged;
        Tr.Changed -= OnLanguageChanged;
        base.Dispose();
    }

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Editor = _editor };
        _canvas = canvas;
        canvas.CursorMoved += position =>
        {
            _cursor = position is { } p
                ? Tr.T("sch.status.cursor",
                    p.X.ToString("0.00", CultureInfo.InvariantCulture),
                    p.Y.ToString("0.00", CultureInfo.InvariantCulture),
                    Tr.T("sch.units.mm"))
                : string.Empty;
            OnPropertyChanged(nameof(StatusFields));
        };
        canvas.FrameRendered += ms =>
        {
            _frame = string.Create(CultureInfo.InvariantCulture, $"{canvas.BackendName} · {ms:0.0} ms");
            OnPropertyChanged(nameof(StatusFields));
        };
        canvas.ViewChanged += () =>
        {
            _zoom = canvas.ZoomPercent;
            OnPropertyChanged(nameof(Summary));
        };
        return canvas;
    }

    /// <summary>The canvas draws again when the history moves; the workbench re-reads the document's state.</summary>
    public void Redraw() => _canvas?.Redraw();

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private void OnHistoryChanged() => OnPropertiesChanged(nameof(IsDirty), nameof(StatusFields), nameof(Summary));

    private string Count() => Tr.T("sch.status.symbols", Sheet.Symbols.Count, Tr.Plural("sch.symbol", Sheet.Symbols.Count));

    private void OnLanguageChanged() =>
        OnPropertiesChanged(nameof(Title), nameof(Summary), nameof(StatusFields), nameof(Selection), nameof(Issues));
}

/// <summary>A check result kept in translation-independent form, so it survives a language switch.</summary>
internal sealed record SheetCheck(IssueSeverity Severity, string TitleKey, string DetailKey, object?[] DetailArgs, string LocationKey, object?[] LocationArgs)
{
    public Issue ToIssue() => new(Severity, Tr.T(TitleKey), Tr.T(DetailKey, DetailArgs), Tr.T(LocationKey, LocationArgs));
}

/// <summary>What can be said about a sheet without a full ERC.</summary>
internal static class SheetChecks
{
    public static IReadOnlyList<SheetCheck> Run(Anode.Kicad.Schematic schematic)
    {
        List<SheetCheck> checks = [];

        if (!schematic.IsSupportedVersion)
        {
            checks.Add(new SheetCheck(IssueSeverity.Error, "sch.issue.oldFormat.title", "sch.issue.oldFormat.detail", [],
                "sch.issue.version", [schematic.Version]));
        }
        else if (schematic.IsNewerThanKnown)
        {
            checks.Add(new SheetCheck(IssueSeverity.Error, "sch.issue.newFormat.title", "sch.issue.newFormat.detail", [],
                "sch.issue.version", [schematic.Version]));
        }

        // A symbol whose definition is missing from the file draws as nothing, so it is worth saying out loud.
        var missing = schematic.Symbols
            .Where(s => s.Definition is null)
            .Select(s => s.Reference ?? s.LibId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            string names = string.Join(", ", missing.Take(6)) + (missing.Count > 6 ? " …" : string.Empty);
            checks.Add(new SheetCheck(IssueSeverity.Warning, "sch.issue.noDefinition.title", "sch.issue.noDefinition.detail", [names],
                "sch.issue.definitions", [missing.Count, schematic.Symbols.Count]));
        }

        return checks;
    }
}
