using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
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
    private IPluginContext? _context;
    private IReadOnlyList<SchNet>? _nets;
    private (string LibId, LibSymbol Definition)? _part;
    private string _labelTool = "sch.tool.label";
    private string _shapeTool = "sch.tool.line";
    private string? _tool;
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
        _editor.History.Changed += ForgetNets;
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

            // Where it lives is the document's to say, not the item's: the item has never heard of a file.
            subtitle ??= Path.GetFileName(FilePath);
            return new SelectionInfo(title, subtitle, [.. SchItemProperties.For(item)], tag)
            {
                Blocks = [.. SchItemProperties.Blocks(item, (name, mutate) => _editor.Modify(name, [item], mutate), NetOf)],
                Actions = Actions(item),
            };
        }
    }

    public override IReadOnlyList<Issue> Issues => [.. _checks.Select(c => c.ToIssue()), .. LoosePins()];

    /// <summary>
    /// Pins that lead nowhere. Deliberately quiet: a pin is only reported when its net has just the one pin, nobody
    /// marked it no-connect, and the net has no name. A named net with a single pin is ordinary and correct — power
    /// goes to a symbol, a label carries the signal off the sheet — and a check that complained about those would
    /// teach its reader to stop looking.
    /// </summary>
    private IEnumerable<Issue> LoosePins()
    {
        foreach (var net in Nets)
        {
            if (net.IsNamed || net.IsNoConnect || net.Pins.Count != 1)
            {
                continue;
            }

            var pin = net.Pins[0];
            yield return new Issue(
                IssueSeverity.Warning,
                Tr.T("sch.issue.loosePin.title"),
                Tr.T("sch.issue.loosePin.detail", pin.ToString()),
                Tr.T("sch.issue.loosePin.location",
                    Units.NmToMm(pin.At.X).ToString("0.##", CultureInfo.InvariantCulture),
                    Units.NmToMm(pin.At.Y).ToString("0.##", CultureInfo.InvariantCulture),
                    Tr.T("sch.units.mm")));
        }
    }

    /// <summary>What the pointer can do on this sheet. The workbench floats these over the canvas.</summary>
    public override IReadOnlyList<ToolDescriptor> Tools =>
    [
        new("sch.tool.select", "sch.tool.select", Icons.Select) { ShortcutText = "Esc", Activate = () => UseTool(null) },
        new("sch.tool.wire", "sch.tool.wire", Icons.Wire) { ShortcutText = "W", Activate = () => UseTool("sch.tool.wire") },
        new("sch.tool.bus", "sch.tool.bus", Icons.Bus) { ShortcutText = "B", Activate = () => UseTool("sch.tool.bus") },
        new(_labelTool, _labelTool, Icons.Label)
        {
            ShortcutText = "L",
            Activate = () => UseTool(_labelTool),

            // One button for the label, its sorts behind a chevron; the one last chosen stays on the button.
            Variants =
            [
                new("sch.tool.label", "sch.tool.label", Icons.Label) { ShortcutText = "L", Activate = () => UseTool("sch.tool.label") },
                new("sch.tool.globalLabel", "sch.tool.globalLabel", Icons.Label) { ShortcutText = "⇧L", Activate = () => UseTool("sch.tool.globalLabel") },
                new("sch.tool.hierarchicalLabel", "sch.tool.hierarchicalLabel", Icons.Label) { Activate = () => UseTool("sch.tool.hierarchicalLabel") },
            ],
        },
        new("sch.tool.noConnect", "sch.tool.noConnect", Icons.NoConnect) { ShortcutText = "Q", Activate = () => UseTool("sch.tool.noConnect") },
        new("sch.tool.junction", "sch.tool.junction", Icons.Junction) { ShortcutText = "J", Activate = () => UseTool("sch.tool.junction") },
        new("sch.tool.busEntry", "sch.tool.busEntry", Icons.BusEntry) { Activate = () => UseTool("sch.tool.busEntry") },
        new("sch.tool.text", "sch.tool.text", Icons.Text) { ShortcutText = "T", Activate = () => UseTool("sch.tool.text") },
        new(_shapeTool, _shapeTool, ShapeIcon(_shapeTool))
        {
            Activate = () => UseTool(_shapeTool),
            Variants =
            [
                new("sch.tool.line", "sch.tool.line", Icons.Line) { Activate = () => UseTool("sch.tool.line") },
                new("sch.tool.rectangle", "sch.tool.rectangle", Icons.Rectangle) { Activate = () => UseTool("sch.tool.rectangle") },
                new("sch.tool.circle", "sch.tool.circle", Icons.Circle) { Activate = () => UseTool("sch.tool.circle") },
            ],
        },

        // A part chosen in the panel is a tool like any other, and must look like one: without this the pointer
        // carried a part with nothing anywhere to say so, and the next click placed a second one.
        .. _part is not null
            ? new ToolDescriptor[]
            {
                new("sch.tool.symbol", "sch.tool.symbol", Icons.Component) { Activate = () => UseTool("sch.tool.symbol") },
            }
            : [],
    ];

    /// <summary>What can be done to the selected item, shown in the inspector's footer.</summary>
    private IReadOnlyList<InspectorAction> Actions(SchItem item) => item switch
    {
        SchSheet sheet when sheet.SheetFile is { Length: > 0 } file =>
            [new InspectorAction(Tr.T("sch.action.openSheet"), () => OpenSheet(file))],
        _ => [],
    };

    /// <summary>Opens a child sheet as its own tab, beside this one.</summary>
    private void OpenSheet(string file)
    {
        if (_context is not { } context || FilePath is not { } path)
        {
            return;
        }

        string target = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, file);
        _ = context.Workbench.OpenAsync(target);
    }

    /// <summary>The shape button wears the kind it would draw.</summary>
    private static string ShapeIcon(string tool) => tool switch
    {
        "sch.tool.rectangle" => Icons.Rectangle,
        "sch.tool.circle" => Icons.Circle,
        _ => Icons.Line,
    };

    public override string? ActiveToolId => _tool;

    /// <summary>KiCad's bus entry steps one grid square down and to the right.</summary>
    private static readonly Vector2L BusStep = new(2_540_000, 2_540_000);

    /// <summary>The name is asked for on the canvas, where the label is being dropped.</summary>
    private PromptTool Label(SchematicCanvas canvas, SchLabelKind kind) =>
        Prompt(canvas, LabelToolId(kind), (name, at) => SchNodes.Label(kind, name, at));

    private static string LabelToolId(SchLabelKind kind) => kind switch
    {
        SchLabelKind.Global => "sch.tool.globalLabel",
        SchLabelKind.Hierarchical => "sch.tool.hierarchicalLabel",
        _ => "sch.tool.label",
    };

    /// <summary>Anything that is written before it is placed asks for its words the same way.</summary>
    private PromptTool Prompt(SchematicCanvas canvas, string id, Func<string, Vector2L, SchItem> make) =>
        new(_editor, id, point => canvas.AskForNameAsync(point, string.Empty), make, ex => _context?.Log.Error(ex.Message, ex));

    /// <summary>
    /// Arms the pointer with a part chosen in the symbols panel. The next click on the sheet drops it, and the tool
    /// stays armed, so several of the same part can be laid down without going back to the panel.
    /// </summary>
    public void ChoosePart(string libId, LibSymbol definition)
    {
        _part = (libId, definition);
        UseTool("sch.tool.symbol");
    }

    /// <summary>The part currently on the pointer, so the panel can mark the row it came from.</summary>
    public string? ChosenPart => _tool == "sch.tool.symbol" ? _part?.LibId : null;

    /// <summary>
    /// Puts a part down at a point of the canvas — what a drop from the components panel does, as against a click
    /// with the part already on the pointer. Answers false when the point is not on a sheet.
    /// </summary>
    public bool DropPart(string libId, LibSymbol definition, Point onCanvas)
    {
        if (_canvas?.SheetPointAt(onCanvas) is not { } at)
        {
            return false;
        }

        PlacePart(libId, definition, at);
        return true;
    }

    /// <summary>
    /// Writes a placed part: the definition copied into the sheet and the instance that draws from it, as one step,
    /// because one click made both and one undo must take back both.
    /// </summary>
    private void PlacePart(string libId, LibSymbol definition, Vector2L at)
    {
        try
        {
            var symbol = SchSymbols.Place(Sheet, libId, definition, at, Designator(definition), ProjectName(), SchSymbols.PathOf(Sheet));
            _editor.Run(new CompositeCommand(
                Tr.T("sch.command.symbol"),
                [new AddLibrarySymbolCommand(Sheet, libId, definition), new AddNodesCommand(Sheet, [symbol])]));
        }
        catch (Exception ex) when (ex is KiCadFormatException or InvalidOperationException or NotSupportedException)
        {
            _context?.Log.Error(ex.Message, ex);
            _context?.Workbench.ShowBanner(new Banner(ex.Message, IsAlert: true));
        }
    }

    /// <summary>
    /// What the part is called as it lands: the library's own prefix and the next free number on the sheet. KiCad
    /// writes "R?" and numbers later; a part that arrives already named saves that second pass, and the numbering
    /// rule is the same one either way — never take a number the sheet has already used.
    /// </summary>
    private string Designator(LibSymbol definition)
    {
        string prefix = SchAnnotation.PrefixOf(definition.Reference);
        return prefix + SchAnnotation.NextNumber(Sheet, prefix).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Numbers everything on the sheet that is still waiting, as one step. For sheets that arrived from elsewhere
    /// with their parts unnumbered; what this application places is numbered as it lands.
    /// </summary>
    private void Annotate()
    {
        var given = SchAnnotation.Annotate(Sheet, Sheet.Symbols);
        if (given.Count == 0)
        {
            return;
        }

        _editor.Modify(Tr.T("sch.command.annotate"), [.. given.Select(g => g.Symbol)], () =>
        {
            foreach (var (symbol, reference) in given)
            {
                SchWrites.SetReference(symbol, reference);
            }
        });
    }

    /// <summary>The project a sheet belongs to, as its .kicad_pro is named; the sheet's own name otherwise.</summary>
    private string ProjectName()
    {
        if (ProjectLibraries.ProjectFolder(FilePath) is { } folder
            && Directory.EnumerateFiles(folder, "*.kicad_pro").Order(StringComparer.Ordinal).FirstOrDefault() is { } project)
        {
            return Path.GetFileNameWithoutExtension(project);
        }

        return Path.GetFileNameWithoutExtension(FilePath ?? string.Empty);
    }

    /// <summary>Puts a tool on the pointer, or takes it off; the buttons and the canvas follow.</summary>
    public void UseTool(string? id)
    {
        _tool = id;
        if (id is "sch.tool.label" or "sch.tool.globalLabel" or "sch.tool.hierarchicalLabel")
        {
            _labelTool = id;
        }

        if (id is "sch.tool.line" or "sch.tool.rectangle" or "sch.tool.circle")
        {
            _shapeTool = id;
        }

        if (_canvas is { } canvas)
        {
            canvas.Tool = id switch
            {
                "sch.tool.wire" => new WireTool(_editor),
                "sch.tool.bus" => new WireTool(_editor, bus: true),
                "sch.tool.label" => Label(canvas, SchLabelKind.Local),
                "sch.tool.globalLabel" => Label(canvas, SchLabelKind.Global),
                "sch.tool.hierarchicalLabel" => Label(canvas, SchLabelKind.Hierarchical),
                "sch.tool.text" => Prompt(canvas, "sch.tool.text", (written, at) => SchNodes.Text(written, at)),
                "sch.tool.line" => new ShapeTool(_editor, "sch.tool.line", SchShapeKind.Polyline),
                "sch.tool.rectangle" => new ShapeTool(_editor, "sch.tool.rectangle", SchShapeKind.Rectangle),
                "sch.tool.circle" => new ShapeTool(_editor, "sch.tool.circle", SchShapeKind.Circle),
                "sch.tool.symbol" when _part is { } part => new SymbolTool(_editor, part.LibId, part.Definition, PlacePart),
                "sch.tool.noConnect" => new PlaceTool(_editor, "sch.tool.noConnect", SchNodes.NoConnect, _ => null),
                "sch.tool.junction" => new PlaceTool(_editor, "sch.tool.junction", SchNodes.Junction, _ => null),
                "sch.tool.busEntry" => new PlaceTool(_editor, "sch.tool.busEntry", at => SchNodes.BusEntry(at, BusStep), _ => null),
                _ => null,
            };
        }

        OnPropertiesChanged(nameof(ActiveToolId), nameof(StatusFields));
    }

    public override Task<bool> SaveAsync(string? path = null)
    {
        _editor.Save(path ?? FilePath ?? throw new InvalidOperationException("The sheet has no path to save to."));
        FilePath = path ?? FilePath;
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty));
        return Task.FromResult(true);
    }

    public override void Activate(IPluginContext context)
    {
        _context = context;
        CommandDescriptor[] commands =
        [
            new("edit.undo", "sch.command.undo")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                MenuKey = "menu.edit", MenuOrder = 0,
                CanExecute = () => _editor.History.CanUndo,
                Execute = () => Guard(() => _editor.Undo(), context),
            },
            new("edit.redo", "sch.command.redo")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 10,
                CanExecute = () => _editor.History.CanRedo,
                Execute = () => Guard(() => _editor.Redo(), context),
            },
            new("edit.cut", "sch.command.cut")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘X", Gesture = Shortcut(Key.X),
                MenuKey = "menu.edit", MenuOrder = 12,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Cut(), context),
            },
            new("edit.copy", "sch.command.copy")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘C", Gesture = Shortcut(Key.C),
                MenuKey = "menu.edit", MenuOrder = 14,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Copy(), context),
            },
            new("edit.paste", "sch.command.paste")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘V", Gesture = Shortcut(Key.V),
                MenuKey = "menu.edit", MenuOrder = 16,
                CanExecute = () => _editor.CanPaste,
                Execute = () => Guard(() => _canvas?.PasteAtCursor(), context),
            },
            new("edit.duplicate", "sch.command.duplicate")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘D", Gesture = Shortcut(Key.D),
                MenuKey = "menu.edit", MenuOrder = 18,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Duplicate(), context),
            },
            new("edit.delete", "sch.command.delete")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌫", MenuKey = "menu.edit", MenuOrder = 20,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.DeleteSelection(), context),
            },
            new("sch.move", "sch.command.move")
            {
                ScopeKey = "scope.schematic", ShortcutText = "M", MenuKey = "menu.edit", MenuOrder = 30,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => _canvas?.BeginMoveWithCursor(),
            },
            new("sch.rotate", "sch.command.rotate")
            {
                ScopeKey = "scope.schematic", ShortcutText = "R", MenuKey = "menu.edit", MenuOrder = 40,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Rotate(90), context),
            },
            new("sch.mirror", "sch.command.mirror")
            {
                ScopeKey = "scope.schematic", ShortcutText = "X", MenuKey = "menu.edit", MenuOrder = 50,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Mirror(horizontal: true), context),
            },
            new("sch.rotateCw", "sch.command.rotateCw")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⇧R", MenuKey = "menu.edit", MenuOrder = 45,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Rotate(-90), context),
            },
            new("sch.mirrorVertical", "sch.command.mirrorVertical")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Y", MenuKey = "menu.edit", MenuOrder = 55,
                CanExecute = () => _editor.Selection.Count > 0,
                Execute = () => Guard(() => _editor.Mirror(horizontal: false), context),
            },
            new("sch.tool.select", "sch.tool.select")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Esc", Gesture = new KeyGesture(Key.Escape),
                CanExecute = () => _tool is not null || _canvas is not null,

                // The first Esc ends the run in progress; with none, it puts the pointer back to selecting.
                Execute = () =>
                {
                    if (_canvas?.CancelToolRun() != true)
                    {
                        UseTool(null);
                    }
                },
            },
            new("sch.tool.wire", "sch.command.wire")
            {
                ScopeKey = "scope.schematic", ShortcutText = "W", MenuKey = "menu.place", MenuOrder = 0,
                Execute = () => UseTool("sch.tool.wire"),
            },
            new("sch.tool.bus", "sch.command.bus")
            {
                ScopeKey = "scope.schematic", ShortcutText = "B", MenuKey = "menu.place", MenuOrder = 10,
                Execute = () => UseTool("sch.tool.bus"),
            },
            new("sch.tool.label", "sch.command.label")
            {
                ScopeKey = "scope.schematic", ShortcutText = "L", MenuKey = "menu.place", MenuOrder = 20,
                Execute = () => UseTool("sch.tool.label"),
            },
            new("sch.tool.globalLabel", "sch.command.globalLabel")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⇧L", MenuKey = "menu.place", MenuOrder = 30,
                Execute = () => UseTool("sch.tool.globalLabel"),
            },
            new("sch.tool.hierarchicalLabel", "sch.command.hierarchicalLabel")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 40,
                Execute = () => UseTool("sch.tool.hierarchicalLabel"),
            },
            new("sch.tool.noConnect", "sch.command.noConnect")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Q", MenuKey = "menu.place", MenuOrder = 50,
                Execute = () => UseTool("sch.tool.noConnect"),
            },
            new("sch.tool.junction", "sch.command.junction")
            {
                ScopeKey = "scope.schematic", ShortcutText = "J", MenuKey = "menu.place", MenuOrder = 55,
                Execute = () => UseTool("sch.tool.junction"),
            },
            new("sch.tool.busEntry", "sch.command.busEntry")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 60,
                Execute = () => UseTool("sch.tool.busEntry"),
            },
            new("sch.tool.text", "sch.command.text")
            {
                ScopeKey = "scope.schematic", ShortcutText = "T", MenuKey = "menu.place", MenuOrder = 70,
                Execute = () => UseTool("sch.tool.text"),
            },
            new("sch.tool.line", "sch.command.line")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 80,
                Execute = () => UseTool("sch.tool.line"),
            },
            new("sch.tool.rectangle", "sch.command.rectangle")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 90,
                Execute = () => UseTool("sch.tool.rectangle"),
            },
            new("sch.tool.circle", "sch.command.circle")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 100,
                Execute = () => UseTool("sch.tool.circle"),
            },
            new("sch.properties", "sch.command.properties")
            {
                ScopeKey = "scope.schematic", ShortcutText = "E", MenuKey = "menu.edit", MenuOrder = 60,
                CanExecute = () => _editor.Selection.Count == 1,

                // The values live in the inspector, so E puts the caret in the first one that can be written.
                Execute = () => context.Workbench.FocusInspector(),
            },
            new("sch.annotate", "sch.command.annotate")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 70,
                CanExecute = () => SchAnnotation.Unannotated(Sheet).Count > 0,
                Execute = () => Guard(Annotate, context),
            },
            new("sch.fit", "sch.command.fit")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Home", MenuKey = "menu.view", MenuOrder = 5,
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
        _editor.History.Changed -= ForgetNets;
        Tr.Changed -= OnLanguageChanged;
        base.Dispose();
    }

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Editor = _editor };
        canvas.ToolCancelled += () => UseTool(null);
        canvas.PartDropped = (part, at) => DropPart(part.LibId, part.Symbol, at);
        canvas.ContextMenu = SheetMenu();
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

    /// <summary>
    /// The sheet's own menu: the tools first, then what can be done to what is selected. The items read the command
    /// registry as the menu opens, so they say what the document says and grey out when there is nothing to do.
    /// </summary>
    private ContextMenu SheetMenu()
    {
        var menu = new ContextMenu();

        foreach (var tool in Tools)
        {
            var item = new MenuItem { Header = tool.Title, Icon = Icons.Draw(tool.IconKey, 14) };
            var chosen = tool;
            item.Click += (_, _) => chosen.Activate();
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        foreach (string id in (string[])
            ["edit.undo", "edit.redo", "edit.cut", "edit.copy", "edit.paste", "edit.duplicate", "sch.rotate", "sch.mirror", "edit.delete"])
        {
            string commandId = id;
            var item = new MenuItem { Tag = commandId };
            item.Click += (_, _) => _context?.Commands.TryExecute(commandId);
            menu.Items.Add(item);
        }

        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Tag is string))
            {
                var command = _context?.Commands.Find((string)item.Tag!);
                item.Header = command?.Title ?? (string)item.Tag!;
                item.InputGesture = null;
                item.IsEnabled = command is not null && Safe(command);
            }

            foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Tag is null))
            {
                item.Header = Tools.FirstOrDefault(t => t.Title == (string?)item.Header)?.Title ?? item.Header;
            }
        };

        return menu;

        static bool Safe(CommandDescriptor command)
        {
            try
            {
                return command.CanExecute();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The nets of this sheet, worked out once and kept until something changes them. The whole sheet is read to
    /// answer even one question about it, so answering per selection would mean reading it again on every click.
    /// </summary>
    public IReadOnlyList<SchNet> Nets => _nets ??= SchConnectivity.Build(Sheet);

    /// <summary>What an item is connected to, by name; null when it is on nothing or nothing is known.</summary>
    public string? NetOf(SchItem item) =>
        Nets.FirstOrDefault(net => net.Items.Contains(item))?.Name;

    /// <summary>The canvas draws again when the history moves; the workbench re-reads the document's state.</summary>
    public void Redraw() => _canvas?.Redraw();

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private void ForgetNets() => _nets = null;

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
