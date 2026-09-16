namespace Anode.Geometry;

/// <summary>Integer point in nanometres (KiCad internal units). Y grows downwards.</summary>
public readonly record struct Vector2L(long X, long Y)
{
    public static readonly Vector2L Zero = default;

    public static Vector2L operator +(Vector2L a, Vector2L b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2L operator -(Vector2L a, Vector2L b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2L operator -(Vector2L a) => new(-a.X, -a.Y);
    public static Vector2L operator *(Vector2L a, long s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt((double)X * X + (double)Y * Y);

    public Vector2D ToDouble() => new(X, Y);

    public override string ToString() => $"({X}, {Y})";
}

public readonly record struct Vector2D(double X, double Y)
{
    public static readonly Vector2D Zero = default;

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator -(Vector2D a) => new(-a.X, -a.Y);
    public static Vector2D operator *(Vector2D a, double s) => new(a.X * s, a.Y * s);
    public static Vector2D operator /(Vector2D a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    public Vector2D Normalized()
    {
        double len = Length;
        return len > 0 ? this / len : Zero;
    }

    /// <summary>Perpendicular vector (rotated +90° in math orientation).</summary>
    public Vector2D Perpendicular => new(-Y, X);

    public static double Dot(Vector2D a, Vector2D b) => a.X * b.X + a.Y * b.Y;

    public static double Cross(Vector2D a, Vector2D b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vector2D a, Vector2D b) => (a - b).Length;

    public Vector2L Round() => new((long)Math.Round(X), (long)Math.Round(Y));

    public override string ToString() => $"({X:G6}, {Y:G6})";
}
