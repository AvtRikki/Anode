using Anode.Geometry;

namespace Anode.Kicad.Editing;

/// <summary>
/// Where a sheet needs a connection dot. KiCad's rule, and so ours: wires that merely cross are <em>not</em>
/// connected, a dot belongs where a wire end lands inside another wire (a T), and where three or more wire ends meet.
/// Two ends meeting is a corner and needs nothing.
///
/// Buses are left out: a wire meets a bus through an entry, not through a dot.
/// </summary>
public static class SchJunctions
{
    /// <summary>
    /// Points that need a dot once <paramref name="added"/> has been drawn. The added segments are taken into
    /// account as if they were already on the sheet, and points that already carry a dot are left out.
    /// </summary>
    public static IReadOnlyList<Vector2L> Needed(Schematic sheet, IReadOnlyList<(Vector2L From, Vector2L To)> added)
    {
        var segments = Segments(sheet);
        foreach (var (from, to) in added)
        {
            if (from != to)
            {
                segments.Add((from, to));
            }
        }

        var dotted = sheet.Junctions.Select(j => j.Position).ToHashSet();
        var needed = new List<Vector2L>();

        foreach (var (from, to) in added)
        {
            foreach (var point in (ReadOnlySpan<Vector2L>)[from, to])
            {
                if (!dotted.Contains(point) && !needed.Contains(point) && IsNeeded(segments, point))
                {
                    needed.Add(point);
                }
            }
        }

        return needed;
    }

    /// <summary>
    /// Dots that stop meaning anything once <paramref name="removed"/> leaves the sheet: the branch under them is
    /// going, and what is left is a corner, or nothing at all.
    ///
    /// Only dots that sat on a wire being removed are reconsidered. A delete is not an excuse to audit the whole
    /// sheet — a dot the user put somewhere for their own reasons, away from what is being deleted, stays.
    /// </summary>
    public static IReadOnlyList<SchJunction> Stale(Schematic sheet, IReadOnlyList<SchItem> removed)
    {
        var wires = removed.OfType<SchWire>().Where(w => !w.IsBus).ToList();
        if (wires.Count == 0)
        {
            return [];
        }

        var going = removed.ToHashSet();
        var segments = Segments(sheet, going);
        return
        [
            .. sheet.Junctions.Where(dot =>
                !going.Contains(dot)
                && wires.Any(wire => Carries(wire, dot.Position))
                && !IsNeeded(segments, dot.Position)),
        ];
    }

    /// <summary>The point lies on the wire: at one of its ends, or anywhere along it.</summary>
    private static bool Carries(SchWire wire, Vector2L point)
    {
        var points = wire.Points;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i - 1] == point || points[i] == point || IsInside(points[i - 1], points[i], point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every wire segment of the sheet; a wire is a polyline, so each pair of its points is one. Wires in
    /// <paramref name="excluded"/> are counted as already gone.
    /// </summary>
    private static List<(Vector2L A, Vector2L B)> Segments(Schematic sheet, IReadOnlySet<SchItem>? excluded = null)
    {
        var segments = new List<(Vector2L A, Vector2L B)>();
        foreach (var wire in sheet.Wires.Where(w => !w.IsBus && excluded?.Contains(w) != true))
        {
            var points = wire.Points;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i - 1] != points[i])
                {
                    segments.Add((points[i - 1], points[i]));
                }
            }
        }

        return segments;
    }

    private static bool IsNeeded(List<(Vector2L A, Vector2L B)> segments, Vector2L point)
    {
        int ends = 0;
        bool inside = false;
        foreach (var (a, b) in segments)
        {
            if (a == point || b == point)
            {
                ends++;
            }
            else if (IsInside(a, b, point))
            {
                inside = true;
            }
        }

        // A wire ending inside another is a T, and a T connects. A point that merely lies along a wire with nothing
        // ending there is no meeting at all — which is exactly what is left once the branch that made it one is gone.
        // Two ends meeting is a corner; three is a branch, and a branch needs a dot.
        return (inside && ends > 0) || ends >= 3;
    }

    /// <summary>The point lies on the segment, strictly between its ends. Exact, in nanometres.</summary>
    public static bool IsInside(Vector2L a, Vector2L b, Vector2L point)
    {
        long abx = b.X - a.X;
        long aby = b.Y - a.Y;
        long apx = point.X - a.X;
        long apy = point.Y - a.Y;

        if ((abx * apy) - (aby * apx) != 0)
        {
            return false;
        }

        long along = (apx * abx) + (apy * aby);
        long length = (abx * abx) + (aby * aby);
        return along > 0 && along < length;
    }
}
