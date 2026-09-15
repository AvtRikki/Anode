namespace Ecad.Geometry;

/// <summary>Circular arc described by centre, radius, start angle and signed sweep (radians, file axes).</summary>
public readonly record struct Arc(Vector2D Center, double Radius, double StartAngle, double Sweep)
{
    public double EndAngle => StartAngle + Sweep;

    public Vector2D PointAt(double angle) => new(Center.X + Radius * Math.Cos(angle), Center.Y + Radius * Math.Sin(angle));

    public Vector2D Start => PointAt(StartAngle);

    public Vector2D End => PointAt(EndAngle);
}

public static class ArcMath
{
    /// <summary>Default chord error when tessellating (KiCad uses 5 µm for display).</summary>
    public const double DefaultMaxErrorNm = 5_000;

    public static bool TryCircleFromThreePoints(Vector2D a, Vector2D b, Vector2D c, out Vector2D center, out double radius)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-9)
        {
            center = default;
            radius = 0;
            return false;
        }

        double a2 = a.LengthSquared, b2 = b.LengthSquared, c2 = c.LengthSquared;
        center = new Vector2D(
            (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
            (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        radius = Vector2D.Distance(center, a);
        return true;
    }

    /// <summary>Arc through start, mid and end; null when the points are collinear.</summary>
    public static Arc? FromStartMidEnd(Vector2D start, Vector2D mid, Vector2D end)
    {
        if (!TryCircleFromThreePoints(start, mid, end, out var center, out double radius))
        {
            return null;
        }

        double a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
        double am = Math.Atan2(mid.Y - center.Y, mid.X - center.X);
        double a1 = Math.Atan2(end.Y - center.Y, end.X - center.X);

        double toEnd = NormalizePositive(a1 - a0);
        double toMid = NormalizePositive(am - a0);

        // Positive sweep if mid is reached before end going in +angle direction, otherwise negative.
        double sweep = toMid <= toEnd ? toEnd : toEnd - 2 * Math.PI;
        if (Vector2D.Distance(start, end) < 1e-9)
        {
            sweep = 2 * Math.PI;
        }

        return new Arc(center, radius, a0, sweep);
    }

    /// <summary>Arc from centre, start point and signed sweep in degrees.</summary>
    public static Arc FromCenterStartSweep(Vector2D center, Vector2D start, double sweepDegrees) =>
        new(center, Vector2D.Distance(center, start), Math.Atan2(start.Y - center.Y, start.X - center.X), sweepDegrees * Math.PI / 180);

    public static int SegmentCount(double radius, double sweep, double maxError = DefaultMaxErrorNm)
    {
        if (radius <= maxError)
        {
            return Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2)));
        }

        double step = 2 * Math.Acos(1 - maxError / radius);
        return Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / step), 1, 720);
    }

    /// <summary>Points along the arc including both ends.</summary>
    public static Vector2D[] Tessellate(Arc arc, double maxError = DefaultMaxErrorNm)
    {
        int n = SegmentCount(arc.Radius, arc.Sweep, maxError);
        var points = new Vector2D[n + 1];
        for (int i = 0; i <= n; i++)
        {
            points[i] = arc.PointAt(arc.StartAngle + arc.Sweep * i / n);
        }

        return points;
    }

    private static double NormalizePositive(double angle)
    {
        angle %= 2 * Math.PI;
        return angle < 0 ? angle + 2 * Math.PI : angle;
    }
}
