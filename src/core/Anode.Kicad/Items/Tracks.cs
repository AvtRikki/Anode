using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

public sealed class Segment(SList node, Board board) : BoardItem(node, board)
{
    public Vector2L Start => Node.ChildPoint("start") ?? default;

    public Vector2L End => Node.ChildPoint("end") ?? default;

    public long Width => Node.ChildNm("width") ?? 0;

    /// <summary>Copper layer, optionally followed by a solder-mask layer (since 20241007).</summary>
    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    public string LayerName => LayerNames.Count > 0 ? LayerNames[0] : string.Empty;

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));
}

public sealed class TrackArc(SList node, Board board) : BoardItem(node, board)
{
    public Vector2L Start => Node.ChildPoint("start") ?? default;

    public Vector2L Mid => Node.ChildPoint("mid") ?? default;

    public Vector2L End => Node.ChildPoint("end") ?? default;

    public long Width => Node.ChildNm("width") ?? 0;

    /// <summary>Copper layer, optionally followed by a solder-mask layer (since 20241007).</summary>
    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    public string LayerName => LayerNames.Count > 0 ? LayerNames[0] : string.Empty;

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));

    public Arc? Geometry => ArcMath.FromStartMidEnd(Start.ToDouble(), Mid.ToDouble(), End.ToDouble());
}

public sealed class Via(SList node, Board board) : BoardItem(node, board)
{
    public Vector2L Position => Node.ChildPoint("at") ?? default;

    public long Size => Node.ChildNm("size") ?? 0;

    public long Drill => Node.ChildNm("drill") ?? 0;

    /// <summary>The two outermost copper layers the via connects.</summary>
    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    /// <summary>through, blind, buried or micro.</summary>
    public string ViaType
    {
        get
        {
            if (Node.ChildString("type") is { } explicitType)
            {
                return explicitType;
            }

            foreach (var kind in (ReadOnlySpan<string>)["micro", "blind", "buried"])
            {
                if (Node.HasSymbol(kind))
                {
                    return kind;
                }
            }

            return "through";
        }
    }

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));
}
