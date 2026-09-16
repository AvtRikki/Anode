using System.Numerics;

namespace Anode.Geometry.Tests;

public class EarcutTests
{
    private static double PolygonArea(ReadOnlySpan<Vector2> points)
    {
        double sum = 0;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            sum += ((double)points[j].X + points[i].X) * ((double)points[j].Y - points[i].Y);
        }

        return Math.Abs(sum / 2);
    }

    private static double TrianglesArea(ReadOnlySpan<Vector2> points, List<int> tris)
    {
        double sum = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            var a = points[tris[t]];
            var b = points[tris[t + 1]];
            var c = points[tris[t + 2]];
            sum += Math.Abs(((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)c.X - a.X) * ((double)b.Y - a.Y)) / 2;
        }

        return sum;
    }

    private static void AssertExact(Vector2[] polygon, int expectedTriangles = -1)
    {
        var tris = Earcut.Triangulate(polygon);
        Assert.Equal(0, tris.Count % 3);
        if (expectedTriangles >= 0)
        {
            Assert.Equal(expectedTriangles, tris.Count / 3);
        }

        double expected = PolygonArea(polygon);
        Assert.Equal(expected, TrianglesArea(polygon, tris), expected * 1e-6 + 1e-9);
    }

    [Fact]
    public void Square_gives_two_triangles()
    {
        AssertExact([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], 2);
    }

    [Fact]
    public void Clockwise_and_counter_clockwise_both_work()
    {
        Vector2[] ccw = [new(0, 0), new(4, 0), new(4, 3), new(2, 1), new(0, 3)];
        AssertExact(ccw, 3);
        AssertExact([.. ccw.Reverse()], 3);
    }

    [Fact]
    public void Fractured_polygon_with_bridged_hole()
    {
        // 10×10 square with a 4×4 hole joined to the outline by a zero-width bridge, as KiCad writes zone fills.
        Vector2[] polygon =
        [
            new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 5),
            new(3, 5), new(3, 7), new(7, 7), new(7, 3), new(3, 3), new(3, 5),
            new(0, 5),
        ];

        var tris = Earcut.Triangulate(polygon);
        Assert.Equal(100 - 16, TrianglesArea(polygon, tris), 1e-6);
    }

    [Fact]
    public void Large_star_is_triangulated_with_hashing()
    {
        const int n = 5000;
        var star = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * i / n;
            double r = i % 2 == 0 ? 100 : 60;
            star[i] = new Vector2((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)));
        }

        AssertExact(star, n - 2);
    }

    [Fact]
    public void Degenerate_input_yields_nothing()
    {
        Assert.Empty(Earcut.Triangulate([new(0, 0), new(1, 1)]));
        Assert.Empty(Earcut.Triangulate([new(0, 0), new(1, 1), new(2, 2)]));
    }
}
