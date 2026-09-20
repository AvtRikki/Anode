using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>Typed view over one list of a schematic. Reads are computed from the tree.</summary>
public abstract class SchItem(SList node) : INodeItem
{
    public SList Node { get; } = node;

    /// <summary>False once the item has been removed from its sheet.</summary>
    public bool IsAttached => Node.Parent is not null;

    public string? Uuid => Node.ChildString("uuid");

    /// <summary>Position of <c>(at x y [angle])</c>, in nanometres.</summary>
    public Vector2L Position => Node.ChildPoint("at") ?? default;

    /// <summary>Degrees from <c>(at x y angle)</c>; zero when the item has none.</summary>
    public double Angle => Node.Find("at") is { Count: > 3 } at ? at.Double(3) : 0;

    /// <summary>Stroke width in nanometres; zero means "the default width for this kind of item".</summary>
    public long StrokeWidth => Node.Find("stroke")?.ChildNm("width") ?? 0;

    /// <summary>Text height of <c>(effects (font (size w h)))</c>, in nanometres.</summary>
    protected long FontHeight(long fallback)
    {
        var size = Node.Find("effects")?.Find("font")?.Find("size");
        return size is { Count: > 2 } ? size.Nm(2) : fallback;
    }

    /// <summary>The face, weight and slant of the item's own text; the stroke font when it names none.</summary>
    public TextFont Font => TextFont.Read(Node.Find("effects"));

    protected (string Horizontal, string Vertical) Justify()
    {
        if (Node.Find("effects")?.Find("justify") is not { } justify)
        {
            return ("center", "center");
        }

        string horizontal = "center";
        string vertical = "center";
        for (int i = 1; i < justify.Count; i++)
        {
            switch (justify.Str(i))
            {
                case "left": horizontal = "left"; break;
                case "right": horizontal = "right"; break;
                case "top": vertical = "top"; break;
                case "bottom": vertical = "bottom"; break;
            }
        }

        return (horizontal, vertical);
    }

    /// <summary>Schematic items read straight from the tree, so a restored subtree needs no rebuilding.</summary>
    public virtual void AfterRestore()
    {
    }

    /// <summary><c>(hide yes)</c> either directly or inside <c>(effects ...)</c>, as both spellings exist.</summary>
    protected bool Hidden => Node.ChildBool("hide") || Node.Find("effects")?.ChildBool("hide") == true;
}

public enum SchShapeKind
{
    Polyline,
    Rectangle,
    Circle,
    Arc,
    Bezier,
    Unsupported,
}

/// <summary>A graphic of a symbol body or of the sheet: polyline, rectangle, circle, arc.</summary>
public sealed class SchGraphic(SList node) : SchItem(node)
{
    public SchShapeKind Kind { get; } = node.Head switch
    {
        "polyline" => SchShapeKind.Polyline,
        "rectangle" => SchShapeKind.Rectangle,
        "circle" => SchShapeKind.Circle,
        "arc" => SchShapeKind.Arc,
        "bezier" => SchShapeKind.Bezier,
        _ => SchShapeKind.Unsupported,
    };

    public Vector2L Start => Node.ChildPoint("start") ?? default;

    public Vector2L Mid => Node.ChildPoint("mid") ?? default;

    public Vector2L End => Node.ChildPoint("end") ?? default;

    public Vector2L Center => Node.ChildPoint("center") ?? default;

    /// <summary>Schematic circles store the radius, unlike board circles.</summary>
    public long Radius => Node.ChildNm("radius") ?? 0;

    public Vector2L[] Points => Node.Find("pts")?.Points() ?? [];

    /// <summary>Filled bodies are drawn in their fill colour; <c>none</c> and <c>background</c> stay outlines.</summary>
    public bool IsFilled => Node.Find("fill")?.ChildString("type") is "outline" or "color";

    public Arc? ArcGeometry => Kind == SchShapeKind.Arc
        ? ArcMath.FromStartMidEnd(Start.ToDouble(), Mid.ToDouble(), End.ToDouble())
        : null;

    public static bool IsGraphicHead(string? head) => head is "polyline" or "rectangle" or "circle" or "arc" or "bezier";
}

/// <summary>A pin of a symbol definition: the line sticking out of the body, plus its name and number.</summary>
public sealed class SchPin(SList node) : SchItem(node)
{
    /// <summary>"input", "power_in", "passive"…</summary>
    public string ElectricalType => Node.Str(1) ?? "unspecified";

    /// <summary>"line", "inverted", "clock"…</summary>
    public string GraphicStyle => Node.Str(2) ?? "line";

    public long Length => Node.ChildNm("length") ?? 0;

    public string Name => Node.Find("name")?.Str(1) ?? string.Empty;

    public string Number => Node.Find("number")?.Str(1) ?? string.Empty;

