using Anode.Geometry;

namespace Anode.Kicad.Editing;

/// <summary>
/// Moving, turning and mirroring what a library symbol is made of — pins, shapes and fields — in the library's own
/// coordinates, where Y runs up. The caller hands over the map a point goes through; this knows which of each item's
/// points to put through it, and what the turn or the mirror does to what is not a point: a pin's direction, a field's
/// reading angle. The counterpart of <see cref="SchEdits"/>, which does the same for what stands on a sheet.
/// </summary>
public static class SymbolEdits
{
    /// <summary>What of a symbol can be moved: its pins, its shapes, its fields — none that is held in place.</summary>
    public static bool CanTransform(SchItem item) => !item.IsLocked && item switch
    {
        SchPin or SchField => true,
        SchGraphic { Kind: not SchShapeKind.Unsupported } => true,
        _ => false,
    };

    /// <summary>
    /// What of a symbol can be deleted: pins and shapes. Fields cannot — reference, value and the rest are the
    /// symbol's own, and KiCad keeps them however they are hidden.
    /// </summary>
    public static bool CanDelete(SchItem item) => CanTransform(item) && item is not SchField;

    /// <summary>The point of an item that lands on the grid when it is moved, in the library's coordinates.</summary>
    public static Vector2L Anchor(SchItem item) => item is SchGraphic graphic ? SchEdits.Anchor(graphic) : item.Position;

    /// <summary>
    /// Puts every point of <paramref name="item"/> through <paramref name="map"/>, and turns what has a direction by
    /// <paramref name="degrees"/>, counter-clockwise as the library measures angles: a pin points that much further
    /// round, and a field that turns by a quarter reads the other way — horizontal becomes vertical and back, which
    /// is how KiCad turns a field, never upside down.
    /// </summary>
    public static void Transform(SchItem item, Func<Vector2L, Vector2L> map, double degrees)
    {
        var node = item.Node;
        double turn = KiCadNumber.Normalize360(degrees);
        switch (item)
        {
            case SchPin:
                if (node.Find("at") is { } at)
                {
                    at.MapPoint(map);
                    if (turn != 0)
                    {
                        at.SetAngle(3, KiCadNumber.Normalize360(item.Angle + turn), omitWhenZero: false);
                    }
                }

                break;

            case SchField:
                if (node.Find("at") is { } place)
                {
                    place.MapPoint(map);
                    if (Math.Abs((turn % 180) - 90) < 1e-6)
                    {
                        place.SetAngle(3, Math.Abs(item.Angle % 180) < 1e-6 ? 90 : 0, omitWhenZero: false);
                    }
                }

                break;

            case SchGraphic:
                MapShape(node, map);
                break;
        }

        item.AfterRestore();
    }

    /// <summary>
    /// Mirrors an item through <paramref name="map"/>: left for right when <paramref name="horizontal"/>, else top
    /// for bottom. A pin then points the mirrored way; a field keeps reading as it did, only its place moves.
    /// </summary>
    public static void Mirror(SchItem item, Func<Vector2L, Vector2L> map, bool horizontal)
    {
        var node = item.Node;
        switch (item)
        {
            case SchPin:
                if (node.Find("at") is { } at)
                {
                    at.MapPoint(map);
                    double mirrored = horizontal ? 180 - item.Angle : -item.Angle;
                    at.SetAngle(3, KiCadNumber.Normalize360(mirrored), omitWhenZero: false);
                }

                break;

            case SchField:
                node.MapChildPoint("at", map);
                break;

            case SchGraphic:
                MapShape(node, map);
                break;
        }

        item.AfterRestore();
    }

    private static void MapShape(Sexpr.SList node, Func<Vector2L, Vector2L> map)
    {
        foreach (string head in (ReadOnlySpan<string>)["start", "mid", "end", "center"])
        {
            node.MapChildPoint(head, map);
        }

        node.Find("pts")?.MapAllPoints(map);
    }
}
