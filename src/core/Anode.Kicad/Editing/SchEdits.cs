using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Geometric edits of a schematic, written straight into the file's tree — the counterpart of <see cref="BoardEdits"/>.
///
/// A sheet keeps everything in sheet coordinates, including a symbol's fields and a child sheet's pins, so moving a
/// symbol moves its properties with it. Angles are counter-clockwise on screen, as KiCad writes them.
/// </summary>
public static class SchEdits
{
    /// <summary>Items that can be moved as a whole. Sheets move but do not turn, as in KiCad.</summary>
    /// <summary>
    /// Whether the item may be moved at all. An item the designer has locked may not: locking that only greyed a
    /// button would be no lock, so it is answered here, where everything that moves things asks.
    /// </summary>
    public static bool CanTransform(SchItem item) => !item.IsLocked && item switch
    {
        SymbolInstance or SchWire or SchJunction or SchNoConnect or SchLabel or SchText or SchBusEntry or SchSheet => true,
        SchGraphic { Kind: not SchShapeKind.Unsupported } => true,
        _ => false,
    };

    /// <summary>Items that turn about a pivot; the rest only move.</summary>
    public static bool CanRotate(SchItem item) => CanTransform(item) && item is not SchSheet;

    /// <summary>Reference point that snaps to the grid when the item is moved.</summary>
    public static Vector2L Anchor(SchItem item) => item switch
    {
        SchWire wire => wire.Points is { Length: > 0 } points ? points[0] : default,
        SchGraphic { Kind: SchShapeKind.Circle } circle => circle.Center,
        SchGraphic { Kind: SchShapeKind.Polyline or SchShapeKind.Bezier } poly =>
            poly.Points is { Length: > 0 } points ? points[0] : default,
        SchGraphic graphic => graphic.Start,
        _ => item.Position,
    };

    /// <summary>
    /// Rotates <paramref name="item"/> by <paramref name="degrees"/> about <paramref name="pivot"/>, then moves it by
    /// <paramref name="delta"/>. Rotation is counter-clockwise on screen and only ever in 90° steps on a sheet.
    /// </summary>
    public static void Transform(SchItem item, Vector2L pivot, double degrees, Vector2L delta)
    {
        if (!CanTransform(item))
        {
            throw new InvalidOperationException($"{item.GetType().Name} cannot be moved.");
        }

        double turn = KiCadNumber.Normalize360(degrees);
        bool rotates = turn != 0;
        if (rotates && !CanRotate(item))
        {
            throw new NotSupportedException($"{item.GetType().Name} cannot be rotated.");
        }

        var t = Transform2D.Translation(-pivot.X, -pivot.Y)
            .Then(KiCadTransforms.Rotation(degrees))
            .Then(Transform2D.Translation(pivot.X + delta.X, pivot.Y + delta.Y));
        Vector2L Map(Vector2L p) => t.ApplyRounded(p);

        var node = item.Node;
        switch (item)
        {
            case SchWire:
                node.Find("pts")?.MapAllPoints(Map);
                break;

            case SchJunction or SchNoConnect:
                node.MapChildPoint("at", Map);
                break;

            case SchLabel or SchText:
                MoveAt(node, Map, turn, rotates);
                break;

            case SchBusEntry:
                node.MapChildPoint("at", Map);

                // The size is a delta from the entry's own point, so it turns but never moves.
                if (rotates && node.Find("size") is { } size)
                {
                    var spun = KiCadTransforms.Rotation(degrees).ApplyRounded(size.Point(1));
                    size.SetPoint(spun);
                }

                break;

            case SchGraphic:
                foreach (string head in (ReadOnlySpan<string>)["start", "mid", "end", "center"])
                {
                    node.MapChildPoint(head, Map);
                }

                node.Find("pts")?.MapAllPoints(Map);
                break;

            case SymbolInstance symbol:
                TransformSymbol(symbol, Map, turn, rotates);
                break;

            case SchSheet sheet:
                TransformSheet(sheet, Map);
                break;
        }
    }

    /// <summary>
    /// Mirrors a symbol across the axis through <paramref name="pivot"/>. KiCad keeps this as <c>(mirror x|y)</c> on
    /// the symbol rather than as reflected geometry, so mirroring twice returns the original file.
    /// </summary>
    public static void Mirror(SchItem item, Vector2L pivot, bool horizontal)
    {
        var node = item.Node;
        Vector2L Map(Vector2L p) => horizontal
            ? new Vector2L((2 * pivot.X) - p.X, p.Y)
            : new Vector2L(p.X, (2 * pivot.Y) - p.Y);

        switch (item)
        {
            case SymbolInstance symbol:
                node.MapChildPoint("at", Map);
                foreach (var property in node.Lists().Where(l => l.Head == "property"))
                {
                    property.MapChildPoint("at", Map);
                }

                SetMirror(symbol, horizontal);
                break;

            case SchWire:
                node.Find("pts")?.MapAllPoints(Map);
                break;

            case SchGraphic:
                foreach (string head in (ReadOnlySpan<string>)["start", "mid", "end", "center"])
                {
                    node.MapChildPoint(head, Map);
                }

                node.Find("pts")?.MapAllPoints(Map);
                break;

            case SchJunction or SchNoConnect or SchLabel or SchText or SchBusEntry or SchSheet:
                node.MapChildPoint("at", Map);
                break;
        }
    }

    /// <summary>A symbol's fields live in sheet coordinates, so they travel with it.</summary>
    private static void TransformSymbol(SymbolInstance symbol, Func<Vector2L, Vector2L> map, double turn, bool rotates)
    {
        var node = symbol.Node;
        MoveAt(node, map, turn, rotates);

        foreach (var property in node.Lists().Where(l => l.Head == "property"))
        {
            MoveAt(property, map, turn, rotates);
        }
    }

    /// <summary>A child sheet carries its pins and fields; its size stays as it is.</summary>
    private static void TransformSheet(SchSheet sheet, Func<Vector2L, Vector2L> map)
    {
        var node = sheet.Node;
        node.MapChildPoint("at", map);

        foreach (var child in node.Lists().Where(l => l.Head is "property" or "pin"))
        {
            child.MapChildPoint("at", map);
        }
    }

    private static void MoveAt(SList node, Func<Vector2L, Vector2L> map, double turn, bool rotates)
    {
        if (node.Find("at") is not { } at)
        {
            return;
        }

        at.MapPoint(map);
        if (rotates)
        {
            at.SetAngle(3, KiCadNumber.Normalize360(at.Double(3) + turn), omitWhenZero: false);
        }
    }

    /// <summary>Flips <c>(mirror …)</c>: adding it when absent, dropping it when the symbol is upright again.</summary>
    private static void SetMirror(SymbolInstance symbol, bool horizontal)
    {
        var node = symbol.Node;
        string axis = horizontal ? "y" : "x";
        if (node.Find("mirror") is { } mirror)
        {
            if (string.Equals(mirror.Str(1), axis, StringComparison.Ordinal))
            {
                node.Remove(mirror);
                return;
            }

            mirror.AtomAt(1)?.SetSymbol(axis);
            return;
        }

        node.Insert(MirrorPosition(node), new SList("mirror", SAtom.Symbol(axis)));
    }

    /// <summary>KiCad writes <c>(mirror …)</c> right after <c>(at …)</c>.</summary>
    private static int MirrorPosition(SList node) =>
        node.Find("at") is { } at && node.IndexOf(at) is >= 0 and var index ? index + 1 : node.Count;
}
