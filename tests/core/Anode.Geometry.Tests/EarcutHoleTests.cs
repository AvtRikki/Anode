using System.Numerics;

namespace Anode.Geometry.Tests;

/// <summary>
/// Outlines with holes — the counters of a letter. Measured by area: the triangles must cover the outline less its
/// holes exactly, whichever way round each ring is written.
/// </summary>
public class EarcutHoleTests
{
    private static Vector2[] Square(float x, float y, float size, bool clockwise) =>
        clockwise
            ? [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)]
            : [new(x, y), new(x, y + size), new(x + size, y + size), new(x + size, y)];

    private static double Covered(Vector2[] points, int[] holeStarts)
    {
        var triangles = new List<int>();
        Earcut.Triangulate(points, holeStarts, triangles);
        double area = 0;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
            area += Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) / 2;
        }

        return area;
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void A_hole_is_left_open_whichever_way_the_rings_run(bool outerClockwise, bool holeClockwise)
    {
        Vector2[] points = [.. Square(0, 0, 10, outerClockwise), .. Square(3, 3, 4, holeClockwise)];

        Assert.Equal(84, Covered(points, [4]), 3);
    }

    [Fact]
    public void Two_holes_are_both_left_open()
    {
        Vector2[] points = [.. Square(0, 0, 10, true), .. Square(1, 1, 3, false), .. Square(6, 6, 3, false)];

        Assert.Equal(100 - 9 - 9, Covered(points, [4, 8]), 3);
    }

    [Fact]
    public void Without_holes_nothing_changes()
    {
        Assert.Equal(100, Covered(Square(0, 0, 10, true), []), 3);
    }
}