    public bool IsHidden => Node.ChildBool("hide");

    public long NameHeight => Size(Node.Find("name"), 1_270_000);

    public long NumberHeight => Size(Node.Find("number"), 1_270_000);

    public TextFont NameFont => TextFont.Read(Node.Find("name")?.Find("effects"));

    public TextFont NumberFont => TextFont.Read(Node.Find("number")?.Find("effects"));

    /// <summary>
    /// The far end of the pin line, away from the point a wire meets — <see cref="SchItem.Position"/> is the
    /// connection point, and the pin is drawn from there towards the body. Useful for drawing the line; not the
    /// place to look for what a pin is wired to.
    /// </summary>
    public Vector2L EndPoint
    {
        get
        {
            var (sin, cos) = Transform2D.SinCos(Angle);
            return new Vector2L(Position.X + (long)(Length * cos), Position.Y + (long)(Length * sin));
        }
    }

    private static long Size(SList? owner, long fallback)
    {
        var size = owner?.Find("effects")?.Find("font")?.Find("size");
        return size is { Count: > 2 } ? size.Nm(2) : fallback;
    }
}

/// <summary>A symbol definition carried in the file's <c>lib_symbols</c>.</summary>
public sealed class LibSymbol : SchItem
{
    private readonly List<SchGraphic> _graphics = [];
    private readonly List<SchPin> _pins = [];

    internal LibSymbol(SList node)
        : base(node)
    {
        Rebuild();
    }

    /// <summary>
    /// Reads the bodies out of the node again. A definition can be rewritten under a live wrapper — by "update from
    /// library", and by the undo that takes it back — and pins and graphics read once in the constructor would go on
    /// describing the part as it used to be.
    /// </summary>
    public override void AfterRestore() => Rebuild();

    private void Rebuild()
    {
        Name = Node.Str(1) ?? string.Empty;
        _graphics.Clear();
        _pins.Clear();
        Units.Clear();

        // Bodies live in child symbols named "<symbol>_<unit>_<bodyStyle>"; unit 0 is common to every unit.
        foreach (var unit in Node.Lists().Where(l => l.Head == "symbol"))
        {
            var (number, style) = ParseUnit(unit.Str(1));
            foreach (var child in unit.Lists())
            {
                if (SchGraphic.IsGraphicHead(child.Head))
                {
                    _graphics.Add(new SchGraphic(child));
                    Units.Add((number, style, _graphics.Count - 1, true));
                }
                else if (child.Head == "pin")
                {
                    _pins.Add(new SchPin(child));
                    Units.Add((number, style, _pins.Count - 1, false));
                }
            }
        }
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Which unit and body style each graphic (true) or pin (false) belongs to.</summary>
    private List<(int Unit, int Style, int Index, bool IsGraphic)> Units { get; } = [];

    public string? Extends => Node.ChildString("extends");

    public bool ShowPinNames => Node.Find("pin_names")?.ChildBool("hide") != true;

    public bool ShowPinNumbers => Node.Find("pin_numbers")?.ChildBool("hide") != true;

    /// <summary>Distance between the pin line and its name, in nanometres.</summary>
    public long PinNameOffset => Node.Find("pin_names")?.ChildNm("offset") ?? 508_000;

    public IReadOnlyList<SchGraphic> Graphics => _graphics;

    public IReadOnlyList<SchPin> Pins => _pins;

    /// <summary>
    /// The designator prefix the library gives this part — "R", "C", "U". KiCad writes it with a question mark on a
    /// part that has not been annotated yet.
    /// </summary>
    public string? Reference => Field("Reference");

    /// <summary>What the library says the part is for, when it says anything.</summary>
    public string? Description => Field("Description");

    /// <summary>The value the library gives the part — for a power symbol, the name of the net it is.</summary>
    public string? Value => Field("Value");

    /// <summary>
    /// Whether this definition is a power symbol: a part that is not a part at all, but a name for a net drawn as a
    /// symbol. Both spellings appear in the wild — the bare <c>(power)</c> of older files and the
    /// <c>(power global)</c> of newer ones — so the marker's presence is the test, not its words.
    /// </summary>
    public bool IsPower => Node.Find("power") is not null;

    private string? Field(string name) => Node.Lists()
        .FirstOrDefault(l => l.Head == "property" && string.Equals(l.Str(1), name, StringComparison.Ordinal))
        ?.Str(2) is { Length: > 0 } value
        ? value
        : null;

    /// <summary>
    /// How many sections the part is drawn in — four for a quad gate, one for an ordinary part. Unit 0 is not a
    /// section but the body common to all of them, so the count is the largest number used rather than how many
    /// entries there are. A definition that <see cref="Extends"/> another carries no bodies of its own and so
    /// reports one section, which is all it can say until the parent is resolved.
    /// </summary>
    public int UnitCount => Units.Count == 0 ? 1 : Math.Max(1, Units.Max(u => u.Unit));

    /// <summary>Graphics of one placed unit: its own plus the ones common to all units.</summary>
    public IEnumerable<SchGraphic> GraphicsOf(int unit, int bodyStyle) =>
        Units.Where(u => u.IsGraphic && Matches(u, unit, bodyStyle)).Select(u => _graphics[u.Index]);

    public IEnumerable<SchPin> PinsOf(int unit, int bodyStyle) =>
        Units.Where(u => !u.IsGraphic && Matches(u, unit, bodyStyle)).Select(u => _pins[u.Index]);

    private static bool Matches((int Unit, int Style, int Index, bool IsGraphic) entry, int unit, int bodyStyle) =>
        (entry.Unit == 0 || entry.Unit == unit) && (entry.Style == 0 || entry.Style == bodyStyle);

    private static (int Unit, int Style) ParseUnit(string? name)
    {
        // "74LS125_1_1" → unit 1, body style 1.
        if (name is null)
        {
            return (0, 0);
        }

        var parts = name.Split('_');
        return parts.Length >= 3 && int.TryParse(parts[^2], out int unit) && int.TryParse(parts[^1], out int style)
            ? (unit, style)
            : (0, 0);
    }
}

/// <summary>A <c>property</c> of a symbol or a sheet: reference, value, sheet name…</summary>
public sealed class SchField(SList node) : SchItem(node)
{
    public string Name => Node.Str(1) ?? string.Empty;

