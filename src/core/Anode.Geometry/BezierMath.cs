namespace Anode.Geometry;

/// <summary>
/// Turning a Bézier curve into the run of short lines that draws it.
///
/// KiCad writes a curve as four points: where it starts, where it ends, and two that pull it on the way. Only the
/// first and last are on the curve at all — joining the four with straight lines draws the frame the curve hangs
/// in, not the curve, which is a different shape and visibly so.
/// </summary>
public static class BezierMath
{
    /// <summary>How finely one curve is cut. KiCad draws with a tolerance; a fixed count is steady and enough.</summary>
    private const int Steps = 32;

    /// <summary>
    /// The points along a curve written as <paramref name="controls"/>: four points for one curve, and every three
    /// after that for each curve joined to it. Fewer than four points is not a curve at all and is given back as
    /// it stands, which is the only honest thing to draw.
    /// </summary>
    public static Vector2D[] Tessellate(IReadOnlyList<Vector2D> controls, int steps = Steps)
    {
        if (controls.Count < 4)
        {
            return [.. controls];
        }

        var points = new List<Vector2D> { controls[0] };
        for (int at = 0; at + 3 < controls.Count; at += 3)
        {
            for (int i = 1; i <= steps; i++)
            {
                points.Add(PointAt(controls[at], controls[at + 1], controls[at + 2], controls[at + 3], (double)i / steps));
            }
        }

        return [.. points];
    }

    /// <summary>Where the curve is a given part of the way along, by de Casteljau's reading of it.</summary>
    public static Vector2D PointAt(Vector2D from, Vector2D pullFrom, Vector2D pullTo, Vector2D to, double at)
    {
        double back = 1 - at;
        double a = back * back * back;
        double b = 3 * back * back * at;
        double c = 3 * back * at * at;
        double d = at * at * at;

        return new Vector2D(
            (a * from.X) + (b * pullFrom.X) + (c * pullTo.X) + (d * to.X),
            (a * from.Y) + (b * pullFrom.Y) + (c * pullTo.Y) + (d * to.Y));
    }
}
