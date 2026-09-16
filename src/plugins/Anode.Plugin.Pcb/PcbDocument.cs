using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Kicad;
using Anode.Sdk;
using Anode.Render;

namespace Anode.Plugin.Pcb;

/// <summary>Opens <c>.kicad_pcb</c> files as document tabs.</summary>
public sealed class PcbDocumentType(ILog log) : IDocumentType
{
    /// <summary>Panels and documents refer to the type by this id.</summary>
    public const string TypeId = "anode.pcb";

    public string Id => TypeId;

    public string Label => Tr.T("pcb.document.label");

    public IReadOnlyList<string> Extensions { get; } = [".kicad_pcb"];

    public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var board = Board.Load(path);
            var scene = SceneBuilder.Build(board);
            if (GraphicsOptions.Renderer == RendererKind.OpenGl)
            {
                // GPU upload needs triangles; compute them here instead of stalling the first frame.
                SceneTriangulator.Triangulate(scene);
            }

            log.Info(Tr.T("pcb.log.loaded", Path.GetFileName(path), board.Footprints.Count, scene.PrimitiveCount));
            return (IDocument)new PcbDocument(board, scene, path);
        },
        cancellationToken);
}

/// <summary>One open board: the canvas, its editor, and what the workbench shows around them.</summary>
public sealed class PcbDocument : DocumentBase
{
    private readonly Board _board;
    private readonly BoardEditor _editor;
    private readonly List<IDisposable> _registrations = [];
    private readonly IReadOnlyList<BoardCheck> _checks;
    private BoardCanvas? _canvas;
    private string _cursor = string.Empty;
    private string _frame = string.Empty;
    private double _zoom;

    internal PcbDocument(Board board, BoardScene scene, string path)
    {
        _board = board;
        Scene = scene;
        FilePath = path;
        _editor = new BoardEditor(scene) { TriangulateChanges = GraphicsOptions.Renderer == RendererKind.OpenGl };
        _editor.SelectionChanged += OnSelectionChanged;
        _editor.History.Changed += OnHistoryChanged;
        _checks = BoardChecks.Run(board);
        Tr.Changed += OnLanguageChanged;
    }

    public BoardScene Scene { get; }

    public override string? DocumentTypeId => PcbDocumentType.TypeId;

    public override string Title => Path.GetFileName(FilePath ?? Tr.T("pcb.document.untitled"));

    public override bool IsDirty => _editor.History.IsDirty;

    public override bool CanSave => true;

    public override string Summary
    {
        get
        {
            int copper = _board.Layers.Copper.Count;
            string zoom = _zoom > 0 ? $" · {_zoom.ToString("0", CultureInfo.InvariantCulture)}%" : string.Empty;
            return Tr.T("pcb.summary.board", copper, Tr.Plural("pcb.layer", copper)) + zoom;
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

            int count = _editor.Selection.Count;
            if (count > 0)
            {
                fields.Add(new StatusField(Tr.T("pcb.status.selected", count)));
            }

            if (_editor.FocusNet is { IsUnconnected: false } net)
            {
                fields.Add(new StatusField(Tr.T("pcb.status.net", net.Name)));
            }

            bool alert = _checks.Any(c => c.Severity == IssueSeverity.Error);
            fields.Add(new StatusField(Tr.T("pcb.status.checks", _checks.Count), AlignEnd: true, IsAlert: alert));
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
            if (_editor.Selection is not { Count: > 0 } selection)
            {
                return null;
            }

            if (selection.Count > 1)
            {
                return new SelectionInfo(Tr.T("pcb.selection.multi", selection.Count), Tr.T("pcb.selection.group"),
                    [.. selection.Take(12).Select(i => new PropertyItem(ItemProperties.Header(i).Title, i.GetType().Name))]);
            }

            var item = selection[0];
            var (title, subtitle, tag) = ItemProperties.Header(item);
            List<PropertyItem> properties = [.. ItemProperties.For(item)];

            // For a footprint, also show the pad, text or graphic that was clicked.
            if (Scene.IsLive(_editor.FocusOwner) && Scene.Owner(_editor.FocusOwner) is var focus && !ReferenceEquals(focus, item))
            {
                properties.Add(new PropertyItem(ItemProperties.Header(focus).Title, string.Empty, IsSection: true));
                properties.AddRange(ItemProperties.For(focus));
            }

            return new SelectionInfo(title, subtitle, properties, tag);
        }
    }

    public override IReadOnlyList<Issue> Issues => [.. _checks.Select(c => c.ToIssue())];