    public string Value => Node.Str(2) ?? string.Empty;

    public bool IsHidden => Hidden;

    public long TextHeight => FontHeight(1_270_000);

    public (string Horizontal, string Vertical) Alignment => Justify();
}

/// <summary>A placed symbol.</summary>
public sealed class SymbolInstance : SchItem
{
    private readonly Schematic _schematic;
    private readonly List<SchField> _fields = [];

    internal SymbolInstance(SList node, Schematic schematic)
        : base(node)
    {
        _schematic = schematic;
        Rebuild();
    }

    /// <summary>
    /// Reads the fields out of the node again. <see cref="Field"/> answers from this list rather than the tree, so a
    /// change that adds or drops a property — swapping the part for another one, and the undo that takes it back —
    /// would otherwise leave the symbol reporting the designator and value it used to carry.
    /// </summary>
    public override void AfterRestore() => Rebuild();

    private void Rebuild()
    {
        _fields.Clear();
        foreach (var property in Node.Lists().Where(l => l.Head == "property"))
        {
            _fields.Add(new SchField(property));
        }
    }

    public string LibId => Node.ChildString("lib_id") ?? string.Empty;

    public int Unit => (int)(Node.ChildDouble("unit") ?? 1);

    public int BodyStyle => (int)(Node.ChildDouble("body_style") ?? Node.ChildDouble("convert") ?? 1);

    public bool IsDnp => Node.ChildBool("dnp");

    /// <summary><c>(mirror y)</c> flips the symbol left to right, <c>(mirror x)</c> top to bottom.</summary>
    public string? Mirror => Node.Find("mirror")?.Str(1);

    public IReadOnlyList<SchField> Fields => _fields;

    public string? Reference => Field("Reference");

    public string? Value => Field("Value");

    public string? Footprint => Field("Footprint");

    public LibSymbol? Definition => _schematic.Definition(this);

    /// <summary>
    /// Symbol coordinates to sheet coordinates. Library geometry has Y up, the sheet has Y down, so the placement is
    /// a mirror of the library frame followed by the usual rotate and translate.
    /// </summary>
    public Transform2D ToSheet
    {
        get
        {
            double mirrorX = Mirror == "y" ? -1 : 1;
            double mirrorY = Mirror == "x" ? -1 : 1;
            return Transform2D.Scale(mirrorX, -mirrorY).Then(KiCadTransforms.Placement(Position, Angle, 1, 1));
        }
    }

    public string? Field(string name) =>
        _fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>
    /// The designator and section this symbol carries in one appearance of its sheet. A sheet placed twice in a
    /// hierarchy draws the same symbols twice under different names — RV201 in one, RV301 in the other — and the
    /// Reference property can hold only one of them. Across the KiCad demos and QA designs it disagrees with the
    /// true name for most symbols on a reused sheet, and even on a root sheet it is sometimes stale. Null when the
    /// file says nothing for that path.
    /// </summary>
    public (string Reference, int Unit)? InstanceAt(string path)
    {
        foreach (var project in Node.Find("instances")?.Lists().Where(l => l.Head == "project") ?? [])
        {
            foreach (var entry in project.Lists().Where(l => l.Head == "path"))
            {
                if (string.Equals(entry.Str(1), path, StringComparison.Ordinal))
                {
                    return (entry.ChildString("reference") ?? Reference ?? string.Empty, (int)(entry.ChildDouble("unit") ?? Unit));
                }
            }
        }

        return null;
    }

