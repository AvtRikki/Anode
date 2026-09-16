using System.Numerics;
using Anode.Kicad;
using Anode.Render;

namespace Anode.Editing;

/// <summary>Exact primitive-level tests for crossing (touch) selection.</summary>
internal static class SelectionGeometry
{
    /// <summary>Candidates with at least one primitive touching <paramref name="box"/>, in one pass over the scene.</summary>
    public static HashSet<BoardItem> Touching(BoardScene scene, RectD box, IReadOnlyCollection<BoardItem> candidates)
    {
        var hits = new HashSet<BoardItem>(ReferenceEqualityComparer.Instance);
        var ownerToItem = new Dictionary<int, BoardItem>();
        foreach (var item in candidates)
        {
            foreach (int id in scene.OwnersOf(item))
            {
                ownerToItem[id] = item;
            }
        }

        foreach (var layer in scene.Layers)
        {
            if (layer.IsDecoration || !layer.IsVisible || !layer.Bounds.Intersects(box))
            {
                continue;
            }

            foreach (var line in layer.Lines)
            {
                if (Pending(line.Owner, out var item) && SegmentTouches(line.A, line.B, line.Width / 2, box))
                {
                    hits.Add(item);
                }
            }

            foreach (var circle in layer.Circles)
            {
                if (Pending(circle.Owner, out var item) && CircleTouches(circle.Center, circle.Radius, box))
                {
                    hits.Add(item);
                }
            }

            foreach (var polygon in layer.Polygons)
            {
                if (Pending(polygon.Owner, out var item) && PolygonTouches(polygon.Points, box))
                {
                    hits.Add(item);
                }
            }
        }

        return hits;

        bool Pending(int owner, out BoardItem item) =>
            ownerToItem.TryGetValue(owner, out item!) && !hits.Contains(item);
    }

    private static bool CircleTouches(Vector2 center, float radius, RectD box)
    {
        double cx = Math.Clamp(center.X, box.MinX, box.MaxX);
        double cy = Math.Clamp(center.Y, box.MinY, box.MaxY);
        double dx = center.X - cx, dy = center.Y - cy;
        return dx * dx + dy * dy <= (double)radius * radius;
    }

    private static bool SegmentTouches(Vector2 a, Vector2 b, float halfWidth, RectD box)
    {
        var grown = box.Inflate(halfWidth);
        if (grown.Contains(a.X, a.Y) || grown.Contains(b.X, b.Y))
        {
            return true;
        }

        // Liang–Barsky clip of the centre line against the grown box.
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        return Clip(-dx, a.X - grown.MinX) && Clip(dx, grown.MaxX - a.X)
            && Clip(-dy, a.Y - grown.MinY) && Clip(dy, grown.MaxY - a.Y);

        bool Clip(double p, double q)
        {
            if (p == 0)
            {
                return q >= 0;
            }

            double r = q / p;
            if (p < 0)
            {
                if (r > t1)
                {
                    return false;
                }

                t0 = Math.Max(t0, r);
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                t1 = Math.Min(t1, r);
            }

            return true;
        }
    }

    private static bool PolygonTouches(Vector2[] points, RectD box)
    {
        foreach (var p in points)
        {
            if (box.Contains(p.X, p.Y))
            {
                return true;
            }
        }

        // The box may sit entirely inside a large fill.
        if (HitTester.Contains(points, new Vector2((float)box.MinX, (float)box.MinY)))
        {
            return true;
        }

        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            if (SegmentTouches(points[j], points[i], 0, box))
            {
                return true;
            }
        }

        return false;
    }
}