    public override Task<bool> SaveAsync(string? path = null)
    {
        string target = path ?? FilePath ?? throw new InvalidOperationException("The document has no path to save to.");
        _editor.Save(target);
        FilePath = target;
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty));
        return Task.FromResult(true);
    }

    public override void Activate(IPluginContext context)
    {
        CommandDescriptor[] commands =
        [
            new("edit.undo", "pcb.command.undo")
            {
                ScopeKey = "scope.board", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                CanExecute = () => _editor.History.CanUndo,
                Execute = () => Guard(() => _editor.Undo(), context),
            },
            new("edit.redo", "pcb.command.redo")
            {
                ScopeKey = "scope.board", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                CanExecute = () => _editor.History.CanRedo,
                Execute = () => Guard(() => _editor.Redo(), context),
            },
            new("edit.delete", "pcb.command.delete")
            {
                ScopeKey = "scope.board", ShortcutText = "⌫",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.DeleteSelection(), context),
            },
            new("pcb.move", "pcb.command.move")
            {
                ScopeKey = "scope.board", ShortcutText = "M",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => _canvas?.BeginMoveWithCursor(),
            },
            new("pcb.rotate", "pcb.command.rotate")
            {
                ScopeKey = "scope.board", ShortcutText = "R",
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Rotate(90), context),
            },
            new("pcb.fit", "pcb.command.fit")
            {
                ScopeKey = "scope.board", ShortcutText = "Home",
                Execute = () => _canvas?.ZoomToFit(),
            },
            new("pcb.flip", "pcb.command.flip")
            {
                ScopeKey = "scope.board", ShortcutText = "⇧B",
                Execute = () =>
                {
                    if (_canvas is { } canvas)
                    {
                        canvas.FlipX = !canvas.FlipX;
                    }
                },
            },
            new("pcb.highlightNet", "pcb.command.highlightNet")
            {
                ScopeKey = "scope.board",
                Execute = () =>
                {
                    if (_canvas is { } canvas)
                    {
                        canvas.HighlightNet = !canvas.HighlightNet;
                    }
                },
            },
        ];

        foreach (var command in commands)
        {
            _registrations.Add(context.Commands.Register(command));
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
        Tr.Changed -= OnLanguageChanged;
        _editor.SelectionChanged -= OnSelectionChanged;
        _editor.History.Changed -= OnHistoryChanged;
        base.Dispose();
    }

    /// <summary>Repaints after something outside the editor changed, e.g. layer visibility.</summary>
    public void Redraw() => _canvas?.Redraw();

    protected override Control CreateView()
    {
        var canvas = new BoardCanvas { Editor = _editor };
        _canvas = canvas;
        canvas.CursorMoved += position =>
        {
            _cursor = position is { } p
                ? Tr.T("pcb.status.cursor",
                    p.X.ToString("0.000", CultureInfo.InvariantCulture),
                    p.Y.ToString("0.000", CultureInfo.InvariantCulture),
                    Tr.T("pcb.units.mm"))
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

    private void OnLanguageChanged() => OnPropertiesChanged(nameof(Title), nameof(Summary), nameof(StatusFields), nameof(Selection), nameof(Issues));

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private void OnHistoryChanged() => OnPropertiesChanged(nameof(IsDirty), nameof(StatusFields));
}

/// <summary>A check result kept in translation-independent form, so it survives a language switch.</summary>
internal sealed record BoardCheck(IssueSeverity Severity, string TitleKey, string DetailKey, object?[] DetailArgs, string LocationKey, object?[] LocationArgs)
{
    public Issue ToIssue() => new(Severity, Tr.T(TitleKey), Tr.T(DetailKey, DetailArgs), Tr.T(LocationKey, LocationArgs));
}

/// <summary>The checks the bottom dock lists. Only what can be said for certain without a full DRC.</summary>
internal static class BoardChecks
{
    public static IReadOnlyList<BoardCheck> Run(Board board)
    {
        List<BoardCheck> checks = [];

        if (!board.IsSupportedVersion)
        {
            checks.Add(new BoardCheck(IssueSeverity.Error, "pcb.issue.oldFormat.title", "pcb.issue.oldFormat.detail", [],
                "pcb.issue.version", [board.Version]));
        }
        else if (board.IsNewerThanKnown)
        {
            checks.Add(new BoardCheck(IssueSeverity.Error, "pcb.issue.newFormat.title", "pcb.issue.newFormat.detail", [],
                "pcb.issue.version", [board.Version]));
        }

        var routed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var net in board.Segments.Select(s => s.Net).Concat(board.Arcs.Select(a => a.Net)).Concat(board.Vias.Select(v => v.Net)))
        {
            if (net is { IsUnconnected: false })
            {
                routed.Add(net.Name);
            }
        }

        var pins = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pad in board.Footprints.SelectMany(f => f.Pads))
        {
            if (pad.Net is { IsUnconnected: false } net)
            {
                pins[net.Name] = pins.GetValueOrDefault(net.Name) + 1;
            }
        }

        var unrouted = pins.Where(p => p.Value > 1 && !routed.Contains(p.Key)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        if (unrouted.Count > 0)
        {
            string names = string.Join(", ", unrouted.Take(6)) + (unrouted.Count > 6 ? " …" : string.Empty);
            checks.Add(new BoardCheck(IssueSeverity.Warning, "pcb.issue.unrouted.title", "{0}", [names],
                "{0} {1}", [unrouted.Count, Tr.Plural("pcb.net", unrouted.Count)]));
        }

        return checks;
    }
}
