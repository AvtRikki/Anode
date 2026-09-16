using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

public enum PadType
{
    ThroughHole,
    Smd,
    Connect,
    NonPlatedThroughHole,
    Unknown,
}

public enum PadShape
{
    Circle,
    Rect,
    Oval,
    RoundRect,
    Trapezoid,
    Custom,
    Unknown,
}

[Flags]
public enum PadChamfers
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 4,
    BottomRight = 8,
}

/// <param name="Size">Hole size; X equals Y for round holes.</param>
/// <param name="Offset">Offset of the pad shape relative to the hole, in pad frame.</param>
public readonly record struct PadDrill(Vector2L Size, bool IsOval, Vector2L Offset);

public sealed class Pad : BoardItem
{
    internal Pad(SList node, Board board, Footprint footprint)
        : base(node, board)
    {
        Footprint = footprint;
    }

    public Footprint Footprint { get; }

    public override BoardItem TopLevel => Footprint;

    public string Number => Node.Str(1) ?? string.Empty;

    public PadType Type => Node.AtomAt(2)?.Raw switch
    {
        "thru_hole" => PadType.ThroughHole,
        "smd" => PadType.Smd,
        "connect" => PadType.Connect,
        "np_thru_hole" => PadType.NonPlatedThroughHole,
        _ => PadType.Unknown,
    };

    public PadShape Shape => ParseShape(Node.AtomAt(3)?.Raw);

    /// <summary>Shape the custom-pad primitives are merged with.</summary>
    public PadShape AnchorShape => ParseShape(Node.Find("options")?.ChildString("anchor") ?? "rect");

    /// <summary>Position in the footprint frame.</summary>
    public Vector2L LocalPosition => Node.Find("at") is { } at ? at.Point() : default;

    /// <summary>Board-frame orientation in degrees; KiCad stores it absolute, not footprint-relative.</summary>
    public double Orientation => Node.Find("at") is { Count: > 3 } at ? at.Double(3) : 0;

    /// <summary>Size in the footprint frame (width, height).</summary>
    public Vector2L Size => Node.Find("size") is { Count: > 2 } s ? s.Point() : default;

    public IReadOnlyList<string> LayerNames => Node.LayerNames();

    public Net? Net => Board.Nets.Resolve(Node.Find("net"));

    public double RoundRectRatio => Node.ChildDouble("roundrect_rratio") ?? 0.25;

    public double ChamferRatio => Node.ChildDouble("chamfer_ratio") ?? 0;

    public PadChamfers Chamfers
    {
        get
        {
            var result = PadChamfers.None;
            if (Node.Find("chamfer") is { } c)
            {
                if (c.HasSymbol("top_left")) result |= PadChamfers.TopLeft;
                if (c.HasSymbol("top_right")) result |= PadChamfers.TopRight;
                if (c.HasSymbol("bottom_left")) result |= PadChamfers.BottomLeft;
                if (c.HasSymbol("bottom_right")) result |= PadChamfers.BottomRight;
            }

            return result;
        }
    }

    /// <summary>Trapezoid deformation <c>(rect_delta dx dy)</c>.</summary>
    public Vector2L RectDelta => Node.Find("rect_delta") is { Count: > 2 } d ? d.Point() : default;

    public PadDrill? Drill
    {
        get
        {
            if (Node.Find("drill") is not { } drill)
            {
                return null;
            }

            bool oval = false;
            var sizes = new List<long>(2);
            for (int i = 1; i < drill.Count; i++)
            {
                if (drill[i] is SAtom a)
                {
                    if (a.IsSymbol("oval"))
                    {
                        oval = true;
                    }
                    else if (a.TryGetDouble(out double mm))
                    {
                        sizes.Add(Units.MmToNm(mm));
                    }
                }
            }

            if (sizes.Count == 0)
            {
                return null;
            }

            long w = sizes[0], h = sizes.Count > 1 ? sizes[1] : sizes[0];
            return new PadDrill(new Vector2L(w, h), oval, drill.ChildPoint("offset") ?? default);
        }
    }

    public Vector2L BoardPosition => Footprint.Transform.ApplyRounded(LocalPosition);

    /// <summary>
    /// Pad frame (centre at the origin, unrotated) to board frame. The footprint scale applies to the pad size,
    /// then the absolute pad orientation, then the board position.
    /// </summary>
    public override Transform2D ToBoard
    {
        get
        {
            var fp = Footprint;
            return KiCadTransforms.Placement(BoardPosition, Orientation, fp.ScaleX, fp.ScaleY);
        }
    }

    /// <summary>Custom pad primitives, in pad frame.</summary>
    public IEnumerable<Shape> Primitives
    {
        get
        {
            if (Node.Find("primitives") is not { } primitives)
            {
                yield break;
            }

            var toBoard = ToBoard;
            foreach (var child in primitives.Lists())
            {
                if (child.Head?.StartsWith("gr_", StringComparison.Ordinal) == true)
                {
                    yield return new Shape(child, Board, toBoard, Footprint);
                }
            }
        }
    }

    public Box2L Bounds
    {
        get
        {
            var size = Size;
            var t = ToBoard;
            var box = Box2L.Empty;
            foreach (var corner in (ReadOnlySpan<Vector2D>)[new(-size.X / 2.0, -size.Y / 2.0), new(size.X / 2.0, -size.Y / 2.0), new(size.X / 2.0, size.Y / 2.0), new(-size.X / 2.0, size.Y / 2.0)])
            {
                box = box.Union(t.Apply(corner).Round());
            }

            foreach (var primitive in Primitives)
            {
                box = box.Union(primitive.Bounds);
            }

            return box;
        }
    }

    private static PadShape ParseShape(string? raw) => raw switch
    {
        "circle" => PadShape.Circle,
        "rect" => PadShape.Rect,
        "oval" => PadShape.Oval,
        "roundrect" => PadShape.RoundRect,
        "trapezoid" => PadShape.Trapezoid,
        "custom" => PadShape.Custom,
        _ => PadShape.Unknown,
    };
}
