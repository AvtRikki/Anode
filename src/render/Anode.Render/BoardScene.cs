using System.Numerics;
using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Render;

/// <summary>Everything drawn on one layer, in scene millimetres.</summary>
public sealed class LayerGeometry(string name)
{
    public string Name { get; } = name;

    public ColorRgba Color { get; set; } = LayerStyle.ColorFor(name);

    public bool IsVisible { get; set; } = true;

    public bool IsCopper { get; } = LayerStyle.IsCopper(name);

    /// <summary>
    /// Drawn but never picked, selected or dimmed: the board body under the layers, the paper a sheet is drawn on, and
    /// the page frame around a board. All are the ground the drawing sits on rather than part of it — dimming them
    /// makes the whole view blink.
    /// </summary>
    public bool IsDecoration { get; } = name is LayerStyle.BoardBody or LayerStyle.Sch.Sheet or LayerStyle.PageFrame;

    public int DrawOrder { get; } = LayerStyle.DrawOrder(name);

    public List<LinePrim> Lines { get; } = [];

    public List<CirclePrim> Circles { get; } = [];

    public List<PolygonPrim> Polygons { get; } = [];

    public List<ImagePrim> Images { get; } = [];

    public int PrimitiveCount => Lines.Count + Circles.Count + Polygons.Count + Images.Count;

    public RectD Bounds { get; internal set; } = RectD.Empty;

    /// <summary>Incremented whenever primitives change so backends can drop caches.</summary>
    public int Version { get; internal set; }
}

/// <summary>
/// Backend-agnostic display list for a board. Primitives belong to owners (pads, tracks, texts...), and owners
/// are grouped by their top-level item so edited items can be removed and rebuilt without touching the rest.
/// </summary>
public sealed class BoardScene : IRenderScene
{
    private readonly Dictionary<string, LayerGeometry> _byName = new(StringComparer.Ordinal);
    private readonly List<LayerGeometry> _layers = [];
    private readonly List<BoardItem?> _owners = [];
    private readonly List<Net?> _ownerNets = [];
    private readonly List<RectD> _ownerBounds = [];
    private readonly Dictionary<BoardItem, List<int>> _ownersByTop = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LayerGeometry> _touched = [];

    internal BoardScene(Board board, Vector2L originNm)
    {
        Board = board;
        OriginNm = originNm;
    }

    public Board Board { get; }

    /// <summary>Board point (nm) that maps to scene (0, 0).</summary>
    public Vector2L OriginNm { get; }

    public RectD BoardOutline { get; internal set; } = RectD.Empty;

    /// <summary>What the page's title block prints besides its own fields; see <see cref="SheetFrameText"/>.</summary>
    public SheetFrameText Frame { get; set; } = new();

    public RectD Bounds { get; private set; } = RectD.Empty;

    /// <summary>Layers in draw order (bottom first).</summary>
    public IReadOnlyList<LayerGeometry> Layers => _layers;

    public LayerGeometry? Find(string name) => _byName.GetValueOrDefault(name);

    public int OwnerCount => _owners.Count;

    public int PrimitiveCount => _layers.Sum(l => l.PrimitiveCount);

    /// <summary>Top-level items that currently have primitives in the scene.</summary>
    public IEnumerable<BoardItem> TopLevelItems => _ownersByTop.Keys;

    public bool IsLive(int id) => (uint)id < (uint)_owners.Count && _owners[id] is not null;

    public BoardItem Owner(int id) => _owners[id] ?? throw new InvalidOperationException($"Owner {id} was removed from the scene.");

    public BoardItem TopLevelOf(int id) => Owner(id).TopLevel;

    /// <summary>Decoration primitives carry no owner, so an unknown id answers "nothing", not an exception.</summary>
    public Net? OwnerNet(int id) => (uint)id < (uint)_ownerNets.Count ? _ownerNets[id] : null;

    public RectD OwnerBounds(int id) => (uint)id < (uint)_ownerBounds.Count ? _ownerBounds[id] : RectD.Empty;

    public IReadOnlyList<int> OwnersOf(BoardItem topLevel) =>
        _ownersByTop.TryGetValue(topLevel, out var ids) ? ids : [];

