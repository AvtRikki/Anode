using Anode.Geometry;

namespace Anode.Kicad.Editing;

/// <param name="Wire">The wire that is cut.</param>
/// <param name="Pieces">What replaces it, end to end, in the original direction.</param>
public sealed record SchSplit(SchWire Wire, IReadOnlyList<SchWire> Pieces);

/// <summary>
/// Cutting a wire where another one ran into it — the tap. The dot says the two are connected; the cut makes each
/// side an item of its own, so it can be selected, moved or deleted without dragging the whole run with it.
///
/// A point at a wire's own end cuts nothing: there is nothing to cut there.
/// </summary>
public static class SchSplits
{
    /// <summary>Wires that any of <paramref name="points"/> lands inside, each with the pieces that replace it.</summary>
    public static IReadOnlyList<SchSplit> At(Schematic sheet, IReadOnlyList<Vector2L> points)
    {
        var splits = new List<SchSplit>();
        if (points.Count == 0)
        {
            return splits;
        }

        foreach (var wire in sheet.Wires.Where(w => !w.IsBus))
        {
            if (Cut(wire.Points, points) is { Count: > 1 } pieces)
            {
                splits.Add(new SchSplit(wire, [.. pieces.Select(piece => SchNodes.Wire(piece))]));
            }
        }

        return splits;
    }

    /// <summary>The polyline broken at every point that lands inside one of its segments, in order along it.</summary>
    private static List<List<Vector2L>> Cut(Vector2L[] polyline, IReadOnlyList<Vector2L> points)
    {
        var pieces = new List<List<Vector2L>>();
        if (polyline.Length < 2)
        {
            return pieces;
        }

        var current = new List<Vector2L> { polyline[0] };
        for (int i = 1; i < polyline.Length; i++)
        {
            var (a, b) = (polyline[i - 1], polyline[i]);
            foreach (var point in points.Where(p => SchJunctions.IsInside(a, b, p)).Distinct().OrderBy(p => Along(a, p)))
            {
                current.Add(point);
                pieces.Add(current);
                current = [point];
            }

            current.Add(b);
        }

        pieces.Add(current);
        return pieces;
    }

    /// <summary>How far along the segment a point sits, as a squared distance: enough to order several cuts.</summary>
    private static long Along(Vector2L from, Vector2L point)
    {
        long dx = point.X - from.X;
        long dy = point.Y - from.Y;
        return (dx * dx) + (dy * dy);
    }
}
