using System.Text;
using Anode.Sexpr;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Anode.Editing;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Kicad.Editing;
using Anode.Sdk;
using Anode.Render;
using Anode.Render.Fonts;

namespace Anode.Plugin.Schematic;

/// <summary>Opens <c>.kicad_sch</c> files as document tabs.</summary>
// Nothing outside the plugin names this type: the workbench only ever sees the IDocumentType it registers.
internal sealed class SchematicDocumentType(ILog log, SymbolLibraryList remembered) : IDocumentType
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
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
            }
            log.Info(Tr.English("sch.log.created", Path.GetFileName(path)));
        },
        cancellationToken);

    public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            var schematic = Anode.Kicad.Schematic.Load(path);
            var diagnostics = new List<HierarchyDiagnostic>();
            var design = Design(path, schematic, diagnostics.Add, cancellationToken);
            string full = Path.GetFullPath(path);
            var appearances = design.Where(i => string.Equals(i.File, full, StringComparison.Ordinal)).ToList();
            string? shown = appearances.FirstOrDefault()?.Path;
            // The sheet the project names may be one the design carries; KiCad keeps it in the root sheet's file.
            var frame = SheetFrameText.ForProject(path, board: false, out string? missing, name => Carried(schematic, full, name));
            EmbedRootFonts(full);
            var scene = SchematicSceneBuilder.Build(schematic, shown, FrameFor(frame, design, shown));
            if (GraphicsOptions.Renderer == RendererKind.OpenGl)
            {
                SceneTriangulator.Triangulate(scene);
            }

            log.Info(Tr.English("sch.log.loaded", Path.GetFileName(path), schematic.Symbols.Count, scene.PrimitiveCount));
            return (IDocument)new SchematicDocument(schematic, scene, path, remembered, appearances, design)
            {
                DrawingSheetMissing = missing,
                HierarchyDiagnostics = diagnostics,
                MissingFaces = OutlineText.StandIns(TextFont.FacesIn(schematic.Document.Root)
                    .Concat(frame.Template?.Items.OfType<WksText>().Select(t => t.Face).OfType<string>() ?? [])),
            };
        },
        cancellationToken);

    /// <summary>
    /// Every sheet place in the project, walked down from its root sheet — the only way to learn a sheet's paths,
    /// since a file does not know who places it. A sheet with no project around it is its own root. One the root
    /// never reaches has no place at all, and its own fields are all there is to show.
    /// </summary>
    /// <summary>A file this sheet carries, or — a sheet below the root carrying none — one the root carries.</summary>
    private static byte[]? Carried(Anode.Kicad.Schematic sheet, string full, string name)
    {
        if (EmbeddedFile.In(sheet.Document.Root).FirstOrDefault(f => f.Name == name)?.Data is { } here)
        {
            return here;
        }

        if (ProjectRoot(full) is not { } root || string.Equals(root, full, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return EmbeddedFile.In(Anode.Kicad.Schematic.Load(root).Document.Root).FirstOrDefault(f => f.Name == name)?.Data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException or SexprParseException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// A sheet below the root draws with the fonts the root carries — KiCad keeps a design's embedded fonts in its
    /// root file and has them loaded whichever sheet is shown. The root is read only when it carries a font.
    /// </summary>
    private static void EmbedRootFonts(string sheet)
    {
        if (ProjectRoot(sheet) is not { } root || string.Equals(root, sheet, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string text = File.ReadAllText(root);
            if (text.Contains("(type font)", StringComparison.Ordinal))
            {
                OutlineText.Embed(EmbeddedFile.In(Anode.Kicad.Schematic.Parse(text).Document.Root));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException or SexprParseException or DecoderFallbackException)
        {
            // The root's fonts are a nicety for this sheet; a root that will not read is reported where it is opened.
        }
    }

    private static IReadOnlyList<SheetInstance> Design(string path, Anode.Kicad.Schematic schematic,
        Action<HierarchyDiagnostic> report, CancellationToken cancellationToken)
    {
        string full = Path.GetFullPath(path);
        if (ProjectRoot(full) is { } root)
        {
            return SchHierarchy.Walk(root, report: report, cancellationToken: cancellationToken);
        }

        return schematic.Uuid is { Length: > 0 } uuid
            ? [new SheetInstance(full, "/" + uuid, Path.GetFileNameWithoutExtension(full), 0)]
            : [];
    }

    /// <summary>
    /// What the frame prints besides the title block: the file, the place in the design as KiCad writes it, and the
    /// page — this place's position in the walk from the root, of all the places there are.
    /// </summary>
    internal static SheetFrameText FrameFor(SheetFrameText project, IReadOnlyList<SheetInstance> design, string? instance)
    {
        int index = -1;
        for (int i = 0; i < design.Count; i++)
        {
            if (design[i].Path == instance)
            {
                index = i;
                break;
            }
        }

        return project with
        {
            SheetPath = index >= 0 ? design[index].Trail : "/",
            SheetName = index > 0 ? design[index].Name : string.Empty,
            Page = index >= 0 ? index + 1 : 1,
            PageCount = Math.Max(1, design.Count),
        };
    }

    /// <summary>The root sheet of the project around <paramref name="path"/>: named after its .kicad_pro, as KiCad names it.</summary>
    internal static string? ProjectRoot(string path) =>
        ProjectLibraries.ProjectFolder(path) is { } folder
        && Directory.EnumerateFiles(folder, "*.kicad_pro").Order(StringComparer.Ordinal).FirstOrDefault() is { } project
        && Path.ChangeExtension(project, ".kicad_sch") is var root
        && File.Exists(root)
            ? root
            : null;
}

/// <summary>One open sheet: the canvas and what the workbench shows around it.</summary>
public sealed class SchematicDocument : DocumentBase
{
    private readonly List<IDisposable> _registrations = [];
    private readonly IReadOnlyList<SheetCheck> _checks;
    private readonly SchematicEditor _editor;
    private readonly SymbolLibraryList _remembered;
    private readonly IReadOnlyList<SheetInstance> _appearances;

    /// <summary>Every sheet place in the project, root first; the frame numbers its pages from it.</summary>
    private readonly IReadOnlyList<SheetInstance> _design;

    /// <summary>The project's other sheets, read for the design-wide checks and kept while their files are unchanged.</summary>
    private readonly Dictionary<string, (DateTime Written, Anode.Kicad.Schematic? Sheet)> _neighbours = new(StringComparer.Ordinal);
    private IReadOnlyList<(string Reference, IReadOnlyList<DesignatorUse> Uses)>? _duplicates;

    /// <summary>An item on the lit net. The net is found again from it after every edit, since edits reshape nets.</summary>
    private SchItem? _netAnchor;
    private SelectionInfo? _overview;

    /// <summary>The scene owners of the lit net, as handed to the canvas; null when no net is lit.</summary>
    internal IReadOnlySet<int>? LitNet { get; private set; }

    /// <summary>The editor under the tab, for the plugin's own tests.</summary>
    internal SchematicEditor Editor => _editor;
    private SymbolIndex? _libraries;
    private SchematicCanvas? _canvas;
    private IPluginContext? _context;
    private IReadOnlyList<SchNet>? _nets;
    private IReadOnlyList<DesignNet>? _designNets;
    private (string LibId, LibSymbol Definition)? _part;
    private string _labelTool = "sch.tool.label";
    private string _shapeTool = "sch.tool.line";
    private string? _tool;
    private string _cursor = string.Empty;
    private string _frame = string.Empty;
    private double _zoom;

    internal SchematicDocument(
        Anode.Kicad.Schematic schematic,
        SchematicScene scene,
        string path,
        SymbolLibraryList remembered,
        IReadOnlyList<SheetInstance>? appearances = null,
        IReadOnlyList<SheetInstance>? design = null)
    {
        Sheet = schematic;
        Scene = scene;
        FilePath = path;
        _remembered = remembered;
        _appearances = appearances ?? [];
        _design = design ?? _appearances;
        _editor = new SchematicEditor(scene) { TriangulateChanges = GraphicsOptions.Renderer == RendererKind.OpenGl };
        _editor.SelectionChanged += OnSelectionChanged;
        _editor.History.Changed += OnHistoryChanged;
        _editor.History.Changed += ForgetDerived;
        _editor.History.Changed += RefreshNetHighlight;
        _editor.History.Changed += RedrawFrame;
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
            string summary = Tr.T("sch.summary.sheet", zoom);

            // A sheet placed more than once says which of its places is on show.
            return _appearances.Count > 1 && _appearances.FirstOrDefault(a => a.Path == Instance) is { } shown
                ? shown.Name + " · " + summary
                : summary;
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
                fields.Add(new StatusField(Tr.T("sch.status.selected", SchItemProperties.Header(single, Instance).Title)));
            }
            else if (_editor.Selection.Count > 1)
            {
                fields.Add(new StatusField(Tr.T("sch.status.selected", Tr.T("sch.selection.multi", _editor.Selection.Count))));
            }

            if (HighlightedNet is { } lit)
            {
                fields.Add(new StatusField(Tr.T("sch.status.net", lit.Name)));
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

            var (title, subtitle, tag) = SchItemProperties.Header(item, Instance);

            // Where it lives is the document's to say, not the item's: the item has never heard of a file.
            subtitle ??= Path.GetFileName(FilePath);
            return new SelectionInfo(title, subtitle, [.. SchItemProperties.For(item)], tag)
            {
                Blocks =
                [
                    .. SchItemProperties.Blocks(item, (name, mutate) => _editor.Modify(name, [item], mutate), NetOf, Instance),
                    .. item is SchSheet sheet && PinMatch(sheet) is { } match ? [PinBlock(sheet, match)] : Array.Empty<InspectorBlock>(),
                ],
                Actions = [.. item is SchSheet placed && PinMatch(placed) is { } found ? SyncActions(placed, found) : [], .. Actions(item)],
            };
        }
    }

    public override IReadOnlyList<Issue> Issues =>
        [.. _checks.Select(c => c.ToIssue()), .. HierarchyIssues(), .. DrawingSheetIssue(), .. FaceIssues(), .. Duplicates(), .. Electrical(), .. SheetPins(), .. LoosePins()];

    /// <summary>
    /// The electrical rules, over the whole design: pins that may not be wired together, and nets nothing drives.
    /// Only what touches the sheet on screen is listed, since every sheet of a design would otherwise repeat the
    /// same list; a net is checked once, where its first named pin stands.
    /// </summary>
    private IEnumerable<Issue> Electrical()
    {
        if (FilePath is not { } path)
        {
            yield break;
        }

        string own = Path.GetFullPath(path);

        // The project decides how strictly its own rules are applied, and which of them it wants to hear about.
        var rules = ErcRules.For(path);
        foreach (var finding in SchErc.Check(DesignNets, rules))
        {
            var here = finding.Pins.FirstOrDefault(p => string.Equals(p.Place.File, own, StringComparison.Ordinal));
            if (here is null)
            {
                continue;
            }

            string pins = string.Join(", ", finding.Pins.Select(p => $"{p} ({Tr.T("sch.pinType." + p.Pin.Pin.ElectricalType)})"));
            yield return new Issue(
                finding.Severity == ErcSeverity.Error ? IssueSeverity.Error : IssueSeverity.Warning,
                Tr.T("sch.issue.erc." + finding.Kind),
                Tr.T("sch.issue.erc.detail", finding.Net.Name, pins),
                Tr.T("sch.issue.erc.location", here.Place.Trail),
                Action: () => Reveal(here));
        }
    }

    /// <summary>
    /// What does not match between a sheet symbol and the sheet it stands for. A pin with no label inside is
    /// reported on the sheet the symbol is drawn on; a label with no pin, on the sheet that carries the label.
    /// </summary>
    private IEnumerable<Issue> SheetPins()
    {
        if (FilePath is not { } path)
        {
            yield break;
        }

        string own = Path.GetFullPath(path);
        foreach (var finding in SchErc.CheckSheets(_design, OpenSheet, ErcRules.For(FilePath)))
        {
            var where = finding.Kind == ErcKind.SheetPinWithoutLabel
                ? _design.FirstOrDefault(p => string.Equals(p.Path, finding.Place.Parent, StringComparison.Ordinal))
                : finding.Place;

            if (where is null || !string.Equals(where.File, own, StringComparison.Ordinal))
            {
                continue;
            }

            // The pin belongs to the sheet symbol, which is what can be selected; the label is its own item.
            var item = finding.Kind == ErcKind.SheetPinWithoutLabel ? (SchItem?)finding.Place.Placement : finding.Item;
            yield return new Issue(
                IssueSeverity.Error,
                Tr.T("sch.issue.erc." + finding.Kind),
                Tr.T("sch.issue.erc.sheetDetail", finding.Name, finding.Place.Name),
                Tr.T("sch.issue.erc.location", where.Trail),
                Action: item is null ? null : () =>
                {
                    ShowInstance(where.Path);
                    Focus(item);
                });
        }
    }

    /// <summary>Turns the tab to the place a pin stands in and shows the part it belongs to.</summary>
    private void Reveal(DesignPin pin)
    {
        ShowInstance(pin.Place.Path);
        Focus(pin.Pin.Symbol);
    }

    /// <summary>
    /// Selects an item and brings it into view. Selecting alone was not enough to find anything: on a sheet the
    /// size of a real one, what is selected is as likely as not to be off the screen, and a reader following a
    /// check had to hunt for what they had just been shown.
    /// </summary>
    private void Focus(SchItem item)
    {
        _editor.SetSelection([item]);
        _canvas?.ShowArea(_editor.Scene.BoundsOf(item));
    }

    internal IReadOnlyList<HierarchyDiagnostic> HierarchyDiagnostics { get; init; } = [];

    private IEnumerable<Issue> HierarchyIssues() => HierarchyDiagnostics.Select(d => new Issue(
        IssueSeverity.Warning,
        Tr.T("sch.hierarchy." + d.Problem),
        d.Detail ?? d.File,
        d.File));

    /// <summary>Faces the sheet and its drawing sheet name that this machine lacks, each with what stands in.</summary>
    internal IReadOnlyList<(string Face, string StandIn)> MissingFaces { get; init; } = [];

    private IEnumerable<Issue> FaceIssues() => MissingFaces.Select(m => new Issue(
        IssueSeverity.Warning,
        Tr.T("sch.issue.face.title", m.Face),
        Tr.T("sch.issue.face.detail", m.Face, m.StandIn),
        m.StandIn));

    /// <summary>The drawing sheet the project names, when it is missing or will not read; the default is drawn instead.</summary>
    internal string? DrawingSheetMissing { get; init; }

    private IEnumerable<Issue> DrawingSheetIssue()
    {
        if (DrawingSheetMissing is { } path)
        {
            yield return new Issue(
                IssueSeverity.Warning,
                Tr.T("sch.issue.drawingSheet.title"),
                Tr.T("sch.issue.drawingSheet.detail", Path.GetFileName(path)),
                path);
        }
    }

    /// <summary>
    /// Designators used twice anywhere in the design, reported on the sheets that carry one of them. The whole
    /// hierarchy is read because a designator belongs to a place, and a clash can span two sheets; this sheet is
    /// read as it stands in the editor, the others as they are on disk.
    /// </summary>
    private IEnumerable<Issue> Duplicates()
    {
        if (FilePath is not { } path)
        {
            yield break;
        }

        string own = Path.GetFullPath(path);
        _duplicates ??= FindDuplicates(own);

        foreach (var (reference, uses) in _duplicates)
        {
            if (uses.FirstOrDefault(u => string.Equals(u.Place.File, own, StringComparison.Ordinal)) is not { } here)
            {
                continue;
            }

            string places = string.Join(", ", uses.Select(u => u.Place.Name).Distinct(StringComparer.Ordinal));
            yield return new Issue(
                IssueSeverity.Error,
                Tr.T("sch.issue.duplicate.title"),
                Tr.T("sch.issue.duplicate.detail", reference, uses.Count, places),
                Tr.T("sch.issue.duplicate.location",
                    Units.NmToMm(here.Symbol.Position.X).ToString("0.##", CultureInfo.InvariantCulture),
                    Units.NmToMm(here.Symbol.Position.Y).ToString("0.##", CultureInfo.InvariantCulture),
                    Tr.T("sch.units.mm")),
                Action: () => Reveal(here));
        }
    }

    private IReadOnlyList<(string Reference, IReadOnlyList<DesignatorUse> Uses)> FindDuplicates(string own)
    {
        var places = SchematicDocumentType.ProjectRoot(own) is { } root ? SchHierarchy.Walk(root, OpenSheet) : _appearances;
        return SchDuplicates.Find(places, OpenSheet);
    }

    /// <summary>
    /// A sheet of the design: this one as it stands in the editor, any other as it is on disk, read once and again
    /// only when its file changes; null when it cannot be read.
    /// </summary>
    private Anode.Kicad.Schematic? OpenSheet(string file)
    {
        if (FilePath is { } own && string.Equals(file, Path.GetFullPath(own), StringComparison.Ordinal))
        {
            return Sheet;
        }

        DateTime written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : default;
        if (!_neighbours.TryGetValue(file, out var known) || known.Written != written)
        {
            Anode.Kicad.Schematic? sheet;
            try
            {
                sheet = written == default ? null : Anode.Kicad.Schematic.Load(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException or SexprParseException or DecoderFallbackException)
            {
                sheet = null;
            }

            known = (written, sheet);
            _neighbours[file] = known;
        }

        return known.Sheet;
    }

    /// <summary>Turns the tab to the place a part stands in and selects it there.</summary>
    private void Reveal(DesignatorUse use)
    {
        ShowInstance(use.Place.Path);
        Focus(use.Symbol);
    }

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
        new("sch.tool.cut", "sch.tool.cut", Icons.Slice) { Activate = () => UseTool("sch.tool.cut") },
        new("sch.tool.textBox", "sch.tool.textBox", Icons.Text) { Activate = () => UseTool("sch.tool.textBox") },
        new("sch.tool.sheet", "sch.tool.sheet", Icons.Sheet) { ShortcutText = "S", Activate = () => UseTool("sch.tool.sheet") },
        new("sch.tool.sheetPin", "sch.tool.sheetPin", Icons.SheetPin) { Activate = () => UseTool("sch.tool.sheetPin") },
        new(_shapeTool, _shapeTool, ShapeIcon(_shapeTool))
        {
            Activate = () => UseTool(_shapeTool),
            Variants =
            [
                new("sch.tool.line", "sch.tool.line", Icons.Line) { Activate = () => UseTool("sch.tool.line") },
                new("sch.tool.rectangle", "sch.tool.rectangle", Icons.Rectangle) { Activate = () => UseTool("sch.tool.rectangle") },
                new("sch.tool.circle", "sch.tool.circle", Icons.Circle) { Activate = () => UseTool("sch.tool.circle") },
                new("sch.tool.arc", "sch.tool.arc", Icons.Circle) { Activate = () => UseTool("sch.tool.arc") },
                new("sch.tool.bezier", "sch.tool.bezier", Icons.Line) { Activate = () => UseTool("sch.tool.bezier") },
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
            [new InspectorAction(Tr.T("sch.action.openSheet"), () => OpenSheet(sheet, file))],
        SymbolInstance symbol when symbol.LibId is { Length: > 0 } libId =>
        [
            new InspectorAction(Tr.T("sch.action.updateFromLibrary"), () => UpdateFromLibrary(libId)),

            // Swapping needs something to swap to, and that is whatever the components panel has chosen.
            .. _part is { } chosen && !string.Equals(chosen.LibId, libId, StringComparison.Ordinal)
                ? new[]
                {
                    new InspectorAction(
                        Tr.T("sch.action.changeSymbol", chosen.LibId),
                        () => ChangeTo(symbol, chosen.LibId, chosen.Definition))
                    {
                        IsPrimary = true,
                    },
                }
                : [],
        ],
        _ when NetOf(item) is not null =>
        [
            SameNet(item)
                ? new InspectorAction(Tr.T("sch.action.unhighlightNet"), () => HighlightNet(null))
                : new InspectorAction(Tr.T("sch.action.highlightNet"), () => HighlightNet(item)),
        ],
        _ => [],
    };

    /// <summary>
    /// Lights the net of what is selected, or puts it out when that net is already lit — backquote, as in KiCad.
    /// </summary>
    private void ToggleNetHighlight()
    {
        var anchor = _editor.Selection.FirstOrDefault(i => NetOf(i) is not null);
        HighlightNet(anchor is not null && !SameNet(anchor) ? anchor : null);
    }

    /// <summary>
    /// Lights a net chosen away from the canvas — in the nets panel — or puts the light out. A wire is preferred as
    /// the anchor: a net is lit by an item of it, and a part belongs to as many nets as it has pins.
    /// </summary>
    internal void LightNet(SchNet? net) =>
        HighlightNet(net?.Items.FirstOrDefault(i => i is SchWire) ?? net?.Items.FirstOrDefault(i => i is not SymbolInstance) ?? net?.Items.FirstOrDefault());

    /// <summary>
    /// Every place on the sheet on screen that says what is looked for, read with the designators of the appearance
    /// on screen. <paramref name="within"/> narrows it to what was selected when the search was narrowed.
    /// </summary>
    internal IReadOnlyList<SchFindHit> Find(SchFindOptions options, IReadOnlyCollection<SchItem>? within = null) =>
        SchFind.All(Sheet, options, Instance, within);

    /// <summary>The parts of the sheet on screen the fields table shows, read with the designators of this appearance.</summary>
    internal IReadOnlyList<SymbolInstance> FieldParts(bool includeExcluded) => SchFieldsTable.Parts(Sheet, Instance, includeExcluded);

    /// <summary>
    /// Writes one value into a field of every part of a line of the fields table, as one step to undo. Nothing
    /// happens when every part already says it.
    /// </summary>
    internal void WriteField(SchFieldsRow row, string field, string value)
    {
        if (row.Value(field) == value || row.Symbols.Any(s => !s.IsAttached))
        {
            return;
        }

        _editor.Modify(Tr.T("sch.fields.edit", field), [.. row.Symbols], () => SchFieldsTable.Write(row, field, value));
    }

    /// <summary>Selects the parts of a line and brings them into view.</summary>
    internal void ShowParts(SchFieldsRow row)
    {
        var parts = row.Symbols.Where(s => s.IsAttached).Cast<SchItem>().ToList();
        if (parts.Count == 0)
        {
            return;
        }

        _editor.SetSelection(parts);
        var area = parts.Aggregate(RectD.Empty, (all, part) => all.Union(_editor.Scene.BoundsOf(part)));
        _canvas?.ShowArea(area);
    }

    /// <summary>What is selected now, for a search to be narrowed to.</summary>
    internal IReadOnlyList<SchItem> SelectedItems => [.. _editor.Selection];

    /// <summary>Selects what a find landed on and brings it into view, zooming in only when it would be hard to see.</summary>
    internal void Show(SchFindHit hit) => Focus(hit.Item);

    /// <summary>Replaces what was found in one place, as one step to undo. False where nothing could be written.</summary>
    internal bool Replace(SchFindHit hit, SchFindOptions options, string with)
    {
        if (!SchFind.CanReplace(hit, options, with, Instance))
        {
            return false;
        }

        _editor.Modify(Tr.T("sch.command.replace"), [hit.Item], () => SchFind.Replace(hit, options, with, Instance));
        return true;
    }

    /// <summary>
    /// Replaces every place on the sheet on screen at once, as one step to undo. Answers how many places changed.
    /// Other sheets are left alone: they are not open, and writing them would edit files behind the designer's back.
    /// </summary>
    internal int ReplaceAll(IReadOnlyList<SchFindHit> hits, SchFindOptions options, string with)
    {
        var writable = hits.Where(h => SchFind.CanReplace(h, options, with, Instance)).ToList();
        if (writable.Count == 0)
        {
            return 0;
        }

        _editor.Modify(Tr.T("sch.command.replaceAll"), [.. writable.Select(h => h.Item).Distinct()], () =>
        {
            foreach (var hit in writable)
            {
                SchFind.Replace(hit, options, with, Instance);
            }
        });
        return writable.Count;
    }

    internal void HighlightNet(SchItem? anchor)
    {
        _netAnchor = anchor;
        RefreshNetHighlight();
        OnPropertiesChanged(nameof(StatusFields), nameof(Selection));
    }

    private bool SameNet(SchItem item) => HighlightedNet is { } net && net.Items.Contains(item);

    /// <summary>The net the canvas is lighting, if any.</summary>
    internal SchNet? HighlightedNet =>
        _netAnchor is { IsAttached: true } anchor ? Nets.FirstOrDefault(n => n.Items.Contains(anchor)) : null;

    /// <summary>
    /// Hands the canvas the owners of the lit net: its wires, labels and marks, and the junction dots on its wires,
    /// which the net itself does not list but without which it would read as broken. Symbols stay dim — a pin is
    /// drawn as part of its symbol, and lighting every part a ground net touches would light the whole sheet.
    /// </summary>
    private void RefreshNetHighlight()
    {
        if (HighlightedNet is not { } net)
        {
            _netAnchor = null;
            LitNet = null;
            if (_canvas is { } idle)
            {
                idle.HighlightedOwners = null;
            }

            return;
        }

        var wires = net.Items.OfType<SchWire>().ToList();
        var owners = new HashSet<int>();
        foreach (var item in net.Items.Where(i => i is not SymbolInstance)
                     .Concat(Sheet.Junctions.Where(j => wires.Any(w => OnWire(j.Position, w)))))
        {
            owners.UnionWith(Scene.OwnersOf(item));
        }

        LitNet = owners;
        if (_canvas is { } canvas)
        {
            canvas.HighlightedOwners = owners;
        }
    }

    private static bool OnWire(Vector2L point, SchWire wire)
    {
        var points = wire.Points;
        for (int i = 1; i < points.Length; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            long cross = ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));
            if (cross == 0
                && point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X)
                && point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Swaps the selected placement for the part chosen in the components panel. Two changes in one step — the new
    /// definition copied into the sheet and the placement rewritten — so that one undo takes back both.
    /// </summary>
    private void ChangeTo(SymbolInstance symbol, string libId, LibSymbol definition)
    {
        string label = Tr.T("sch.action.changeSymbol", libId);
        try
        {
            _editor.Run(new CompositeCommand(
                label,
                [
                    new AddLibrarySymbolCommand(Sheet, libId, definition),
                    new ModifyNodesCommand(label, [symbol], () => SchSymbols.Change(Sheet, symbol, libId, definition)),
                ]));
        }
        catch (Exception ex) when (ex is KiCadFormatException or InvalidOperationException or NotSupportedException)
        {
            _context?.Log.Error(ex.Message, ex);
            _context?.Workbench.ShowBanner(new Banner(ex.Message, IsAlert: true));
        }
    }

    /// <summary>
    /// Takes the sheet's copy of a definition from the library again — what a designer does once the part has been
    /// fixed there. Every placement drawing from it is named in the change, so they all redraw, and one undo takes
    /// the whole thing back.
    /// </summary>
    private void UpdateFromLibrary(string libId)
    {
        if (Sheet.LibrarySymbols.GetValueOrDefault(libId) is not { } current)
        {
            return;
        }

        if (Libraries.Find(libId) is not { } fresh)
        {
            _context?.Log.Error(Tr.English("sch.log.updateMissing", libId));
            return;
        }

        _editor.Run(new ModifyNodesCommand(
            Tr.T("sch.action.updateFromLibrary"),
            SchSymbols.Affected(Sheet, libId),
            () => SchSymbols.Update(Sheet, libId, fresh)));
    }

    /// <summary>
    /// The libraries this sheet can reach, read once and kept: the project's table, what KiCad installed, and what
    /// Anode was told to remember.
    /// </summary>
    private SymbolIndex Libraries => _libraries ??= ProjectLibraries.For(FilePath, _remembered.Load());

    /// <summary>Opens a child sheet as its own tab, beside this one.</summary>
    private void OpenSheet(SchSheet sheet, string file)
    {
        if (_context is not { } context || FilePath is not { } path)
        {
            return;
        }

        // At the appearance under this one, so the child shows the designators it has in this branch of the design.
        string target = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, file);
        string? below = Instance is { } here && sheet.Uuid is { Length: > 0 } id ? here + "/" + id : null;
        _ = context.Workbench.OpenAsync(target, below);
    }

    /// <summary>The path of the appearance on show; null for a sheet its project never reaches.</summary>
    public override string? Instance => Scene.SheetPath;

    /// <summary>
    /// Shows another place this sheet appears. Nothing in the file changes — only which designators and sections are
    /// read — so the parts are drawn again rather than edited, and the undo history is left alone.
    /// </summary>
    public override void ShowInstance(string instance)
    {
        if (string.Equals(instance, Scene.SheetPath, StringComparison.Ordinal))
        {
            return;
        }

        Scene.SheetPath = instance;
        Scene.Frame = SchematicDocumentType.FrameFor(Scene.Frame, _design, instance);
        SchematicSceneBuilder.RedrawFrame(Scene);
        _overview = null;
        _editor.Redraw([.. Sheet.Symbols]);
        _canvas?.Redraw();
        OnPropertiesChanged(nameof(Instance), nameof(Selection), nameof(StatusFields), nameof(Summary));
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
    private DesignNumbers? _numbers;

    private static readonly Vector2L BusStep = new(2_540_000, 2_540_000);

    /// <summary>How far outside a sheet's border a click still means that sheet: one grid step, as KiCad allows.</summary>
    private const long PinReach = 2_540_000;

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
            var symbol = SchSymbols.Place(Sheet, libId, definition, at, Designator(definition), ProjectName(), Instance ?? SchSymbols.PathOf(Sheet));
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
    /// What the part is called as it lands: the library's own prefix and the first number the design does not use.
    /// KiCad writes "R?" and numbers later; a part that arrives already named saves that second pass, and the rule
    /// is the same one either way — never take a number anything in the design already carries.
    /// </summary>
    private string Designator(LibSymbol definition)
    {
        string prefix = SchAnnotation.PrefixOf(definition.Reference);
        return prefix + Numbers().TakeFirstFree(prefix).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The numbers the design has already used. Reading a whole design is too much to do on every click, so it is
    /// read once and then kept: a number handed out is remembered in it, which is what stops a row of parts laid
    /// down one after another from all taking the same one. It is read again after a save, when what is on disk —
    /// and what the other sheets of the design say — may have moved on.
    /// </summary>
    private DesignNumbers Numbers()
    {
        if (_numbers is not null)
        {
            return _numbers;
        }

        try
        {
            return _numbers = FilePath is null
                ? DesignNumbers.Of(Sheet, Instance)
                : DesignNumbers.Of(RootFile(), OpenSheet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
            or Anode.Sexpr.SexprParseException or InvalidOperationException)
        {
            // A design that will not walk is still a sheet to place parts on; its own numbers are better than none.
            _context?.Log.Warn(ex.Message);
            return _numbers = DesignNumbers.Of(Sheet, Instance);
        }
    }

    /// <summary>
    /// Numbers everything on the sheet that is still waiting, as one step. For sheets that arrived from elsewhere
    /// with their parts unnumbered; what this application places is numbered as it lands.
    /// </summary>
    private void Annotate()
    {
        // The whole design is read again here: annotating is the one moment where being right matters more than
        // being quick, and a number another sheet took since must not be handed out twice.
        _numbers = null;
        var given = SchAnnotation.Annotate(Sheet, Sheet.Symbols, Instance, Numbers());
        if (given.Count == 0)
        {
            return;
        }

        _editor.Modify(Tr.T("sch.command.annotate"), [.. given.Select(g => g.Symbol)], () =>
        {
            foreach (var (symbol, reference) in given)
            {
                SchWrites.SetReference(symbol, reference, Instance);
            }
        });
    }

    /// <summary>
    /// Numbers this sheet's parts again from scratch, left to right — for a sheet numbered in the order it happened
    /// to be drawn. Unlike annotation it renames parts that already carry a number, so it is one undo away from
    /// being taken back, and it is this sheet's alone: the numbers of the rest of the design are still respected,
    /// and nothing on another sheet is touched.
    /// </summary>
    private void Renumber()
    {
        _numbers = null;
        var given = SchAnnotation.Renumber(Sheet.Symbols, Instance, Numbers());
        var changed = given.Where(g => !string.Equals(g.Symbol.ReferenceAt(Instance), g.Reference, StringComparison.Ordinal)).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        _editor.Modify(Tr.T("sch.command.renumber"), [.. changed.Select(g => g.Symbol)], () =>
        {
            foreach (var (symbol, reference) in changed)
            {
                SchWrites.SetReference(symbol, reference, Instance);
            }
        });

        _context?.Log.Info(Tr.English("sch.log.renumbered", changed.Count));
    }

    /// <summary>
    /// Puts a note in a box of its own: the box is drawn first, then the words are asked for. A box with nothing
    /// written in it is still a box — KiCad keeps one — so an empty answer leaves it there rather than undoing it.
    /// </summary>
    private async Task PlaceTextBoxAsync(Vector2L at, Vector2L size)
    {
        if (_canvas is not { } canvas)
        {
            return;
        }

        string words = await canvas.AskForNameAsync(at, string.Empty) ?? string.Empty;
        _editor.Apply(Tr.T("sch.tool.textBox"), [SchNodes.TextBox(words, at, size)], []);
    }

    /// <summary>
    /// Cuts a wire in two where it was clicked, and puts a dot on the cut. The halves touch, so they are one net
    /// either way; the dot is what says so to the eye, and KiCad's own Break leaves one there too.
    /// </summary>
    private void CutWire(SchWire wire, Vector2L at)
    {
        if (SchWires.Cut(wire, at) is not var (first, second))
        {
            return;
        }

        _editor.Apply(Tr.T("sch.command.cutWire"), [first, second, SchNodes.Junction(at)], [wire]);
    }

    /// <summary>
    /// Shows or hides what the sheet keeps out of sight — a field nobody wanted shown, a part's power pins. They
    /// are drawn already, on layers of their own that are off, so this is a switch and not a redrawing.
    /// </summary>
    private void Reveal(string layer)
    {
        if (_editor.Scene.Find(layer) is not { } hidden)
        {
            return;
        }

        hidden.IsVisible = !hidden.IsVisible;
        _canvas?.Redraw();
        OnPropertiesChanged(nameof(StatusFields));
    }

    /// <summary>
    /// Holds the selection where it is, or lets it go again. A locked item stays put through moving, turning,
    /// tidying and deleting — the lock is answered where things are changed, not where the buttons are drawn.
    /// </summary>
    private void Lock(bool locked)
    {
        var change = _editor.Selection.Where(item => item.IsLocked != locked).ToList();
        if (change.Count == 0)
        {
            return;
        }

        _editor.Modify(Tr.T(locked ? "sch.command.lock" : "sch.command.unlock"), change, () =>
        {
            foreach (var item in change)
            {
                SchWrites.SetFlag(item, "locked", locked);
            }
        });
    }

    /// <summary>What in the selection has words on it that could be written as another kind of thing.</summary>
    private IReadOnlyList<SchItem> Relabelable() =>
        [.. _editor.Selection.Where(item => item switch
        {
            SchLabel label => label.Text.Length > 0,
            SchText text => text.Text.Length > 0,
            _ => false,
        })];

    /// <summary>
    /// Writes the selected labels as another kind — a local name made global, a hierarchical one made into a note.
    /// The old item goes and a new one takes its place, which is one step to undo; what is already of that kind is
    /// left alone rather than rewritten for nothing.
    /// </summary>
    private void Relabel(SchLabelKind? kind)
    {
        var change = Relabelable()
            .Where(item => !(kind is { } wanted ? item is SchLabel label && label.Kind == wanted : item is SchText))
            .ToList();

        if (change.Count == 0)
        {
            return;
        }

        var fresh = change.Select(item => SchNodes.Relabel(item, kind)).ToList();
        _editor.Apply(Tr.T(kind is null ? "sch.command.toText" : "sch.command.toLabel"), fresh, change);
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

    /// <summary>
    /// Puts a child sheet down: the rectangle that stands for it here, and the schematic it reads, written beside
    /// this one and named after it. A sheet whose file is already there reads that file rather than overwriting it,
    /// which is how an existing sheet is brought into a second design — KiCad does the same.
    ///
    /// The sheet is placed where it stands in the hierarchy, with the next free page number, because a sheet that
    /// names no path has no page and is not annotated with the rest of the design.
    /// </summary>
    private async Task PlaceSheetAsync(Vector2L at, Vector2L size)
    {
        if (_canvas is not { } canvas || FilePath is not { } path)
        {
            Warn(Tr.T("sch.sheet.needsFile"));
            return;
        }

        if (await canvas.AskForNameAsync(at, string.Empty) is not { Length: > 0 } name)
        {
            return;
        }

        string file = name + ".kicad_sch";
        if (name.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Warn(Tr.T("sch.sheet.badName", name));
            return;
        }

        try
        {
            string target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty, file);
            if (!File.Exists(target))
            {
                SchSheets.NewSheet(Sheet).Save(target);
            }

            var sheet = SchNodes.Sheet(name, file, at, size, [(ProjectName(), Instance ?? SchSymbols.PathOf(Sheet), NextPage())]);
            _editor.Apply(Tr.T("sch.tool.sheet"), [sheet], []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or KiCadFormatException or InvalidOperationException)
        {
            _context?.Log.Error(ex.Message, ex);
            Warn(ex.Message);
        }
    }

    /// <summary>
    /// Puts a pin on a child sheet's edge. Its shape is the one the hierarchical label of that name carries inside
    /// the sheet, so a pin made for a label that is already there matches it and the two answer each other.
    /// </summary>
    private async Task PlaceSheetPinAsync(SchSheet sheet, Vector2L at, SheetSide side)
    {
        if (_canvas is not { } canvas || await canvas.AskForNameAsync(at, string.Empty) is not { Length: > 0 } name)
        {
            return;
        }

        if (sheet.Pins.Any(p => string.Equals(KicadText.Unescape(p.Name), name, StringComparison.Ordinal)))
        {
            Warn(Tr.T("sch.sheet.pinTaken", name));
            return;
        }

        _editor.Modify(Tr.T("sch.tool.sheetPin"), [sheet], () => SchSheets.AddPin(sheet, name, ShapeInside(sheet, name), at, side));
    }

    /// <summary>What the label of that name inside the sheet is, so the pin arrives already agreeing with it.</summary>
    private string ShapeInside(SchSheet sheet, string name)
    {
        if (ChildFile(sheet) is { } file && OpenSheet(file) is { } child)
        {
            foreach (var label in child.Labels)
            {
                if (label.Kind == SchLabelKind.Hierarchical && string.Equals(label.Shown, name, StringComparison.Ordinal))
                {
                    return label.Shape;
                }
            }
        }

        return "input";
    }

    /// <summary>
    /// How a child sheet's pins stand against the hierarchical labels inside it, read from the file it names as that
    /// file is on disk; null when there is no file to read.
    /// </summary>
    internal SheetPinMatch? PinMatch(SchSheet sheet) =>
        ChildFile(sheet) is { } file && OpenSheet(file) is { } child ? SchSheetSync.Compare(sheet, child) : null;

    /// <summary>
    /// The pins of a selected sheet against the labels inside it: what has no partner, and what disagrees on shape.
    /// A pin that names nothing can be pointed at a label that has no pin, which renames it — the usual cause is a
    /// label renamed inside and the pin left behind.
    /// </summary>
    private InspectorBlock PinBlock(SchSheet sheet, SheetPinMatch match)
    {
        var rows = new List<InspectorRow>();
        if (match.IsInStep)
        {
            rows.Add(new InspectorRow(Tr.T("sch.sync.inStep"), match.Matched.Count.ToString(CultureInfo.InvariantCulture)));
            return new InspectorBlock(Tr.T("sch.sync.title"), rows);
        }

        foreach (var (pin, label) in match.ShapeDiffers)
        {
            rows.Add(new InspectorRow(KicadText.Unescape(pin.Name), Tr.T("sch.sync.shapes", pin.Shape, label.Shape))
            {
                Trailing = Tr.T("sch.sync.shapeDiffers"),
                IsUnresolved = true,
            });
        }

        foreach (var label in match.LabelsWithoutPin)
        {
            rows.Add(new InspectorRow(label.Shown, label.Shape) { Trailing = Tr.T("sch.sync.noPin"), IsUnresolved = true });
        }

        var free = match.LabelsWithoutPin.ToDictionary(l => l.Shown, StringComparer.Ordinal);
        foreach (var pin in match.PinsWithoutLabel)
        {
            var target = pin;
            rows.Add(new InspectorRow(KicadText.Unescape(pin.Name), Tr.T("sch.sync.noLabel"))
            {
                Trailing = pin.Shape,
                IsUnresolved = true,
                Choices = free.Count > 0 ? [.. free.Keys] : null,
                Commit = free.Count > 0
                    ? name =>
                    {
                        if (free.TryGetValue(name, out var label))
                        {
                            _editor.Modify(Tr.T("sch.sync.rename"), [sheet], () => SchSheetSync.Adopt(target, label));
                        }
                    }
                    : null,
            });
        }

        return new InspectorBlock(Tr.T("sch.sync.title"), rows);
    }

    /// <summary>
    /// The ways of bringing a sheet's pins in step, each one step to undo: pins for the labels that have none, the
    /// labels' shapes for pins that disagree, and taking off the pins that name nothing.
    /// </summary>
    private IEnumerable<InspectorAction> SyncActions(SchSheet sheet, SheetPinMatch match)
    {
        if (match.LabelsWithoutPin.Count > 0)
        {
            yield return new InspectorAction(Tr.T("sch.sync.addPins", match.LabelsWithoutPin.Count), () =>
            {
                IReadOnlyList<SchLabel> unplaced = [];
                _editor.Modify(Tr.T("sch.sync.addPinsStep"), [sheet], () => unplaced = SchSheetSync.AddPins(sheet, match.LabelsWithoutPin));
                if (unplaced.Count > 0)
                {
                    Warn(Tr.T("sch.sync.noRoom", unplaced.Count, string.Join(", ", unplaced.Select(l => l.Shown))));
                }
            })
            { IsPrimary = true };
        }

        if (match.ShapeDiffers.Count > 0)
        {
            yield return new InspectorAction(Tr.T("sch.sync.takeShapes", match.ShapeDiffers.Count), () =>
                _editor.Modify(Tr.T("sch.sync.takeShapesStep"), [sheet], () =>
                {
                    foreach (var (pin, label) in match.ShapeDiffers)
                    {
                        SchSheetSync.Adopt(pin, label);
                    }
                }));
        }

        if (match.PinsWithoutLabel.Count > 0)
        {
            yield return new InspectorAction(Tr.T("sch.sync.removePins", match.PinsWithoutLabel.Count), () =>
                _editor.Modify(Tr.T("sch.sync.removePinsStep"), [sheet], () => SchSheetSync.RemovePins(sheet, match.PinsWithoutLabel)));
        }
    }

    /// <summary>The file a child sheet reads, beside this one; null when the sheet names none or this one has no path.</summary>
    private string? ChildFile(SchSheet sheet)
    {
        if (FilePath is not { } path || sheet.SheetFile is not { Length: > 0 } relative)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The page a new sheet takes: one past the design as it stands. KiCad numbers the pages of a hierarchy in the
    /// order they are walked, and a sheet appended at the end takes the next number.
    /// </summary>
    private string NextPage()
    {
        try
        {
            return (SchHierarchy.Walk(RootFile(), OpenSheet).Count + 1).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
            or Anode.Sexpr.SexprParseException or InvalidOperationException)
        {
            return "2";
        }
    }

    private void Warn(string message) => _context?.Workbench.ShowBanner(new Banner(message, IsAlert: true));

    /// <summary>Puts a tool on the pointer, or takes it off; the buttons and the canvas follow.</summary>
    public void UseTool(string? id)
    {
        _tool = id;
        if (id is "sch.tool.label" or "sch.tool.globalLabel" or "sch.tool.hierarchicalLabel")
        {
            _labelTool = id;
        }

        if (id is "sch.tool.line" or "sch.tool.rectangle" or "sch.tool.circle" or "sch.tool.arc" or "sch.tool.bezier")
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
                "sch.tool.arc" => new PointsTool(_editor, "sch.tool.arc", 3,
                    p => SchNodes.Arc(p[0], p[2], p[1]),
                    p => p.Count >= 3 && ArcMath.FromStartMidEnd(p[0].ToDouble(), p[2].ToDouble(), p[1].ToDouble()) is { } arc
                        ? ArcMath.Tessellate(arc)
                        : [.. p.Select(q => q.ToDouble())]),
                "sch.tool.bezier" => new PointsTool(_editor, "sch.tool.bezier", 4,
                    SchNodes.Bezier,
                    p => BezierMath.Tessellate([.. p.Select(q => q.ToDouble())])),
                "sch.tool.cut" => new CutTool(_editor, point => SchWires.At(Sheet.Wires, point), CutWire),
                "sch.tool.textBox" => new SheetTool(_editor, PlaceTextBoxAsync, ex => _context?.Log.Error(ex.Message, ex)) { Id = "sch.tool.textBox" },
                "sch.tool.sheet" => new SheetTool(_editor, PlaceSheetAsync, ex => _context?.Log.Error(ex.Message, ex)),
                "sch.tool.sheetPin" => new SheetPinTool(
                    _editor,
                    point => SchSheets.At(Sheet.Sheets, point, PinReach),
                    PlaceSheetPinAsync,
                    ex => _context?.Log.Error(ex.Message, ex)),
                _ => null,
            };
        }

        // The inspector too: what the footer offers a selected part depends on which part the panel has chosen, and
        // choosing one is this. Without it the swap would not be offered until the symbol was selected a second time.
        OnPropertiesChanged(nameof(ActiveToolId), nameof(StatusFields), nameof(Selection));
    }

    public override Task<bool> SaveAsync(string? path = null)
    {
        string target = path ?? FilePath ?? throw new InvalidOperationException("The sheet has no path to save to.");

        // As KiCad does before it writes: the fonts the design's texts use are carried when it asks for that, and
        // dropped when it does not.
        EmbeddedFonts.Sync(Sheet.Root, DocumentFonts.Carried(DocumentFonts.Of(FontSheets())));

        _editor.Save(target);
        FilePath = target;
        _numbers = null;
        _overview = null;
        OnPropertiesChanged(nameof(FilePath), nameof(Title), nameof(IsDirty), nameof(Overview));
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
            new("edit.find", "sch.command.find")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘F", Gesture = Shortcut(Key.F),
                MenuKey = "menu.edit", MenuOrder = 24,
                Execute = () => OpenFind(context, replace: false),
            },
            new("edit.findReplace", "sch.command.findReplace")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⌘⌥F", Gesture = Shortcut(Key.F, KeyModifiers.Alt),
                MenuKey = "menu.edit", MenuOrder = 25,
                Execute = () => OpenFind(context, replace: true),
            },
            new("edit.findNext", "sch.command.findNext")
            {
                ScopeKey = "scope.schematic", ShortcutText = "F3", Gesture = new KeyGesture(Key.F3),
                MenuKey = "menu.edit", MenuOrder = 26,
                CanExecute = () => FindSession.Text.Length > 0,
                Execute = () => Guard(() => FindSession.Step(this, 1), context),
            },
            new("edit.findPrevious", "sch.command.findPrevious")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⇧F3", Gesture = new KeyGesture(Key.F3, KeyModifiers.Shift),
                MenuKey = "menu.edit", MenuOrder = 27,
                CanExecute = () => FindSession.Text.Length > 0,
                Execute = () => Guard(() => FindSession.Step(this, -1), context),
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
                ScopeKey = "scope.schematic", ShortcutText = "W", Gesture = new KeyGesture(Key.W), MenuKey = "menu.place", MenuOrder = 0,
                Execute = () => UseTool("sch.tool.wire"),
            },
            new("sch.tool.bus", "sch.command.bus")
            {
                ScopeKey = "scope.schematic", ShortcutText = "B", Gesture = new KeyGesture(Key.B), MenuKey = "menu.place", MenuOrder = 10,
                Execute = () => UseTool("sch.tool.bus"),
            },
            new("sch.tool.label", "sch.command.label")
            {
                ScopeKey = "scope.schematic", ShortcutText = "L", Gesture = new KeyGesture(Key.L), MenuKey = "menu.place", MenuOrder = 20,
                Execute = () => UseTool("sch.tool.label"),
            },
            new("sch.tool.globalLabel", "sch.command.globalLabel")
            {
                ScopeKey = "scope.schematic", ShortcutText = "⇧L", Gesture = new KeyGesture(Key.L, KeyModifiers.Shift), MenuKey = "menu.place", MenuOrder = 30,
                Execute = () => UseTool("sch.tool.globalLabel"),
            },
            new("sch.tool.hierarchicalLabel", "sch.command.hierarchicalLabel")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 40,
                Execute = () => UseTool("sch.tool.hierarchicalLabel"),
            },
            new("sch.tool.noConnect", "sch.command.noConnect")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Q", Gesture = new KeyGesture(Key.Q), MenuKey = "menu.place", MenuOrder = 50,
                Execute = () => UseTool("sch.tool.noConnect"),
            },
            new("sch.tool.junction", "sch.command.junction")
            {
                ScopeKey = "scope.schematic", ShortcutText = "J", Gesture = new KeyGesture(Key.J), MenuKey = "menu.place", MenuOrder = 55,
                Execute = () => UseTool("sch.tool.junction"),
            },
            new("sch.tool.busEntry", "sch.command.busEntry")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 60,
                Execute = () => UseTool("sch.tool.busEntry"),
            },
            new("sch.tool.text", "sch.command.text")
            {
                ScopeKey = "scope.schematic", ShortcutText = "T", Gesture = new KeyGesture(Key.T), MenuKey = "menu.place", MenuOrder = 70,
                Execute = () => UseTool("sch.tool.text"),
            },
            new("sch.tool.textBox", "sch.command.textBox")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 71,
                Execute = () => UseTool("sch.tool.textBox"),
            },
            new("sch.tool.cut", "sch.command.cutWire")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 74,
                Execute = () => UseTool("sch.tool.cut"),
            },
            new("sch.tool.sheet", "sch.command.sheet")
            {
                ScopeKey = "scope.schematic", ShortcutText = "S", Gesture = new KeyGesture(Key.S),
                MenuKey = "menu.place", MenuOrder = 75,
                Execute = () => UseTool("sch.tool.sheet"),
            },
            new("sch.tool.sheetPin", "sch.command.sheetPin")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 76,
                Execute = () => UseTool("sch.tool.sheetPin"),
            },
            new("sch.tool.line", "sch.command.line")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 80,
                Execute = () => UseTool("sch.tool.line"),
            },
            new("sch.tool.arc", "sch.command.arc")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 110,
                Execute = () => UseTool("sch.tool.arc"),
            },
            new("sch.tool.bezier", "sch.command.bezier")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.place", MenuOrder = 111,
                Execute = () => UseTool("sch.tool.bezier"),
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
            new("sch.net.highlight", "sch.command.highlightNet")
            {
                ScopeKey = "scope.schematic", ShortcutText = "`", MenuKey = "menu.view", MenuOrder = 30,
                CanExecute = () => _netAnchor is not null || _editor.Selection.Any(i => NetOf(i) is not null),
                Execute = ToggleNetHighlight,
            },
            new("sch.fieldsTable", "sch.command.fieldsTable")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 68,
                Execute = () => context.Workbench.RevealPanel(FieldsPanelId),
            },
            new("sch.annotate", "sch.command.annotate")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 70,
                CanExecute = () => SchAnnotation.Unannotated(Sheet, Instance).Count > 0,
                Execute = () => Guard(Annotate, context),
            },
            .. new[]
            {
                ("sch.alignLeft", SchematicEditor.AlignTo.Left, 90),
                ("sch.alignRight", SchematicEditor.AlignTo.Right, 91),
                ("sch.alignTop", SchematicEditor.AlignTo.Top, 92),
                ("sch.alignBottom", SchematicEditor.AlignTo.Bottom, 93),
                ("sch.alignMiddleAcross", SchematicEditor.AlignTo.MiddleAcross, 94),
                ("sch.alignMiddleDown", SchematicEditor.AlignTo.MiddleDown, 95),
                ("sch.alignToGrid", SchematicEditor.AlignTo.Grid, 96),
            }.Select(a => new CommandDescriptor(a.Item1, $"sch.command.{a.Item1["sch.".Length..]}")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = a.Item3,
                CanExecute = () => _editor.Selection.Count >= (a.Item2 == SchematicEditor.AlignTo.Grid ? 1 : 2),
                Execute = () => Guard(() => _editor.Align(a.Item2), context),
            }),
            new("sch.showHiddenFields", "sch.command.showHiddenFields")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.view", MenuOrder = 60,
                Execute = () => Guard(() => Reveal(LayerStyle.Sch.HiddenField), context),
            },
            new("sch.showHiddenPins", "sch.command.showHiddenPins")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.view", MenuOrder = 61,
                Execute = () => Guard(() => Reveal(LayerStyle.Sch.HiddenPin), context),
            },
            new("sch.drag", "sch.command.drag")
            {
                ScopeKey = "scope.schematic", ShortcutText = "G", Gesture = new KeyGesture(Key.G),
                MenuKey = "menu.edit", MenuOrder = 96,
                CanExecute = () => _editor.Selection.Any(SchEdits.CanTransform),
                Execute = () => _canvas?.BeginMoveWithCursor(stretching: true),
            },
            new("sch.lock", "sch.command.lock")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 97,
                CanExecute = () => _editor.Selection.Any(i => !i.IsLocked),
                Execute = () => Guard(() => Lock(true), context),
            },
            new("sch.unlock", "sch.command.unlock")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 98,
                CanExecute = () => _editor.Selection.Any(i => i.IsLocked),
                Execute = () => Guard(() => Lock(false), context),
            },
            new("sch.toLabel", "sch.command.toLabel")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 80,
                CanExecute = () => Relabelable().Count > 0,
                Execute = () => Guard(() => Relabel(SchLabelKind.Local), context),
            },
            new("sch.toGlobalLabel", "sch.command.toGlobalLabel")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 81,
                CanExecute = () => Relabelable().Count > 0,
                Execute = () => Guard(() => Relabel(SchLabelKind.Global), context),
            },
            new("sch.toHierarchicalLabel", "sch.command.toHierarchicalLabel")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 82,
                CanExecute = () => Relabelable().Count > 0,
                Execute = () => Guard(() => Relabel(SchLabelKind.Hierarchical), context),
            },
            new("sch.toText", "sch.command.toText")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 83,
                CanExecute = () => Relabelable().Count > 0,
                Execute = () => Guard(() => Relabel(null), context),
            },
            new("sch.renumber", "sch.command.renumber")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.edit", MenuOrder = 75,
                CanExecute = () => Sheet.Symbols.Count > 0,
                Execute = () => Guard(Renumber, context),
            },
            new("sch.fit", "sch.command.fit")
            {
                ScopeKey = "scope.schematic", ShortcutText = "Home", MenuKey = "menu.view", MenuOrder = 5,
                Execute = () => _canvas?.ZoomToFit(),
            },
            new("sch.exportNetlist", "sch.command.exportNetlist")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.file", MenuOrder = 80,
                CanExecute = () => FilePath is not null,
                Execute = () => _ = ExportAsync(context, "sch.command.exportNetlist", ".net",
                    () => SchNetlist.Write(RootFile(), OpenSheet, $"Anode {context.Manifest.Version}")),
            },
            new("sch.exportBom", "sch.command.exportBom")
            {
                ScopeKey = "scope.schematic", MenuKey = "menu.file", MenuOrder = 85,
                CanExecute = () => FilePath is not null,
                Execute = () => _ = ExportAsync(context, "sch.command.exportBom", ".csv", () => SchBom.Write(RootFile(), OpenSheet)),
            },
        ];

        foreach (var command in commands)
        {
            _registrations.Add(context.Commands.Register(command));
        }
    }

    private static KeyGesture Shortcut(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | extra);

    /// <summary>The fields table's place at the foot of the window.</summary>
    internal const string FieldsPanelId = "sch.fields";

    /// <summary>Brings the find panel up and puts the caret in it.</summary>
    private static void OpenFind(IPluginContext context, bool replace)
    {
        context.Workbench.RevealPanel(FindSession.PanelId);
        FindSession.RequestFocus(replace);
    }

    private void Guard(Action action, IPluginContext context)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or KiCadFormatException or SexprParseException or DecoderFallbackException)
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
        _editor.History.Changed -= ForgetDerived;
        _editor.History.Changed -= RefreshNetHighlight;
        _editor.History.Changed -= RedrawFrame;
        Tr.Changed -= OnLanguageChanged;
        base.Dispose();
    }

    protected override Control CreateView()
    {
        var canvas = new SchematicCanvas { Editor = _editor };
        canvas.ToolCancelled += () => UseTool(null);
        canvas.HighlightNetRequested += ToggleNetHighlight;
        canvas.HighlightCleared += () => HighlightNet(null);
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

    /// <summary>
    /// The nets of the whole design this sheet belongs to, sheets joined through their pins and global names. Worked
    /// out from the project's root, this sheet as it stands in the editor and the others as they are on disk; empty
    /// when the file belongs to no project.
    /// </summary>
    public IReadOnlyList<DesignNet> DesignNets =>
        _designNets ??= FilePath is { } path && SchematicDocumentType.ProjectRoot(Path.GetFullPath(path)) is { } root
            ? SchDesignNets.Build(root, OpenSheet)
            : [];

    /// <summary>The part of <paramref name="net"/> that lies on the sheet shown here, if any.</summary>
    internal SchNet? OnThisSheet(DesignNet net) =>
        net.Parts.FirstOrDefault(p => string.Equals(p.Place.Path, Instance, StringComparison.Ordinal)).Net
        ?? net.Parts.FirstOrDefault(p => Nets.Contains(p.Net)).Net;

    /// <summary>What an item is connected to, by name; null when it is on nothing or nothing is known.</summary>
    public string? NetOf(SchItem item) =>
        Nets.FirstOrDefault(net => net.Items.Contains(item))?.Name;

    /// <summary>The canvas draws again when the history moves; the workbench re-reads the document's state.</summary>
    public void Redraw() => _canvas?.Redraw();

    private void OnSelectionChanged() => OnPropertiesChanged(nameof(Selection), nameof(StatusFields));

    /// <summary>
    /// The frame prints the title block, which any edit may have changed and which no item on the sheet owns; it is
    /// one layer of a few hundred strokes, so it is simply drawn again.
    /// </summary>
    private void RedrawFrame()
    {
        SchematicSceneBuilder.RedrawFrame(Scene);
        _canvas?.Redraw();
    }

    /// <summary>What was worked out from the sheet as it was: its nets, and the design-wide designator check.</summary>
    private void ForgetDerived()
    {
        _nets = null;
        _designNets = null;
        _duplicates = null;
        _overview = null;
    }

    /// <summary>The sheet itself, for the inspector when nothing is selected; worked out once per state of the sheet.</summary>
    public override SelectionInfo? Overview => _overview ??= SheetOverview.Build(
        Sheet,
        FilePath,
        Scene.BoardOutline,
        Instance,
        _appearances,
        Nets,
        Issues,
        [
            .. SchAnnotation.Unannotated(Sheet, Instance).Count > 0
                ? new[] { new InspectorAction(Tr.T("sch.command.annotate"), Annotate) { IsPrimary = true } }
                : [],
            new InspectorAction(Tr.T("sch.command.fit"), () => _canvas?.ZoomToFit()),
        ],
        EditTitleBlock,
        DocumentFonts.Of(FontSheets()),
        EmbedFonts);

    /// <summary>
    /// The sheets whose texts decide what this file carries: the whole design when this is the sheet that keeps
    /// KiCad's setting — the design's root, as KiCad keeps it there — otherwise this sheet alone.
    /// </summary>
    private IReadOnlyList<Anode.Kicad.Schematic> FontSheets()
    {
        if (FilePath is not { } path || SchematicDocumentType.ProjectRoot(Path.GetFullPath(path)) is not { } root
            || !string.Equals(root, Path.GetFullPath(path), StringComparison.Ordinal))
        {
            return [Sheet];
        }

        return [.. SchHierarchy.Walk(root, OpenSheet).Select(p => p.File).Distinct(StringComparer.Ordinal)
            .Select(OpenSheet).OfType<Anode.Kicad.Schematic>()];
    }

    /// <summary>The root of the design this sheet belongs to; the sheet itself when it belongs to no project.</summary>
    private string RootFile()
    {
        string full = Path.GetFullPath(FilePath ?? throw new InvalidOperationException("The sheet has no path."));
        return SchematicDocumentType.ProjectRoot(full) ?? full;
    }

    /// <summary>
    /// Writes an export of the design where the person asks for it. What is written is the whole design's, not this
    /// sheet's: exporting from a sheet below the root still describes the project, with this sheet as it stands in
    /// the editor. The file is written the way a document is — a temp file beside the target, renamed over it — so
    /// nobody reads one that is only half there.
    /// </summary>
    private async Task ExportAsync(IPluginContext context, string titleKey, string extension, Func<string> contents)
    {
        if (FilePath is null)
        {
            return;
        }

        string root = RootFile();
        string suggested = Path.GetFileNameWithoutExtension(root) + extension;

        try
        {
            if (await context.Workbench.AskWhereToWriteAsync(suggested, extension, titleKey, Path.GetDirectoryName(root))
                is not { Length: > 0 } target)
            {
                return;
            }

            // Worked out once the path is known: a design of any size is not read to be thrown away on a cancel.
            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temp, contents());
            File.Move(temp, target, overwrite: true);
            context.Log.Info(Tr.English("sch.log.exported", Path.GetFileName(target)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException)
        {
            context.Log.Error(ex.Message, ex);
        }
    }

    /// <summary>KiCad's setting that the design carries its fonts, written as one undoable step.</summary>
    internal void EmbedFonts(bool on)
    {
        if (EmbeddedFonts.Wanted(Sheet.Root) == on)
        {
            return;
        }

        _editor.Run(new RootChildCommand(
            Sheet.Root, "embedded_fonts", Tr.T("sch.command.embedFonts"), () => EmbeddedFonts.SetWanted(Sheet.Root, on)));
    }

    /// <summary>
    /// Writes one field of the title block as one undoable step. The block is not an item on the sheet and may not
    /// exist yet, so the step remembers the whole block rather than a node that was never there.
    /// </summary>
    internal void EditTitleBlock(string field, string value)
    {
        // "comment3" is the third comment line; everything else names a field.
        int comment = field.StartsWith("comment", StringComparison.Ordinal)
            && int.TryParse(field.AsSpan("comment".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? n
                : 0;

        string current = (comment > 0 ? Sheet.TitleBlock.Comment(comment) : field switch
        {
            "title" => Sheet.TitleBlock.Title,
            "date" => Sheet.TitleBlock.Date,
            "rev" => Sheet.TitleBlock.Revision,
            "company" => Sheet.TitleBlock.Company,
            _ => null,
        }) ?? string.Empty;

        string written = value.Trim();
        if (string.Equals(current, written, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _editor.Run(new RootChildCommand(
                Sheet.Root,
                "title_block",
                Tr.T("sch.command.titleBlock"),
                () =>
                {
                    if (comment > 0)
                    {
                        TitleBlockWrites.SetComment(Sheet.Root, comment, written);
                    }
                    else
                    {
                        TitleBlockWrites.Set(Sheet.Root, field, written);
                    }
                }));
        }
        catch (Exception ex) when (ex is KiCadFormatException or ArgumentException)
        {
            _context?.Log.Error(ex.Message, ex);
        }
    }

    private void OnHistoryChanged() => OnPropertiesChanged(nameof(IsDirty), nameof(StatusFields), nameof(Summary));

    private string Count() => Tr.T("sch.status.symbols", Sheet.Symbols.Count, Tr.Plural("sch.symbol", Sheet.Symbols.Count));

    private void OnLanguageChanged()
    {
        _overview = null;
        OnPropertiesChanged(nameof(Title), nameof(Summary), nameof(StatusFields), nameof(Selection), nameof(Overview), nameof(Issues));
    }
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
