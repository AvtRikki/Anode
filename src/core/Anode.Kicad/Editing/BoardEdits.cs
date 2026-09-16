using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>Geometric edits written straight into the file's tree.</summary>
public static class BoardEdits
{
    /// <summary>Items that can be moved and rotated as a whole.</summary>
    public static bool CanTransform(BoardItem item) => item switch
    {
        Footprint or Segment or TrackArc or Via => true,
        Shape { Footprint: null, Kind: not ShapeKind.Unsupported } => true,
        Text { Footprint: null } => true,
        Zone { Footprint: null } => true,
        _ => false,
    };

    /// <summary>Reference point that snaps to the grid when the item is moved, as KiCad uses.</summary>
    public static Vector2L Anchor(BoardItem item) => item switch
    {
        Footprint fp => fp.Position,
        Via via => via.Position,
        Segment segment => segment.Start,
        TrackArc arc => arc.Start,
        Text text => text.BoardPosition,
        Shape { Kind: ShapeKind.Circle } circle => circle.Center,
        Shape { Kind: ShapeKind.Polygon or ShapeKind.Bezier } poly => poly.Points is { Length: > 0 } p ? p[0] : default,
        Shape shape => shape.Start,
        Zone zone => zone.Outlines.FirstOrDefault() is { Length: > 0 } outline ? outline[0] : default,
        _ => default,
    };

    /// <summary>
    /// Rotates <paramref name="item"/> by <paramref name="degrees"/> (counter-clockwise on screen) about
    /// <paramref name="pivot"/>, then moves it by <paramref name="delta"/>.
    /// </summary>
    public static void Transform(BoardItem item, Vector2L pivot, double degrees, Vector2L delta)
    {
        if (!CanTransform(item))
        {
            throw new InvalidOperationException($"{item.GetType().Name} cannot be moved or rotated.");
        }

        bool rotates = KiCadNumber.Normalize360(degrees) != 0;
        if (rotates && item is Shape { Kind: ShapeKind.Rect } && Math.Abs(Math.IEEERemainder(degrees, 90)) > 1e-9)
        {
            throw new NotSupportedException("Rectangles can only be rotated in 90° steps.");
        }

        var t = Transform2D.Translation(-pivot.X, -pivot.Y)
            .Then(KiCadTransforms.Rotation(degrees))
            .Then(Transform2D.Translation(pivot.X + delta.X, pivot.Y + delta.Y));
        Vector2L Map(Vector2L p) => t.ApplyRounded(p);

        var node = item.Node;
        switch (item)
        {
            case Segment:
                node.MapChildPoint("start", Map);
                node.MapChildPoint("end", Map);
                break;

            case TrackArc:
                node.MapChildPoint("start", Map);
                node.MapChildPoint("mid", Map);
                node.MapChildPoint("end", Map);
                break;

            case Via:
                node.MapChildPoint("at", Map);
                break;

            case Shape:
                foreach (string head in (ReadOnlySpan<string>)["start", "mid", "end", "center"])
                {
                    node.MapChildPoint(head, Map);
                }

                node.Find("pts")?.MapAllPoints(Map);
                break;

            case Text:
                if (node.Find("at") is { } at)
                {
                    at.MapPoint(Map);
                    if (rotates)
                    {
                        at.SetAngle(3, KiCadNumber.Normalize360(at.Double(3) + degrees), omitWhenZero: false);
                    }
                }

                break;

            case Zone:
                node.MapAllPoints(Map);
                break;

            case Footprint footprint:
                TransformFootprint(footprint, Map, degrees, rotates);
                break;
        }
    }

    private static void TransformFootprint(Footprint footprint, Func<Vector2L, Vector2L> map, double degrees, bool rotates)
    {
        var node = footprint.Node;

        // Children keep footprint-relative coordinates; only the placement moves.
        if (node.Find("transform") is { } transform)
        {
            transform.MapChildPoint("translate", map);
            if (rotates && transform.Find("rotate") is { } rotate)
            {
                rotate.SetAngle(1, KiCadNumber.Normalize180(rotate.Double(1) + degrees), omitWhenZero: false);
            }
        }
        else if (node.Find("at") is { } at)
        {
            at.MapPoint(map);
            if (rotates)
            {
                at.SetAngle(3, KiCadNumber.Normalize180(at.Double(3) + degrees), omitWhenZero: true);
            }
        }

        // Pad and text angles are stored in board frame, so they turn with the footprint.
        if (rotates)
        {
            foreach (var child in node.Lists())
            {
                switch (child.Head)
                {
                    case "pad" when child.Find("at") is { } padAt:
                        padAt.SetAngle(3, KiCadNumber.Normalize360(padAt.Double(3) + degrees), omitWhenZero: true);
                        break;
                    case "property" or "fp_text" when child.Find("at") is { } textAt:
                        textAt.SetAngle(3, KiCadNumber.Normalize360(textAt.Double(3) + degrees), omitWhenZero: false);
                        break;
                }
            }
        }

        // Footprint zone outlines were stored in board frame before the affine-transform format.
        if (footprint.Board.Version < KiCadFormat.FootprintAffineTransform)
        {
            foreach (var zone in node.FindAll("zone"))
            {
                zone.MapAllPoints(map);
            }
        }

        footprint.Refresh();
    }
}
