using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>A fractured fill polygon: holes are already bridged into a single outline.</summary>
public readonly record struct FilledPolygon(string LayerName, Vector2L[] Points);

public sealed class Zone : BoardItem
{
    private readonly Footprint? _footprint;

    internal Zone(SList node, Board board, Footprint? footprint)
        : base(node, board)
    {
        _footprint = footprint;
    }

    public Footprint? Footprint => _footprint;

    public override BoardItem TopLevel => (BoardItem?)_footprint ?? this;

    public string? Name => Node.ChildString("name");

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));

    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    /// <summary>Keepout / rule area rather than a copper pour.</summary>
    public bool IsRuleArea => Node.Find("keepout") is not null;

    /// <summary>Outline polygons in board frame.</summary>
    public IEnumerable<Vector2L[]> Outlines
    {
        get
        {
            var t = OutlineToBoard;
            foreach (var polygon in Node.FindAll("polygon"))
            {
                var points = polygon.Find("pts")?.Points() ?? [];
                yield return t == Transform2D.Identity ? points : Array.ConvertAll(points, t.ApplyRounded);
            }
        }
    }

    /// <summary>Copper fill as last computed by KiCad, in board frame.</summary>
    public IEnumerable<FilledPolygon> FilledPolygons
    {
        get
        {
            string? defaultLayer = LayerNames.Count == 1 ? LayerNames[0] : null;
            foreach (var filled in Node.FindAll("filled_polygon"))
            {
                string layer = filled.ChildString("layer") ?? defaultLayer ?? string.Empty;
                yield return new FilledPolygon(layer, filled.Find("pts")?.Points() ?? []);
            }
        }
    }

    /// <summary>Footprint zone outlines moved from board frame to library frame in <see cref="KiCadFormat.FootprintAffineTransform"/>.</summary>
    private Transform2D OutlineToBoard =>
        _footprint is not null && Board.Version >= KiCadFormat.FootprintAffineTransform
            ? _footprint.Transform
            : Transform2D.Identity;
}