    public RectD BoundsOf(BoardItem topLevel)
    {
        var bounds = RectD.Empty;
        foreach (int id in OwnersOf(topLevel))
        {
            bounds = bounds.Union(_ownerBounds[id]);
        }

        return bounds;
    }

    public Vector2 ToScene(Vector2D boardNm)
    {
        var mm = ToSceneMm(boardNm);
        return new Vector2((float)mm.X, (float)mm.Y);
    }

    public Vector2D ToSceneMm(Vector2D boardNm) =>
        new((boardNm.X - OriginNm.X) / Units.NmPerMm, (boardNm.Y - OriginNm.Y) / Units.NmPerMm);

    public Vector2D ToBoardNm(Vector2D sceneMm) =>
        new(sceneMm.X * Units.NmPerMm + OriginNm.X, sceneMm.Y * Units.NmPerMm + OriginNm.Y);

    /// <summary>
    /// Removes every primitive of the given top-level items. With <paramref name="collect"/> the removed primitives
    /// are returned as detached layers in draw order, e.g. to draw a move preview.
    /// </summary>
    public IReadOnlyList<LayerGeometry> Remove(IEnumerable<BoardItem> topLevelItems, bool collect = false)
    {
        var ids = new HashSet<int>();
        foreach (var top in topLevelItems)
        {
            if (_ownersByTop.Remove(top, out var list))
            {
                foreach (int id in list)
                {
                    ids.Add(id);
                    _owners[id] = null;
                    _ownerNets[id] = null;
                }
            }
        }

        if (ids.Count == 0)
        {
            return [];
        }

        var removed = new List<LayerGeometry>();
        foreach (var layer in _byName.Values)
        {
            var copy = collect ? new LayerGeometry(layer.Name) { Color = layer.Color } : null;
            int count = layer.Lines.RemoveAll(p => Take(p.Owner, p, copy?.Lines))
                        + layer.Circles.RemoveAll(p => Take(p.Owner, p, copy?.Circles))
                        + layer.Polygons.RemoveAll(p => Take(p.Owner, p, copy?.Polygons))
                        + layer.Images.RemoveAll(p => Take(p.Owner, p, copy?.Images));

            if (count > 0)
            {
                _touched.Add(layer);
                if (copy is not null)
                {
                    copy.Bounds = ComputeBounds(copy);
                    removed.Add(copy);
                }
            }
        }

        Commit();
        removed.Sort((a, b) => a.DrawOrder.CompareTo(b.DrawOrder));
        return removed;

        bool Take<T>(int owner, T primitive, List<T>? sink)
        {
            if (!ids.Contains(owner))
            {
                return false;
            }

            sink?.Add(primitive);
            return true;
        }
    }

    internal int AddOwner(BoardItem item)
    {
        int id = _owners.Count;
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
        _ownerBounds.Add(RectD.Empty);

        var top = item.TopLevel;
        if (!_ownersByTop.TryGetValue(top, out var ids))
        {
            ids = [];
            _ownersByTop[top] = ids;
        }

        ids.Add(id);
        return id;
    }

    internal void GrowOwner(int id, RectD bounds) => _ownerBounds[id] = _ownerBounds[id].Union(bounds);

    internal LayerGeometry Layer(string name)
    {
        if (!_byName.TryGetValue(name, out var layer))
        {
            layer = new LayerGeometry(name);
            _byName[name] = layer;
        }

        _touched.Add(layer);
        return layer;
    }

    /// <summary>Applies pending changes: bounds and versions of touched layers, layer order, scene bounds.</summary>
    internal void Commit()
    {
        foreach (var layer in _touched)
        {
            layer.Bounds = ComputeBounds(layer);
            layer.Version++;
        }

        _touched.Clear();

        _layers.Clear();
        _layers.AddRange(_byName.Values.Where(l => l.PrimitiveCount > 0).OrderBy(l => l.DrawOrder).ThenBy(l => l.Name, StringComparer.Ordinal));

        var all = RectD.Empty;
        foreach (var layer in _layers)
        {
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

        foreach (var image in layer.Images)
        {
            r = r.Union(image.Bounds);
        }

        return r;
    }
}
