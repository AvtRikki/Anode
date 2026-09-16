using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

public enum ShapeKind
{
    Line,
    Rect,
    Circle,
    Arc,
    Polygon,
    Bezier,
    Unsupported,
}

/// <summary>
/// Graphic primitive: <c>gr_*</c> on the board, <c>fp_*</c> inside a footprint, or a pad primitive.
/// Coordinates are in the owner's frame; use <see cref="BoardItem.ToBoard"/>.
/// </summary>
public sealed class Shape : BoardItem
{
    private readonly Transform2D _toBoard;

    internal Shape(SList node, Board board, Transform2D toBoard, Footprint? footprint = null)
        : base(node, board)
    {
        _toBoard = toBoard;
        Footprint = footprint;
        Kind = KindFromHead(node.Head);
    }

    /// <summary>Owning footprint for fp_* graphics and pad primitives; null for board graphics.</summary>
    public Footprint? Footprint { get; }

    public override BoardItem TopLevel => (BoardItem?)Footprint ?? this;

    public ShapeKind Kind { get; }

    public override Transform2D ToBoard => _toBoard;

    public Vector2L Start => Node.ChildPoint("start") ?? default;

    public Vector2L Mid => Node.ChildPoint("mid") ?? default;

    public Vector2L End => Node.ChildPoint("end") ?? default;

    /// <summary>Circle centre; for circles <see cref="End"/> is a point on the circumference.</summary>
    public Vector2L Center => Node.ChildPoint("center") ?? default;

    public long Radius => (long)Math.Round((End - Center).Length);

    public long CornerRadius => Node.ChildNm("radius") ?? 0;

    public long StrokeWidth => Node.Find("stroke")?.ChildNm("width") ?? Node.ChildNm("width") ?? 0;

    public bool IsFilled => Node.Find("fill") is { } fill && fill.AtomAt(1)?.Raw is "yes" or "solid" or "hatch" or "reverse_hatch" or "cross_hatch";

    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));

    /// <summary>Polygon outline (arcs tessellated) or the four Bézier control points.</summary>
    public Vector2L[] Points => Node.Find("pts")?.Points() ?? [];

    public Arc? ArcGeometry => Kind == ShapeKind.Arc
        ? ArcMath.FromStartMidEnd(Start.ToDouble(), Mid.ToDouble(), End.ToDouble())
        : null;

    /// <summary>Board-space bounding box including stroke width.</summary>
    public Box2L Bounds
    {
        get
        {
            var box = Box2L.Empty;
            var t = ToBoard;
            void Add(Vector2D p) => box = box.Union(t.Apply(p).Round());

            switch (Kind)
            {
                case ShapeKind.Line:
                    Add(Start.ToDouble());
                    Add(End.ToDouble());
                    break;
                case ShapeKind.Rect:
                    Add(Start.ToDouble());
                    Add(End.ToDouble());
                    Add(new Vector2D(Start.X, End.Y));
                    Add(new Vector2D(End.X, Start.Y));
                    break;
                case ShapeKind.Circle:
                    var c = Center.ToDouble();
                    double r = Radius;
                    Add(c + new Vector2D(-r, -r));
                    Add(c + new Vector2D(r, r));
                    Add(c + new Vector2D(-r, r));
                    Add(c + new Vector2D(r, -r));
                    break;
                case ShapeKind.Arc:
                    if (ArcGeometry is { } arc)
                    {
                        foreach (var p in ArcMath.Tessellate(arc))
                        {
                            Add(p);
                        }
                    }

                    break;
                case ShapeKind.Polygon:
                case ShapeKind.Bezier:
                    foreach (var p in Points)
                    {
                        Add(p.ToDouble());
                    }

                    break;
            }

            return box.Inflate((long)(StrokeWidth * t.ScaleFactor / 2));
        }
    }

    private static ShapeKind KindFromHead(string? head)
    {
        if (head is null)
        {
            return ShapeKind.Unsupported;
        }

        int underscore = head.IndexOf('_');
        return (underscore >= 0 ? head[(underscore + 1)..] : head) switch
        {
            "line" => ShapeKind.Line,
            "rect" => ShapeKind.Rect,
            "circle" => ShapeKind.Circle,
            "arc" => ShapeKind.Arc,
            "poly" => ShapeKind.Polygon,
            "curve" => ShapeKind.Bezier,
            _ => ShapeKind.Unsupported,
        };
    }

    internal static bool IsShapeHead(string? head) =>
        head is "gr_line" or "gr_rect" or "gr_circle" or "gr_arc" or "gr_poly" or "gr_curve" or "gr_ellipse" or "gr_ellipse_arc"
            or "fp_line" or "fp_rect" or "fp_circle" or "fp_arc" or "fp_poly" or "fp_curve" or "fp_ellipse" or "fp_ellipse_arc";
}
