using System.Numerics;

namespace Anode.Render;

/// <summary>
/// Chains loose outline segments into closed rings. KiCad stores a board outline as separate lines and arcs in any
/// order and direction; joining them by their endpoints gives the polygons needed to fill the board body.
/// </summary>
public static class OutlineLoops
{
    /// <summary>Owner of primitives that belong to no board item: decoration only, never picked.</summary>
    public const int NoOwner = -1;

    /// <summary>Endpoints closer than this (in scene millimetres) are the same point.</summary>
    private const float Tolerance = 0.02f;

    public static IReadOnlyList<Vector2[]> Build(IReadOnlyList<LinePrim> lines)
    {
        // Segments by rounded endpoint, so chaining is a lookup instead of a scan.
        var byPoint = new Dictionary<(int X, int Y), List<int>>();
        var used = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            Add(Key(lines[i].A), i);
            Add(Key(lines[i].B), i);
        }

        var loops = new List<Vector2[]>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            used[i] = true;
            var start = lines[i].A;
            var points = new List<Vector2> { start, lines[i].B };
            var tip = lines[i].B;

            while (Next(tip, out int next))
            {
                used[next] = true;
                tip = Near(lines[next].A, tip) ? lines[next].B : lines[next].A;
                if (Near(tip, start))
                {
                    break;
                }

                points.Add(tip);
            }

            // Only closed rings are a board body; an open chain is a drawing, not an outline.
            if (points.Count >= 3 && Near(tip, start) && Math.Abs(Area(points)) > Tolerance)
            {
                loops.Add([.. points]);
            }
        }

        // The outer ring first: inner cut-outs are drawn over it by the layers above anyway.
        loops.Sort((a, b) => Math.Abs(Area(b)).CompareTo(Math.Abs(Area(a))));
        return loops;

        void Add((int X, int Y) key, int index)
        {
            if (!byPoint.TryGetValue(key, out var list))
            {
                list = [];
                byPoint[key] = list;
            }

            list.Add(index);
        }

        bool Next(Vector2 point, out int index)
        {
            foreach (var key in Neighbourhood(point))
            {
                if (!byPoint.TryGetValue(key, out var candidates))
                {
                    continue;
                }

                foreach (int candidate in candidates)
                {
                    if (!used[candidate] && (Near(lines[candidate].A, point) || Near(lines[candidate].B, point)))
                    {
                        index = candidate;
                        return true;
                    }
                }
            }

            index = -1;
            return false;
        }
    }

    private static (int X, int Y) Key(Vector2 p) =>
        ((int)MathF.Round(p.X / Tolerance), (int)MathF.Round(p.Y / Tolerance));

    /// <summary>The rounded key plus its neighbours: two endpoints within the tolerance can land in adjacent cells.</summary>
    private static IEnumerable<(int X, int Y)> Neighbourhood(Vector2 p)
    {
        var (x, y) = Key(p);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                yield return (x + dx, y + dy);
            }
        }
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= Tolerance * Tolerance;

    private static float Area(IReadOnlyList<Vector2> points)
    {
        float sum = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            sum += (points[j].X * points[i].Y) - (points[i].X * points[j].Y);
        }

        return sum / 2;
    }
}
