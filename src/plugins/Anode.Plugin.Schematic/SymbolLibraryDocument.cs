using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>The symbol libraries a person opens: a <c>.kicad_sym</c> file is a tab of its own.</summary>
internal sealed class SymbolLibraryDocumentType : IDocumentType
{
    public const string TypeId = "anode.symlib";

    public string Id => TypeId;

    public string Label => Tr.T("sch.lib.label");

    public IReadOnlyList<string> Extensions { get; } = [".kicad_sym"];

    public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => Task.Run<IDocument>(
        () => new SymbolLibraryDocument(path, SymbolLibrary.Load(path)),
        cancellationToken);
}

/// <summary>
/// A symbol library, as KiCad's symbol editor holds one: the file is the document — it is what is saved — and one of
/// its symbols is on the canvas at a time, in one of its units and body styles. The library's symbols are listed in a
/// panel beside it; choosing one there brings it up here.
///
/// A symbol is drawn in its own coordinates, Y upward as the library writes them, through the flip the scene
/// builder applies; what the inspector and the status bar say is in the library's terms too.
/// </summary>
internal sealed class SymbolLibraryDocument : DocumentBase
{
    private readonly List<IDisposable> _registrations = [];
    private SchematicEditor? _editor;
    private SchematicCanvas? _canvas;
    private string _cursor = string.Empty;
    private string? _tool;

    public SymbolLibraryDocument(string path, SymbolLibrary library)
    {
        FilePath = Path.GetFullPath(path);
        Library = library;
        History.Changed += OnHistoryChanged;
        if (library.Symbols.FirstOrDefault() is { } first)
        {
            Show(first.Name);
        }
    }

    /// <summary>What has been done to the library, to undo: one history for the whole file, as it is one thing saved.</summary>
    public UndoStack History { get; } = new();

    public override bool IsDirty => History.IsDirty;

    public SymbolLibrary Library { get; }

    public override string? DocumentTypeId => SymbolLibraryDocumentType.TypeId;

    public override string Title => Path.GetFileName(FilePath ?? string.Empty);

    public override string Summary => Tr.T("sch.lib.count", Library.Symbols.Count);

    /// <summary>The symbol on the canvas, or null in an empty library.</summary>
    public LibSymbol? Current { get; private set; }

    /// <summary>
    /// Whose body is drawn for the symbol on the canvas: its own, or — for a symbol derived from another with
    /// <c>extends</c> — the one it is derived from, which is where a derived symbol's body lives.
    /// </summary>
    public LibSymbol? Body => Current is { Extends: { Length: > 0 } parent } ? Library.Find(parent) ?? Current : Current;

    /// <summary>Which unit is shown: 1 for A, 2 for B…</summary>
    public int Unit { get; private set; } = 1;

    /// <summary>Which body style is shown: 1 for the ordinary one, 2 for De Morgan's.</summary>
    public int BodyStyle { get; private set; } = 1;

    internal SchematicEditor? Editor => _editor;

    public override bool CanSave => true;

    /// <summary>Brings a symbol of the library onto the canvas, at its first unit and ordinary body.</summary>
    public void Show(string name)
    {
        if (Library.Find(name) is not { } symbol)
        {
            return;
        }

        Current = symbol;
        Unit = 1;
        BodyStyle = 1;
        Rebuild();
    }

    public void ShowUnit(int unit)
    {
        if (Body is { } body && unit >= 1 && unit <= body.UnitCount && unit != Unit)
        {
            Unit = unit;
            Rebuild();
        }
    }

    public void ShowBodyStyle(int style)
    {
        if (Body is { HasAlternateBody: true } && style is 1 or 2 && style != BodyStyle)
        {
            BodyStyle = style;
            Rebuild();
        }
    }

