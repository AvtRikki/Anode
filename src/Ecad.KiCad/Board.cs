using Ecad.Geometry;
using Ecad.Sexpr;

namespace Ecad.KiCad;

public sealed class KiCadFormatException(string message) : Exception(message);

/// <summary>A <c>.kicad_pcb</c> file: the lossless CST plus typed views over its items.</summary>
public sealed class Board
{
    private readonly List<Footprint> _footprints = [];
    private readonly List<Segment> _segments = [];
    private readonly List<TrackArc> _arcs = [];
    private readonly List<Via> _vias = [];
    private readonly List<Zone> _zones = [];
    private readonly List<Shape> _shapes = [];
    private readonly List<Text> _texts = [];
    private readonly List<SList> _other = [];

    private Board(SDocument document)
    {
        Document = document;
        Root = document.Roots.OfType<SList>().FirstOrDefault()
            ?? throw new KiCadFormatException("File contains no S-expression.");

        if (Root.Head != "kicad_pcb")
        {
            throw new KiCadFormatException($"Expected (kicad_pcb ...), found ({Root.Head} ...).");
        }

        Version = Root.Find("version") is { } v && int.TryParse(v.AtomAt(1)?.Raw, out int version) ? version : 0;
        Layers = new LayerTable(Root.Find("layers"));

        // Net declarations come before items in legacy files, but register them first regardless of order.
        foreach (var net in Root.FindAll("net"))
        {
            Nets.Declare(net);
        }

        foreach (var child in Root.Lists())
        {
            switch (child.Head)
            {
                case "footprint":
                    _footprints.Add(new Footprint(child, this));
                    break;
                case "segment":
                    _segments.Add(new Segment(child, this));
                    break;
                case "arc":
                    _arcs.Add(new TrackArc(child, this));
                    break;
                case "via":
                    _vias.Add(new Via(child, this));
                    break;
                case "zone":
                    _zones.Add(new Zone(child, this, null));
                    break;
                case "gr_text":
                    _texts.Add(new Text(child, this, null));
                    break;
                case var head when Shape.IsShapeHead(head):
                    _shapes.Add(new Shape(child, this, Transform2D.Identity));
                    break;
                case "version" or "generator" or "generator_version" or "general" or "paper" or "title_block"
                    or "layers" or "setup" or "net" or "property":
                    break;
                default:
                    _other.Add(child);
                    break;
            }
        }
    }

    public SDocument Document { get; }

    public SList Root { get; }

    public int Version { get; }

    public string? Generator => Root.ChildString("generator");

    public string? GeneratorVersion => Root.ChildString("generator_version");

    public bool IsSupportedVersion => Version >= KiCadFormat.OldestSupported;

    public bool IsNewerThanKnown => Version > KiCadFormat.NewestKnown;

    public double Thickness => Root.Find("general")?.ChildDouble("thickness") ?? 1.6;

    public LayerTable Layers { get; }

    public NetTable Nets { get; } = new();

    public IReadOnlyList<Footprint> Footprints => _footprints;

    public IReadOnlyList<Segment> Segments => _segments;

    public IReadOnlyList<TrackArc> Arcs => _arcs;

    public IReadOnlyList<Via> Vias => _vias;

    public IReadOnlyList<Zone> Zones => _zones;

    public IReadOnlyList<Shape> Shapes => _shapes;

    public IReadOnlyList<Text> Texts => _texts;

    /// <summary>Top-level lists this model does not interpret (dimensions, groups, tables, images...). Kept in the file untouched.</summary>
    public IReadOnlyList<SList> OtherItems => _other;

    public static Board Load(string path) => FromDocument(SDocument.Load(path));

    public static Board Parse(string text) => FromDocument(SDocument.Parse(text));

    public static Board FromDocument(SDocument document) => new(document);

    public void Save(string path) => Document.Save(path);

    /// <summary>Board outline extent from Edge.Cuts, falling back to all items.</summary>
    public Box2L ComputeBounds()
    {
        var edge = Box2L.Empty;
        foreach (var shape in _shapes.Concat(_footprints.SelectMany(f => f.Shapes)))
        {
            if (shape.LayerNames.Contains("Edge.Cuts"))
            {
                edge = edge.Union(shape.Bounds);
            }
        }

        if (!edge.IsEmpty)
        {
            return edge;
        }

        var all = Box2L.Empty;
        foreach (var s in _shapes)
        {
            all = all.Union(s.Bounds);
        }

        foreach (var f in _footprints)
        {
            all = all.Union(f.Bounds);
        }

        foreach (var t in _segments)
        {
            all = all.Union(Box2L.FromPoints(t.Start, t.End).Inflate(t.Width / 2));
        }

        foreach (var v in _vias)
        {
            all = all.Union(Box2L.FromCircle(v.Position, v.Size / 2));
        }

        return all;
    }
}
