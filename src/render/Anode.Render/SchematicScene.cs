using System.Numerics;
using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Render;

/// <summary>
/// Primitives of one schematic sheet, grouped by the parts of the drawing (wires, symbol bodies, pins, labels…).
/// The counterpart of <see cref="BoardScene"/>: same primitives, same renderers, no layers and no nets.
/// </summary>
public sealed class SchematicScene : IRenderScene
{
    private readonly Dictionary<string, LayerGeometry> _byName = new(StringComparer.Ordinal);
    private readonly List<LayerGeometry> _layers = [];
    private readonly List<SchItem?> _owners = [];
    private readonly List<RectD> _ownerBounds = [];
    private readonly HashSet<LayerGeometry> _touched = [];

    internal SchematicScene(Schematic schematic, Vector2L originNm)
    {
        Schematic = schematic;
        OriginNm = originNm;
    }

    public Schematic Schematic { get; }

    /// <summary>Sheet point (nm) that maps to scene (0, 0).</summary>
    public Vector2L OriginNm { get; }

    /// <summary>The paper the drawing sits on: what "zoom to fit" shows.</summary>
    public RectD BoardOutline { get; internal set; } = RectD.Empty;

    public RectD Bounds { get; private set; } = RectD.Empty;

    public IReadOnlyList<LayerGeometry> Layers => _layers;

    public int OwnerCount => _owners.Count;

    public int PrimitiveCount => _layers.Sum(l => l.PrimitiveCount);

    public LayerGeometry? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>A schematic carries no nets yet; highlighting works by selection only.</summary>
    public Net? OwnerNet(int id) => null;

    public bool IsLive(int id) => (uint)id < (uint)_owners.Count && _owners[id] is not null;

    public SchItem Owner(int id) => _owners[id] ?? throw new InvalidOperationException($"Owner {id} is not in the scene.");

    public RectD OwnerBounds(int id) => (uint)id < (uint)_ownerBounds.Count ? _ownerBounds[id] : RectD.Empty;

    public Vector2 ToScene(Vector2D sheetNm)
    {
        var mm = ToSceneMm(sheetNm);
        return new Vector2((float)mm.X, (float)mm.Y);
    }

    public Vector2D ToSceneMm(Vector2D sheetNm) =>
        new((sheetNm.X - OriginNm.X) / Units.NmPerMm, (sheetNm.Y - OriginNm.Y) / Units.NmPerMm);

    public Vector2D ToSheetNm(Vector2D sceneMm) =>
        new((sceneMm.X * Units.NmPerMm) + OriginNm.X, (sceneMm.Y * Units.NmPerMm) + OriginNm.Y);

    internal int AddOwner(SchItem item)
    {
        _owners.Add(item);
        _ownerBounds.Add(RectD.Empty);
        return _owners.Count - 1;
    }

    internal void GrowOwner(int id, RectD bounds)
    {
        if ((uint)id < (uint)_ownerBounds.Count)
        {
            _ownerBounds[id] = _ownerBounds[id].Union(bounds);
        }
    }

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
        _layers.AddRange(_byName.Values.Where(l => l.PrimitiveCount > 0)
            .OrderBy(l => l.DrawOrder).ThenBy(l => l.Name, StringComparer.Ordinal));

        var all = RectD.Empty;
        foreach (var layer in _layers)
        {
            all = all.Union(layer.Bounds);
        }

        Bounds = all;
    }

    private static RectD ComputeBounds(LayerGeometry layer)
    {
        var bounds = RectD.Empty;
        foreach (var line in layer.Lines)
        {
            float h = line.Width / 2;
            bounds = bounds.Union(new RectD(
                Math.Min(line.A.X, line.B.X) - h, Math.Min(line.A.Y, line.B.Y) - h,
                Math.Max(line.A.X, line.B.X) + h, Math.Max(line.A.Y, line.B.Y) + h));
        }

        foreach (var circle in layer.Circles)
        {
            bounds = bounds.Union(new RectD(
                circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius,
                circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius));
        }

        foreach (var polygon in layer.Polygons)
        {
            bounds = bounds.Union(polygon.Bounds);
        }

        return bounds;
    }
}
