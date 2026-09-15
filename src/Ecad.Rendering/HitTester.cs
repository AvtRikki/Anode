using System.Numerics;
using Ecad.KiCad;

namespace Ecad.Rendering;

public static class HitTester
{
    /// <summary>
    /// Owner id of the topmost visible primitive under <paramref name="world"/>, or -1.
    /// Zones are only picked when nothing else is hit, so tracks and pads on top of pours stay selectable.
    /// </summary>
    public static int Pick(BoardScene scene, Vector2 world, float tolerance)
    {
        int zoneHit = -1;
        for (int i = scene.Layers.Count - 1; i >= 0; i--)
        {
            var layer = scene.Layers[i];
            if (!layer.IsVisible || !layer.Bounds.Inflate(tolerance).Contains(world.X, world.Y))
            {
                continue;
            }

            foreach (var circle in layer.Circles)
            {
                if (Vector2.DistanceSquared(circle.Center, world) <= Square(circle.Radius + tolerance))
                {
                    return circle.Owner;
                }
            }

            foreach (var line in layer.Lines)
            {
                if (DistanceToSegmentSquared(world, line.A, line.B) <= Square(line.Width / 2 + tolerance))
                {
                    return line.Owner;
                }
            }

            foreach (var polygon in layer.Polygons)
            {
                if (!Contains(polygon.Points, world))
                {
                    continue;
                }

                if (scene.Owner(polygon.Owner) is not Zone)
                {
                    return polygon.Owner;
                }

                if (zoneHit < 0)
                {
                    zoneHit = polygon.Owner;
                }
            }
        }

        return zoneHit;
    }

    public static float DistanceToSegmentSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0, 1) : 0;
        return Vector2.DistanceSquared(p, a + ab * t);
    }

    /// <summary>Even-odd point-in-polygon test.</summary>
    public static bool Contains(Vector2[] polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float Square(float v) => v * v;
}
