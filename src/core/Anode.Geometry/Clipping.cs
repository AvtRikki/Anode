using Clipper2Lib;

namespace Anode.Geometry;

/// <summary>A filled region: an outline and the holes cut into it.</summary>
public sealed record PolygonWithHoles(Vector2D[] Outline, IReadOnlyList<Vector2D[]> Holes);

/// <summary>
/// Boolean work on filled regions, over Clipper2 in whole nanometres — KiCad's own units, so a region cut here lands
/// on the same grid as KiCad's.
/// </summary>
public static class Clipping
{
    /// <summary>
    /// Knocks text out of a box, as KiCad draws knockout text: the letters — strokes of <paramref name="strokeWidth"/>
    /// with round ends, and filled shapes — are gathered, the box around them is taken in the text's own frame
    /// (turned by <paramref name="angleDegrees"/> about <paramref name="pivot"/>) and grown by
    /// <paramref name="margin"/>, and the letters are cut out of it. Counters come back as islands of their own.
    /// </summary>
    /// <param name="maxError">How far a rounded end may stray from the true arc, in the same units.</param>
    public static IReadOnlyList<PolygonWithHoles> KnockOut(
        Vector2D pivot,
        double angleDegrees,
        double margin,
        IEnumerable<(Vector2D A, Vector2D B)> strokes,
        double strokeWidth,
        IEnumerable<PolygonWithHoles> shapes,
        double maxError)
    {
        var text = Letters(strokes, strokeWidth, shapes, maxError);
        if (text.Count == 0)
        {
            return [];
        }

        // The box in the text's frame: every point turned back by the text's angle, then the corners turned forward.
        var (sin, cos) = Transform2D.SinCos(angleDegrees);
        Vector2D Turn(Vector2D p, double s)
        {
            double dx = p.X - pivot.X, dy = p.Y - pivot.Y;
            return new Vector2D(pivot.X + (dx * cos) + (dy * s), pivot.Y - (dx * s) + (dy * cos));
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in text.SelectMany(p => p))
        {
            var local = Turn(new Vector2D(point.X, point.Y), -sin);
            minX = Math.Min(minX, local.X);
            minY = Math.Min(minY, local.Y);
            maxX = Math.Max(maxX, local.X);
            maxY = Math.Max(maxY, local.Y);
        }

        Vector2D[] corners =
        [
            new(minX - margin, minY - margin), new(maxX + margin, minY - margin),
            new(maxX + margin, maxY + margin), new(minX - margin, maxY + margin),
        ];
        var box = new Paths64 { Path(corners.Select(c => Turn(c, sin))) };

        var clipper = new Clipper64();
        clipper.AddSubject(box);
        clipper.AddClip(text);
        var tree = new PolyTree64();
        clipper.Execute(ClipType.Difference, FillRule.NonZero, tree);
        return Regions(tree);
    }

    /// <summary>Strokes and shapes merged into one region; outlines and holes are taken by their role, not their winding.</summary>
    private static Paths64 Letters(IEnumerable<(Vector2D A, Vector2D B)> strokes, double strokeWidth, IEnumerable<PolygonWithHoles> shapes, double maxError)
    {
        var lines = new Paths64(strokes.Select(s => Path([s.A, s.B])));
        var region = lines.Count > 0 && strokeWidth > 0
            ? Clipper.InflatePaths(lines, strokeWidth / 2, JoinType.Round, EndType.Round, 2, Math.Max(maxError, 1))
            : [];

        var filled = new Paths64();
        foreach (var shape in shapes)
        {
            filled.Add(Oriented(Path(shape.Outline), positive: true));
            filled.AddRange(shape.Holes.Select(h => Oriented(Path(h), positive: false)));
        }

        return filled.Count == 0 ? region : Clipper.Union(region, filled, FillRule.NonZero);
    }

    /// <summary>Every outer ring with the holes directly inside it; what sits inside a hole is a region of its own.</summary>
    private static List<PolygonWithHoles> Regions(PolyPath64 parent)
    {
        var regions = new List<PolygonWithHoles>();
        for (int i = 0; i < parent.Count; i++)
        {
            var outer = parent[i];
            var holes = new List<Vector2D[]>();
            for (int j = 0; j < outer.Count; j++)
            {
                holes.Add(Points(outer[j].Polygon!));
                regions.AddRange(Regions(outer[j]));
            }

            regions.Add(new PolygonWithHoles(Points(outer.Polygon!), holes));
        }

        return regions;
    }

    private static Path64 Oriented(Path64 path, bool positive) =>
        (Clipper.Area(path) >= 0) == positive ? path : Clipper.ReversePath(path);

    private static Path64 Path(IEnumerable<Vector2D> points) =>
        new(points.Select(p => new Point64((long)Math.Round(p.X), (long)Math.Round(p.Y))));

    private static Vector2D[] Points(Path64 path) => [.. path.Select(p => new Vector2D(p.X, p.Y))];
}
