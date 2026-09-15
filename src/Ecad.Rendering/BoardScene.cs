using System.Numerics;
using Ecad.Geometry;
using Ecad.KiCad;

namespace Ecad.Rendering;

/// <summary>Everything drawn on one layer, in scene millimetres.</summary>
public sealed class LayerGeometry(string name)
{
    public string Name { get; } = name;

    public ColorRgba Color { get; set; } = LayerStyle.ColorFor(name);

    public bool IsVisible { get; set; } = true;

    public bool IsCopper { get; } = LayerStyle.IsCopper(name);

    public int DrawOrder { get; } = LayerStyle.DrawOrder(name);

    public List<LinePrim> Lines { get; } = [];

    public List<CirclePrim> Circles { get; } = [];

    public List<PolygonPrim> Polygons { get; } = [];

    public List<TextPrim> Texts { get; } = [];

    public int PrimitiveCount => Lines.Count + Circles.Count + Polygons.Count + Texts.Count;

    public RectD Bounds { get; internal set; } = RectD.Empty;

    /// <summary>Incremented whenever primitives change so backends can drop caches.</summary>
    public int Version { get; internal set; }
}

/// <summary>Backend-agnostic display list for a board.</summary>
public sealed class BoardScene
{
    private readonly Dictionary<string, LayerGeometry> _byName = new(StringComparer.Ordinal);
    private readonly List<LayerGeometry> _layers = [];
    private readonly List<BoardItem> _owners = [];
    private readonly List<Net?> _ownerNets = [];

    internal BoardScene(Board board, Vector2L originNm)
    {
        Board = board;
        OriginNm = originNm;
    }

    public Board Board { get; }

    /// <summary>Board point (nm) that maps to scene (0, 0).</summary>
    public Vector2L OriginNm { get; }

    public RectD BoardOutline { get; internal set; } = RectD.Empty;

    public RectD Bounds { get; internal set; } = RectD.Empty;

    /// <summary>Layers in draw order (bottom first).</summary>
    public IReadOnlyList<LayerGeometry> Layers => _layers;

    public LayerGeometry? Find(string name) => _byName.GetValueOrDefault(name);

    public BoardItem Owner(int id) => _owners[id];

    public Net? OwnerNet(int id) => _ownerNets[id];

    public int OwnerCount => _owners.Count;

    public int PrimitiveCount => _layers.Sum(l => l.PrimitiveCount);

    public Vector2 ToScene(Vector2D boardNm) =>
        new((float)((boardNm.X - OriginNm.X) / Units.NmPerMm), (float)((boardNm.Y - OriginNm.Y) / Units.NmPerMm));

    public Vector2D ToBoardNm(Vector2D sceneMm) =>
        new(sceneMm.X * Units.NmPerMm + OriginNm.X, sceneMm.Y * Units.NmPerMm + OriginNm.Y);

    internal int AddOwner(BoardItem item)
    {
        _owners.Add(item);
        _ownerNets.Add(item switch
        {
            Pad p => p.Net,
            Segment s => s.Net,
            TrackArc a => a.Net,
            Via v => v.Net,
            Zone z => z.Net,
            Shape sh => sh.Net,
            _ => null,
        });
        return _owners.Count - 1;
    }

    internal LayerGeometry Layer(string name)
    {
        if (!_byName.TryGetValue(name, out var layer))
        {
            layer = new LayerGeometry(name);
            _byName[name] = layer;
        }

        return layer;
    }

    internal void Finish()
    {
        _layers.Clear();
        _layers.AddRange(_byName.Values.Where(l => l.PrimitiveCount > 0).OrderBy(l => l.DrawOrder).ThenBy(l => l.Name, StringComparer.Ordinal));

        var all = RectD.Empty;
        foreach (var layer in _layers)
        {
            layer.Bounds = ComputeBounds(layer);
            all = all.Union(layer.Bounds);
        }

        Bounds = all;
    }

    private static RectD ComputeBounds(LayerGeometry layer)
    {
        var r = RectD.Empty;
        foreach (var l in layer.Lines)
        {
            r = r.Union(Math.Min(l.A.X, l.B.X) - l.Width / 2, Math.Min(l.A.Y, l.B.Y) - l.Width / 2)
                 .Union(Math.Max(l.A.X, l.B.X) + l.Width / 2, Math.Max(l.A.Y, l.B.Y) + l.Width / 2);
        }

        foreach (var c in layer.Circles)
        {
            r = r.Union(c.Center.X - c.Radius, c.Center.Y - c.Radius).Union(c.Center.X + c.Radius, c.Center.Y + c.Radius);
        }

        foreach (var p in layer.Polygons)
        {
            r = r.Union(p.Bounds);
        }

        foreach (var t in layer.Texts)
        {
            r = r.Union(t.Position.X, t.Position.Y);
        }

        return r;
    }
}