    /// <summary>The designator in the given appearance of the sheet, or the property when there is no path or no entry.</summary>
    public string? ReferenceAt(string? path) => path is not null && InstanceAt(path) is { } at ? at.Reference : Reference;

    /// <summary>The section in the given appearance of the sheet; the symbol's own when there is no entry.</summary>
    public int UnitAt(string? path) => path is not null && InstanceAt(path) is { } at ? at.Unit : Unit;
}

/// <summary>A wire or a bus segment.</summary>
public sealed class SchWire(SList node) : SchItem(node)
{
    public bool IsBus => Node.Head == "bus";

    public Vector2L[] Points => Node.Find("pts")?.Points() ?? [];
}

/// <summary>The little diagonal that connects a wire to a bus.</summary>
public sealed class SchBusEntry(SList node) : SchItem(node)
{
    public Vector2L Size => Node.ChildPoint("size") ?? default;

    public Vector2L EndPoint => new(Position.X + Size.X, Position.Y + Size.Y);
}

/// <summary>A connection dot.</summary>
public sealed class SchJunction(SList node) : SchItem(node)
{
    public long Diameter => Node.ChildNm("diameter") is { } d and > 0 ? d : 914_400;
}

/// <summary>The cross marking a pin that is deliberately left unconnected.</summary>
public sealed class SchNoConnect(SList node) : SchItem(node);

public enum SchLabelKind
{
    Local,
    Global,
    Hierarchical,
    NetClassFlag,
}

/// <summary>A net name written on the sheet.</summary>
public sealed class SchLabel(SList node) : SchItem(node)
{
    public SchLabelKind Kind { get; } = node.Head switch
    {
        "global_label" => SchLabelKind.Global,
        "hierarchical_label" => SchLabelKind.Hierarchical,
        "netclass_flag" => SchLabelKind.NetClassFlag,
        _ => SchLabelKind.Local,
    };

    public string Text => Node.Str(1) ?? string.Empty;

    /// <summary>The text as KiCad shows it, with its escapes put back — <c>{slash}</c> is a "/".</summary>
    public string Shown => KicadText.Unescape(Text);

    /// <summary>"input", "output", "bidirectional", "tri_state", "passive" — the arrow drawn around the text.</summary>
    public string Shape => Node.ChildString("shape") ?? "passive";

    public long TextHeight => FontHeight(1_270_000);

    public (string Horizontal, string Vertical) Alignment => Justify();
}

/// <summary>Free text on the sheet.</summary>
public sealed class SchText(SList node) : SchItem(node)
{
    public string Text => Node.Str(1) ?? string.Empty;

    /// <summary>The text as KiCad shows it, with its escapes put back — <c>{slash}</c> is a "/".</summary>
    public string Shown => KicadText.Unescape(Text);

    public long TextHeight => FontHeight(1_270_000);

    public (string Horizontal, string Vertical) Alignment => Justify();
}

/// <summary>A pin on the border of a child sheet.</summary>
public sealed class SchSheetPin(SList node) : SchItem(node)
{
    public string Name => Node.Str(1) ?? string.Empty;

    public string Shape => Node.Str(2) ?? "passive";

    public long TextHeight => FontHeight(1_270_000);
}

/// <summary>A child sheet of the hierarchy: a rectangle with its name, file and pins.</summary>
public sealed class SchSheet : SchItem
{
    private readonly List<SchField> _fields = [];
    private readonly List<SchSheetPin> _pins = [];

    internal SchSheet(SList node)
        : base(node)
    {
        Rebuild();
    }

    /// <summary>
    /// Reads the fields and pins out of the node again. The name and the file are answered from that list rather
    /// than the tree, so a change that adds or drops one — a pin following a hierarchical label, and the undo that
    /// takes it back — would otherwise leave the sheet reporting what it used to hold.
    /// </summary>
    public override void AfterRestore() => Rebuild();

    private void Rebuild()
    {
        _fields.Clear();
        _pins.Clear();

        foreach (var child in Node.Lists())
        {
            if (child.Head == "property")
            {
                _fields.Add(new SchField(child));
            }
            else if (child.Head == "pin" && child.Count > 2)
            {
                _pins.Add(new SchSheetPin(child));
            }
        }
    }

    public Vector2L Size => Node.ChildPoint("size") ?? default;

    public IReadOnlyList<SchField> Fields => _fields;

    public IReadOnlyList<SchSheetPin> Pins => _pins;

    public string? SheetName => _fields.FirstOrDefault(f => f.Name == "Sheetname")?.Value;

    public string? SheetFile => _fields.FirstOrDefault(f => f.Name == "Sheetfile")?.Value;
}
