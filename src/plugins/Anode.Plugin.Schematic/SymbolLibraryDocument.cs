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

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Editor = _editor };
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
                Execute = () => History.Undo(),
            },
            new("edit.redo", "sch.command.redo")
            {
                ScopeKey = "scope.symlib", ShortcutText = "⌘⇧Z", Gesture = Shortcut(Key.Z, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 10,
                CanExecute = () => History.CanRedo,
                Execute = () => History.Redo(),
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

    /// <summary>
    /// An undo or redo may take away the symbol on the canvas — a new one undone — or change what it is; either
    /// way the canvas is drawn again, from the first symbol when the one shown is gone or none was shown.
    /// </summary>
    private void OnHistoryChanged()
    {
        if (Current is null or { IsAttached: false })
        {
            Current = Library.Symbols.FirstOrDefault();
            Unit = BodyStyle = 1;
        }

        Rebuild();
        OnPropertiesChanged(nameof(IsDirty), nameof(Summary), nameof(Library));
    }

    /// <summary>The letter a unit is known by: A, B, … — KiCad's <c>LetterSubReference</c>.</summary>
    public static string UnitName(int unit) => SchFind.UnitLetters(unit);

    private void Rebuild()
    {
        if (_editor is { } previous)
        {
            previous.SelectionChanged -= OnSelectionChanged;
        }

        if (Current is { } symbol && Body is { } body)
        {
            _editor = new SchematicEditor(SchematicSceneBuilder.BuildSymbol(symbol, body, Unit, BodyStyle)) { IsReadOnly = true };
            _editor.SelectionChanged += OnSelectionChanged;
        }
        else
        {
            _editor = null;
        }

        if (_canvas is { } canvas)
        {
            canvas.Editor = _editor;
        }

        OnPropertiesChanged(nameof(Current), nameof(Unit), nameof(BodyStyle), nameof(Selection), nameof(Overview), nameof(StatusFields));
    }

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    private SelectionInfo DescribeSymbol(LibSymbol symbol)
    {
        string? Field(string name) => symbol.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        var body = Body ?? symbol;

        var rows = new List<InspectorRow>
        {
            new(Tr.T("sch.lib.row.reference"), Field("Reference") ?? string.Empty),
            new(Tr.T("sch.lib.row.value"), Field("Value") ?? string.Empty),
        };

        foreach (var (name, key) in new[] { ("Description", "description"), ("ki_keywords", "keywords"), ("Footprint", "footprint"), ("Datasheet", "datasheet") })
        {
            if (Field(name) is { Length: > 0 } value)
            {
                rows.Add(new InspectorRow(Tr.T("sch.lib.row." + key), value));
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
            new(Tr.T("sch.lib.row.pinNumbers"), YesNo(body.ShowPinNumbers)),
            new(Tr.T("sch.lib.row.pinNames"), YesNo(body.ShowPinNames)),
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
        switch (item)
        {
            case SchPin pin:
                return new SelectionInfo(pin.Name is { Length: > 0 } and not "~" ? pin.Name : pin.Number, Current?.Name, [], Tr.T("sch.lib.tag.pin"))
                {
                    Blocks =
                    [
                        new InspectorBlock(Tr.T("sch.lib.block.pin"),
                        [
                            new(Tr.T("sch.lib.row.number"), pin.Number),
                            new(Tr.T("sch.lib.row.name"), pin.Name),
                            new(Tr.T("sch.lib.row.type"), pin.ElectricalType),
                            new(Tr.T("sch.lib.row.shape"), pin.GraphicStyle),
                            new(Tr.T("sch.lib.row.length"), Mm(pin.Length)),
                            new(Tr.T("sch.lib.row.orientation"), Tr.T("sch.lib.orientation." + Orientation(pin.Angle))),
                            new(Tr.T("sch.lib.row.position"), Point(pin.Position)),
                            new(Tr.T("sch.lib.row.hidden"), YesNo(pin.IsHidden)),
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
                            new(Tr.T("sch.lib.row.strokeWidth"), Mm(graphic.StrokeWidth)),
                            new(Tr.T("sch.lib.row.strokeType"), graphic.StrokeStyle),
                            new(Tr.T("sch.lib.row.fill"), graphic.Fill),
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
                            new(Tr.T("sch.lib.row.value"), field.Value),
                            new(Tr.T("sch.lib.row.position"), Point(field.Position)),
                            new(Tr.T("sch.lib.row.hidden"), YesNo(field.IsHidden)),
                        ]),
                    ],
                };

            default:
                return null;
        }
    }

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
