using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Kicad.Editing;
using Anode.Sdk;
using Anode.Render;
using Anode.Render.Fonts;

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
            // A board is one page, framed with the drawing sheet its project names for boards.
            // The sheet the project names may be one the board itself carries.
            var frame = SheetFrameText.ForProject(path, board: true, out string? missing,
                name => EmbeddedFile.In(board.Document.Root).FirstOrDefault(f => f.Name == name)?.Data);
            var scene = SceneBuilder.Build(board, frame);
            if (GraphicsOptions.Renderer == RendererKind.OpenGl)
            {
                // GPU upload needs triangles; compute them here instead of stalling the first frame.
                SceneTriangulator.Triangulate(scene);
            }

            log.Info(Tr.T("pcb.log.loaded", Path.GetFileName(path), board.Footprints.Count, scene.PrimitiveCount));
            return (IDocument)new PcbDocument(board, scene, path)
            {
                DrawingSheetMissing = missing,
                MissingFaces = MissingFaces(board, frame),
            };
        },
        cancellationToken);

    /// <summary>
    /// Faces the board and its drawing sheet name that this machine lacks, with what stands in for each, and whether
    /// every text in that face is drawn from the letters KiCad saved — in which case the board looks as authored.
    /// </summary>
    private static IReadOnlyList<(string Face, string StandIn, bool Saved)> MissingFaces(Board board, SheetFrameText frame)
    {
        var onBoard = TextFont.FacesIn(board.Document.Root);
        var inFrame = frame.Template?.Items.OfType<WksText>().Select(t => t.Face).OfType<string>() ?? [];
        return [.. OutlineText.StandIns(onBoard.Concat(inFrame)).Select(m =>
        {
            var texts = board.Texts.Where(t => string.Equals(t.FontFace, m.Face, StringComparison.OrdinalIgnoreCase)).ToList();
            return (m.Face, m.StandIn, texts.Count > 0 && texts.All(SceneBuilder.DrawsFromCache) && !inFrame.Contains(m.Face, StringComparer.OrdinalIgnoreCase));
        })];
    }
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
    private SelectionInfo? _overview;
    private IPluginContext? _context;

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

    /// <summary>The editor behind the canvas; the tests drive selection and history through it.</summary>
    internal BoardEditor Editor => _editor;

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

    /// <summary>The board itself, for the inspector when nothing is selected; worked out once per state of the board.</summary>
    public override SelectionInfo? Overview =>
        _overview ??= BoardOverview.Build(_board, FilePath, Scene.BoardOutline, Issues, EditTitleBlock, DocumentFonts.Of(_board), EmbedFonts);

    /// <summary>
    /// One change to an item of the board, as one undoable step. The step remembers the top-level item — a text of a
    /// footprint belongs to the footprint — which is also what the scene redraws.
    /// </summary>
    private void EditItem(string name, Action mutate)
    {
        if (_editor.Selection is not { Count: > 0 } selection)
        {
            return;
        }

        try
        {
            _editor.Run(new Anode.Kicad.Editing.ModifyNodesCommand(name, [selection[0].TopLevel], mutate));
        }
        catch (Exception ex) when (ex is KiCadFormatException or ArgumentException)
        {
            _context?.Log.Error(ex.Message, ex);
        }
    }

    /// <summary>KiCad's setting that the board carries its fonts, written as one undoable step.</summary>
    internal void EmbedFonts(bool on)
    {
        if (EmbeddedFonts.Wanted(_board.Root) == on)
        {
            return;
        }

        _editor.Run(new Anode.Kicad.Editing.RootChildCommand(
            _board.Root, "embedded_fonts", Tr.T("pcb.command.embedFonts"), () => EmbeddedFonts.SetWanted(_board.Root, on)));
    }

    /// <summary>
    /// Writes one line of the board's title block as one undoable step: "title", "date", "rev", "company", or
    /// "comment3" for the third comment. The block is not an item on the board and may not exist yet, so the step
    /// remembers the whole block rather than a node that was never there.
    /// </summary>
    internal void EditTitleBlock(string field, string value)
    {
        int comment = field.StartsWith("comment", StringComparison.Ordinal)
            && int.TryParse(field.AsSpan("comment".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? n
                : 0;

        var block = _board.TitleBlock;
        string current = (comment > 0 ? block.Comment(comment) : field switch
        {
            "title" => block.Title,
            "date" => block.Date,
            "rev" => block.Revision,
            "company" => block.Company,
            _ => null,
        }) ?? string.Empty;

        string written = value.Trim();
        if (string.Equals(current, written, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _editor.Run(new Anode.Kicad.Editing.RootChildCommand(
                _board.Root,
                "title_block",
                Tr.T("pcb.command.titleBlock"),
                () =>
                {
                    if (comment > 0)
                    {
                        Anode.Kicad.Editing.TitleBlockWrites.SetComment(_board.Root, comment, written);
                    }
                    else
                    {
                        Anode.Kicad.Editing.TitleBlockWrites.Set(_board.Root, field, written);
                    }
                }));
        }
        catch (Exception ex) when (ex is KiCadFormatException or ArgumentException)
        {
            _context?.Log.Error(ex.Message, ex);
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
            BoardItem? focus = null;
            if (Scene.IsLive(_editor.FocusOwner) && Scene.Owner(_editor.FocusOwner) is { } clicked && !ReferenceEquals(clicked, item))
            {
                focus = clicked;
                properties.Add(new PropertyItem(ItemProperties.Header(focus).Title, string.Empty, IsSection: true));
                properties.AddRange(ItemProperties.For(focus));
            }

            // Where it lives is the document's to say: the item has never heard of a file.
            string? file = Path.GetFileName(FilePath);
            string? where = (file, subtitle) switch
            {
                ({ Length: > 0 }, { Length: > 0 }) => $"{file} · {subtitle}",
                ({ Length: > 0 }, _) => file,
                _ => subtitle,
            };

            return new SelectionInfo(title, where, properties, tag)
            {
                // A text of the footprint that was clicked is set from here too, though the footprint is what is selected.
                Blocks = [.. ItemProperties.Blocks(item, EditItem), .. focus is null ? [] : ItemProperties.Typography(focus, EditItem) is { } type ? new[] { type } : []],
            };
        }
    }

    public override IReadOnlyList<Issue> Issues => [.. _checks.Select(c => c.ToIssue()), .. DrawingSheetIssue(), .. FaceIssues()];

    /// <summary>Faces this machine lacks: what stands in, and whether KiCad's saved letters make that moot.</summary>
    internal IReadOnlyList<(string Face, string StandIn, bool Saved)> MissingFaces { get; init; } = [];

    private IEnumerable<Issue> FaceIssues() => MissingFaces.Select(m => new Issue(
        IssueSeverity.Warning,
        Tr.T("pcb.issue.face.title", m.Face),
        Tr.T(m.Saved ? "pcb.issue.face.saved" : "pcb.issue.face.detail", m.Face, m.StandIn),
        m.StandIn));

    /// <summary>The drawing sheet the project names, when it is missing or will not read; the default is drawn instead.</summary>
    internal string? DrawingSheetMissing { get; init; }

    private IEnumerable<Issue> DrawingSheetIssue()
    {
        if (DrawingSheetMissing is { } path)
        {
            yield return new Issue(
                IssueSeverity.Warning,
                Tr.T("pcb.issue.drawingSheet.title"),
                Tr.T("pcb.issue.drawingSheet.detail", Path.GetFileName(path)),
                path);
        }
    }

    public override Task<bool> SaveAsync(string? path = null)
    {
        string target = path ?? FilePath ?? throw new InvalidOperationException("The document has no path to save to.");

        // As KiCad does before it writes: the fonts the board's texts use are carried when it asks for that, and
        // dropped when it does not.
        EmbeddedFonts.Sync(_board.Root, DocumentFonts.Carried(DocumentFonts.Of(_board)));

        _editor.Save(target);
        FilePath = target;
        _overview = null;
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty), nameof(Overview));
        return Task.FromResult(true);
    }

    public override void Activate(IPluginContext context)
    {
        _context = context;
        CommandDescriptor[] commands =
        [
            new("edit.undo", "pcb.command.undo")
            {
                ScopeKey = "scope.board", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                MenuKey = "menu.edit", MenuOrder = 0,
                CanExecute = () => _editor.History.CanUndo,
                Execute = () => Guard(() => _editor.Undo(), context),
            },
            new("edit.redo", "pcb.command.redo")
            {
                ScopeKey = "scope.board", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 10,
                CanExecute = () => _editor.History.CanRedo,
                Execute = () => Guard(() => _editor.Redo(), context),
            },
            new("edit.delete", "pcb.command.delete")
            {
                ScopeKey = "scope.board", ShortcutText = "⌫", MenuKey = "menu.edit", MenuOrder = 20,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.DeleteSelection(), context),
            },
            new("pcb.move", "pcb.command.move")
            {
                ScopeKey = "scope.board", ShortcutText = "M", MenuKey = "menu.edit", MenuOrder = 30,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => _canvas?.BeginMoveWithCursor(),
            },
            new("pcb.rotate", "pcb.command.rotate")
            {
                ScopeKey = "scope.board", ShortcutText = "R", MenuKey = "menu.edit", MenuOrder = 40,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Rotate(90), context),
            },
            new("pcb.fit", "pcb.command.fit")
            {
                ScopeKey = "scope.board", ShortcutText = "Home", MenuKey = "menu.view", MenuOrder = 5,
                Execute = () => _canvas?.ZoomToFit(),
            },
            new("pcb.flip", "pcb.command.flip")
            {
                ScopeKey = "scope.board", ShortcutText = "⇧B", MenuKey = "menu.view", MenuOrder = 6,
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

    private void OnLanguageChanged()
    {
        _overview = null;
        OnPropertiesChanged(nameof(Title), nameof(Summary), nameof(StatusFields), nameof(Selection), nameof(Overview), nameof(Issues));
    }

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private void OnHistoryChanged()
    {
        _overview = null;

        // The page prints the title block, which no item on the board owns; one layer, simply drawn again.
        SceneBuilder.RedrawFrame(Scene);
        _canvas?.Redraw();
        OnPropertiesChanged(nameof(IsDirty), nameof(StatusFields));
    }
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