    /// <summary>
    /// Writes the library where it was read from, or to another file. It is written as it stands: a library
    /// nobody changed comes out byte for byte as it went in.
    /// </summary>
    public override Task<bool> SaveAsync(string? path = null)
    {
        string target = path ?? FilePath ?? throw new InvalidOperationException("The library has no path to save to.");
        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        Library.Save(temp);
        File.Move(temp, target, overwrite: true);
        FilePath = Path.GetFullPath(target);
        History.MarkSaved();
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty));
        return Task.FromResult(true);
    }

    public override IReadOnlyList<StatusField> StatusFields =>
    [
        .. _cursor.Length > 0 ? new[] { new StatusField(_cursor) } : [],
        new(Current is { } symbol ? Tr.T("sch.lib.status.symbol", symbol.Name, UnitName(Unit)) : Tr.T("sch.lib.empty")),
    ];

    /// <summary>What is selected on the canvas, described as the library describes it.</summary>
    public override SelectionInfo? Selection =>
        _editor?.Selection is [var item] ? Describe(item) : null;

    /// <summary>With nothing selected, the symbol itself.</summary>
    public override SelectionInfo? Overview => Current is { } symbol ? DescribeSymbol(symbol) : null;

    public override IReadOnlyList<ToolDescriptor> Tools =>
    [
        new("sch.tool.select", "sch.tool.select", Icons.Select) { ShortcutText = "Esc", Activate = () => UseTool(null) },
        new("sch.tool.pin", "sch.tool.pin", Icons.Pin) { ShortcutText = "P", Activate = () => UseTool("sch.tool.pin") },
        new("sch.tool.line", "sch.tool.line", Icons.Line) { Activate = () => UseTool("sch.tool.line") },
        new("sch.tool.rectangle", "sch.tool.rectangle", Icons.Rectangle) { Activate = () => UseTool("sch.tool.rectangle") },
        new("sch.tool.circle", "sch.tool.circle", Icons.Circle) { Activate = () => UseTool("sch.tool.circle") },
        new("sch.tool.arc", "sch.tool.arc", Icons.Circle) { Activate = () => UseTool("sch.tool.arc") },
    ];

    public override string? ActiveToolId => _tool;

    /// <summary>Puts a drawing tool on the pointer, or takes it off; a symbol that lends its body draws nothing.</summary>
    public void UseTool(string? id)
    {
        _tool = id is not null && Current is { } symbol && ReferenceEquals(Body, symbol) ? id : null;
        if (_canvas is { } canvas)
        {
            canvas.Tool = _tool is { } tool ? MakeTool(tool) : null;
        }

        OnPropertiesChanged(nameof(ActiveToolId), nameof(StatusFields));
    }

    private ISchTool? MakeTool(string id) => _editor is not { } editor ? null : id switch
    {
        "sch.tool.pin" => new PinTool(editor, MakePin),
        "sch.tool.line" => new ShapeTool(editor, "sch.tool.line", SchShapeKind.Polyline),
        "sch.tool.rectangle" => new ShapeTool(editor, "sch.tool.rectangle", SchShapeKind.Rectangle),
        "sch.tool.circle" => new ShapeTool(editor, "sch.tool.circle", SchShapeKind.Circle),
        "sch.tool.arc" => new PointsTool(editor, "sch.tool.arc", 3,
            p => SchNodes.Arc(p[0], p[2], p[1]),
            p => p.Count >= 3 && Anode.Geometry.ArcMath.FromStartMidEnd(p[0].ToDouble(), p[2].ToDouble(), p[1].ToDouble()) is { } arc
                ? Anode.Geometry.ArcMath.Tessellate(arc)
                : [.. p.Select(q => q.ToDouble())]),
        _ => null,
    };

    /// <summary>
    /// A new pin, as KiCad's pin tool first makes one: an input, drawn as a plain line, 100 mil long, pointing right
    /// into the body, unnamed, and numbered one past the highest number the symbol has. It is made at the scene's
    /// point; the rules turn it over into the library's.
    /// </summary>
    internal SchItem MakePin(Anode.Geometry.Vector2L at)
    {
        int next = (Body?.Pins ?? []).Select(p => int.TryParse(p.Number, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        string length = Anode.Kicad.KiCadNumber.FormatMm(PinTool.LengthNm);
        string text =
            $"(pin input line (at {Anode.Kicad.KiCadNumber.FormatMm(at.X)} {Anode.Kicad.KiCadNumber.FormatMm(at.Y)} 0) (length {length})"
            + " (name \"~\" (effects (font (size 1.27 1.27))))"
            + $" (number \"{next.ToString(CultureInfo.InvariantCulture)}\" (effects (font (size 1.27 1.27)))))";
        return new SchPin(SchNodes.Adopt(Anode.Sexpr.SDocument.Parse(text).Root));
    }

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Editor = _editor };
        canvas.ToolCancelled += () => UseTool(null);
        canvas.CursorMoved += position =>
        {
            // The canvas speaks sheet millimetres, Y down; the library's Y runs up.
            _cursor = position is { } p
                ? Tr.T("sch.status.cursor",
                    p.X.ToString("0.00", CultureInfo.InvariantCulture),
                    (-p.Y).ToString("0.00", CultureInfo.InvariantCulture),
                    Tr.T("sch.units.mm"))
                : string.Empty;
            OnPropertyChanged(nameof(StatusFields));
        };
        _canvas = canvas;
        return new SymbolLibraryView(this, canvas);
    }

    /// <summary>
    /// Adds a new symbol to the library under <paramref name="name"/>, as KiCad's New Symbol does, and brings it up —
    /// one step to undo. Answers why the name will not do, in the words the person is shown, or null when it did.
    /// </summary>
    public string? AddSymbol(string name)
    {
        name = name.Trim();
        if (Library.NameProblem(name) is { } problem)
        {
            return Tr.T("sch.lib.name." + problem, name);
        }

        var symbol = SymbolLibrary.NewSymbol(name);
        History.Execute(new AddNodesCommand(Library, [symbol]));
        Show(name);
        return null;
    }

    public override void Activate(IPluginContext context)
    {
        CommandDescriptor[] commands =
        [
            new("edit.undo", "sch.command.undo")
            {
                ScopeKey = "scope.symlib", ShortcutText = "⌘Z", Gesture = Shortcut(Key.Z),
                MenuKey = "menu.edit", MenuOrder = 0,
                CanExecute = () => History.CanUndo,
                Execute = Undo,
            },
            new("edit.redo", "sch.command.redo")
            {
                ScopeKey = "scope.symlib", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 10,
                CanExecute = () => History.CanRedo,
                Execute = Redo,
            },
            new("sch.lib.tool.pin", "sch.tool.pin")
            {
                ScopeKey = "scope.symlib", ShortcutText = "P", Gesture = new KeyGesture(Key.P),
                MenuKey = "menu.place", MenuOrder = 0,
                Execute = () => UseTool("sch.tool.pin"),
            },
            new("sch.lib.newSymbol", "sch.command.newSymbol")
            {
                ScopeKey = "scope.symlib", ShortcutText = "⌘N", Gesture = Shortcut(Key.N),
                MenuKey = "menu.file", MenuOrder = 5,
                Execute = () =>
                {
                    context.Workbench.RevealPanel(SymbolListPanel.PanelId);
                    SymbolListPanel.RequestNewSymbol();
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
        History.Changed -= OnHistoryChanged;
        base.Dispose();
    }

    private static KeyGesture Shortcut(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | extra);

    /// <summary>Takes back the last step on the library, whichever symbol it was made on, and draws the canvas anew.</summary>
    public void Undo()
    {
        if (History.CanUndo)
        {
            _editor?.CancelMove();
            History.Undo();
            AfterHistory();
        }
    }

    public void Redo()
    {
        if (History.CanRedo)
        {
            History.Redo();
            AfterHistory();
        }
    }

    /// <summary>
    /// An undo or redo may take away the symbol on the canvas — a new one undone — or change it anywhere; the
    /// canvas is drawn afresh, from the first symbol when the one shown is gone or none was shown.
    /// </summary>
    private void AfterHistory()
    {
        if (Current is null or { IsAttached: false })
        {
            Current = Library.Symbols.FirstOrDefault();
            Unit = BodyStyle = 1;
        }

        Rebuild();
    }

    /// <summary>
    /// A step done or undone: the file now differs from what was saved, or no longer does, and the lists and the
    /// inspector say so. The canvas redraws what an edit touched itself.
    /// </summary>
    private void OnHistoryChanged() =>
        OnPropertiesChanged(nameof(IsDirty), nameof(Summary), nameof(Library), nameof(Selection), nameof(Overview));

    /// <summary>The letter a unit is known by: A, B, … — KiCad's <c>LetterSubReference</c>.</summary>
    public static string UnitName(int unit) => SchFind.UnitLetters(unit);

    private void Rebuild()
    {
        if (_editor is { } previous)
        {
            previous.SelectionChanged -= OnSelectionChanged;
            previous.SceneChanged -= OnSelectionChanged;
        }

        if (Current is { } symbol && Body is { } body)
        {
            // What is drawn next goes into the unit and body on screen.
            body.Drawing = (Unit, BodyStyle);
            _editor = new SchematicEditor(SchematicSceneBuilder.BuildSymbol(symbol, body, Unit, BodyStyle), new SymbolRules(symbol, body), History);
            _editor.SelectionChanged += OnSelectionChanged;
            _editor.SceneChanged += OnSelectionChanged;
        }
        else
        {
            _editor = null;
        }

        if (_canvas is { } canvas)
        {
            canvas.Editor = _editor;

            // A tool in hand stays in hand across a change of symbol or unit — on the new editor.
            canvas.Tool = _tool is { } tool ? MakeTool(tool) : null;
        }

        OnPropertiesChanged(nameof(Current), nameof(Unit), nameof(BodyStyle), nameof(Selection), nameof(Overview), nameof(StatusFields));
    }

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private SelectionInfo DescribeSymbol(LibSymbol symbol)
    {
        var body = Body ?? symbol;
        bool own = ReferenceEquals(body, symbol);

        // The fields a library symbol is described by, each written where it stands — a field the symbol lacks is
        // not offered, since adding fields is not done here yet.
        var rows = new List<InspectorRow>();
        foreach (var (name, key) in new[]
        {
            ("Reference", "reference"), ("Value", "value"), ("Description", "description"),
            ("ki_keywords", "keywords"), ("Footprint", "footprint"), ("Datasheet", "datasheet"),
        })
        {
            if (symbol.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) is { } field)
            {
                rows.Add(new InspectorRow(Tr.T("sch.lib.row." + key), field.Value)
                {
                    Commit = v => Edit(key, [field], () => SymbolWrites.SetFieldValue(field, v)),
                });
            }
        }

        if (symbol.Extends is { Length: > 0 } parent)
        {
            rows.Add(new InspectorRow(Tr.T("sch.lib.row.extends"), parent));
        }

        var drawing = new List<InspectorRow>
        {
            new(Tr.T("sch.lib.row.units"), body.UnitCount.ToString(CultureInfo.InvariantCulture)),
            new(Tr.T("sch.lib.row.deMorgan"), YesNo(body.HasAlternateBody)),
            new(Tr.T("sch.lib.row.power"), YesNo(symbol.IsPower)),
            new(Tr.T("sch.lib.row.pinNumbers"), Tr.T("sch.lib.shown"))
            {
                Switch = body.ShowPinNumbers,
                Commit = own ? v => EditSymbol("pinNumbers", body, () => SymbolWrites.SetPinTextShown(body, "pin_numbers", v == "yes")) : null,
            },
            new(Tr.T("sch.lib.row.pinNames"), Tr.T("sch.lib.shown"))
            {
                Switch = body.ShowPinNames,
                Commit = own ? v => EditSymbol("pinNames", body, () => SymbolWrites.SetPinTextShown(body, "pin_names", v == "yes")) : null,
            },
            new(Tr.T("sch.lib.row.pinNameOffset"), Mm(body.PinNameOffset)),
        };

        var pins = body.PinsOf(Unit, BodyStyle)
            .OrderBy(p => p.Number, KicadOrderComparer)
            .Select(p => new InspectorRow(p.Number, p.Name == "~" ? string.Empty : p.Name) { Trailing = p.ElectricalType })
            .ToList();

        return new SelectionInfo(symbol.Name, Title, [], Tr.T("sch.lib.tag.symbol"))
        {
            Blocks =
            [
                new InspectorBlock(Tr.T("sch.lib.block.symbol"), rows),
                new InspectorBlock(Tr.T("sch.lib.block.drawing"), drawing),
                new InspectorBlock(Tr.T("sch.lib.block.pins", pins.Count, UnitName(Unit)), pins) { IsConnections = true },
            ],
        };
    }

    private SelectionInfo? Describe(SchItem item)
    {
        // What belongs to a derived symbol's parent is shown, not written: the body is the parent's to change.
        bool own = Current is { } shown && (item is SchField ? shown.Fields.Contains(item) : ReferenceEquals(Body, shown));
        Action<string>? Writes(string key, Action<string> write) => own ? v => Edit(key, [item], () => write(v)) : null;

        switch (item)
        {
            case SchPin pin:
                return new SelectionInfo(pin.Name is { Length: > 0 } and not "~" ? pin.Name : pin.Number, Current?.Name, [], Tr.T("sch.lib.tag.pin"))
                {
                    Blocks =
                    [
                        new InspectorBlock(Tr.T("sch.lib.block.pin"),
                        [
                            new(Tr.T("sch.lib.row.number"), pin.Number) { Commit = Writes("number", v => SymbolWrites.SetPinNumber(pin, v)) },
                            new(Tr.T("sch.lib.row.name"), pin.Name) { Commit = Writes("name", v => SymbolWrites.SetPinName(pin, v)) },
                            new(Tr.T("sch.lib.row.type"), pin.ElectricalType)
                            {
                                Choices = SymbolWrites.PinTypes,
                                Commit = Writes("type", v => SymbolWrites.SetPinType(pin, v)),
                            },
                            new(Tr.T("sch.lib.row.shape"), pin.GraphicStyle)
                            {
                                Choices = SymbolWrites.PinShapes,
                                Commit = Writes("shape", v => SymbolWrites.SetPinShape(pin, v)),
                            },
                            new(Tr.T("sch.lib.row.length"), MmValue(pin.Length))
                            {
                                Commit = Writes("length", v =>
                                {
                                    if (ParseMm(v) is { } nm)
                                    {
                                        SymbolWrites.SetPinLength(pin, nm);
                                    }
                                }),
                            },
                            new(Tr.T("sch.lib.row.orientation"), Tr.T("sch.lib.orientation." + Orientation(pin.Angle)))
                            {
                                Choices = [.. Directions.Select(d => Tr.T("sch.lib.orientation." + d.Word))],
                                Commit = Writes("orientation", v =>
                                {
                                    if (Directions.FirstOrDefault(d => Tr.T("sch.lib.orientation." + d.Word) == v) is { Word: not null } chosen)
                                    {
                                        SymbolWrites.SetPinAngle(pin, chosen.Angle);
                                    }
                                }),
                            },
                            new(Tr.T("sch.lib.row.position"), Point(pin.Position)),
                            new(Tr.T("sch.lib.row.hidden"), Tr.T("sch.lib.hiddenWord"))
                            {
                                Switch = pin.IsHidden,
                                Commit = Writes("hidden", v => SymbolWrites.SetPinHidden(pin, v == "yes")),
                            },
                        ]),
                    ],
                };

            case SchGraphic graphic:
                return new SelectionInfo(Tr.T("sch.lib.shape." + graphic.Kind.ToString().ToLowerInvariant()), Current?.Name, [], Tr.T("sch.lib.tag.graphic"))
                {
                    Blocks =
                    [
                        new InspectorBlock(Tr.T("sch.lib.block.graphic"),
                        [
                            new(Tr.T("sch.lib.row.strokeWidth"), MmValue(graphic.StrokeWidth))
                            {
                                Commit = Writes("strokeWidth", v =>
                                {
                                    if (ParseMm(v) is { } nm)
                                    {
                                        SymbolWrites.SetStrokeWidth(graphic, nm);
                                    }
                                }),
                            },
                            new(Tr.T("sch.lib.row.strokeType"), graphic.StrokeStyle),
                            new(Tr.T("sch.lib.row.fill"), graphic.Fill)
                            {
                                Choices = SymbolWrites.Fills,
                                Commit = Writes("fill", v => SymbolWrites.SetFill(graphic, v)),
                            },
                        ]),
                    ],
                };

            case SchField field:
                return new SelectionInfo(field.Name, Current?.Name, [], Tr.T("sch.lib.tag.field"))
                {
                    Blocks =
                    [
                        new InspectorBlock(Tr.T("sch.lib.block.field"),
                        [
                            new(Tr.T("sch.lib.row.value"), field.Value) { Commit = Writes("value", v => SymbolWrites.SetFieldValue(field, v)) },
                            new(Tr.T("sch.lib.row.position"), Point(field.Position)),
                            new(Tr.T("sch.lib.row.hidden"), Tr.T("sch.lib.hiddenWord"))
                            {
                                Switch = field.IsHidden,
                                Commit = Writes("hidden", v => SymbolWrites.SetFieldHidden(field, v == "yes")),
                            },
                        ]),
                    ],
                };

            default:
                return null;
        }
    }

    /// <summary>One change to pins, shapes or fields, as a step on the library's history; the canvas redraws them.</summary>
    private void Edit(string key, IReadOnlyList<SchItem> items, Action write)
    {
        _editor?.Modify(Tr.T("sch.lib.edit", Tr.T("sch.lib.row." + key)), items, write);
        OnPropertiesChanged(nameof(Selection), nameof(Overview));
    }

    /// <summary>A change to the symbol as a whole — how its pins read — after which the canvas is drawn afresh.</summary>
    private void EditSymbol(string key, LibSymbol symbol, Action write)
    {
        History.Execute(new ModifyNodesCommand(Tr.T("sch.lib.edit", Tr.T("sch.lib.row." + key)), [symbol], write));
        Rebuild();
    }

    /// <summary>The four ways a pin can point, in KiCad's words, and the angle each is written as.</summary>
    private static readonly (string Word, double Angle)[] Directions = [("right", 0), ("up", 90), ("left", 180), ("down", 270)];

    private static string MmValue(long nm) => (nm / 1_000_000.0).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A length typed in millimetres — with a point or a comma — as nanometres; null when it is not a length.</summary>
    private static long? ParseMm(string text) =>
        double.TryParse(text.Trim().Replace(',', '.').Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double mm) && mm >= 0 && mm < 10_000
            ? (long)Math.Round(mm * 1_000_000)
            : null;

    /// <summary>
    /// Which way a pin points from where a wire meets it, in KiCad's words: its angle 0 is "right", 90 "up",
    /// 180 "left", 270 "down".
    /// </summary>
    private static string Orientation(double angle) => ((((int)Math.Round(angle / 90) % 4) + 4) % 4) switch
    {
        0 => "right",
        1 => "up",
        2 => "left",
        _ => "down",
    };

    private static string YesNo(bool on) => Tr.T(on ? "sch.lib.yes" : "sch.lib.no");

    private static string Mm(long nm) => (nm / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture) + " " + Tr.T("sch.units.mm");

    private static string Point(Anode.Geometry.Vector2L at) =>
        $"{(at.X / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture)} / {(at.Y / 1_000_000.0).ToString("0.###", CultureInfo.InvariantCulture)}";

    private static readonly IComparer<string> KicadOrderComparer = Comparer<string>.Create((a, b) =>
    {
        bool na = int.TryParse(a, out int x), nb = int.TryParse(b, out int y);
        return na && nb ? x.CompareTo(y) : string.CompareOrdinal(a, b);
    });
}
