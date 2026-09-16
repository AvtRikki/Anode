namespace Anode.Geometry;

/// <summary>
/// Affine transform: x' = A·x + C·y + Tx, y' = B·x + D·y + Ty.
/// </summary>
public readonly record struct Transform2D(double A, double B, double C, double D, double Tx, double Ty)
{
    public static readonly Transform2D Identity = new(1, 0, 0, 1, 0, 0);

    public static Transform2D Translation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    public static Transform2D Translation(Vector2L t) => Translation(t.X, t.Y);

    public static Transform2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Rotation by <paramref name="degrees"/> in math orientation (counter-clockwise for Y-up axes).</summary>
    public static Transform2D Rotation(double degrees)
    {
        var (sin, cos) = SinCos(degrees);
        return new Transform2D(cos, sin, -sin, cos, 0, 0);
    }

    public double Determinant => A * D - B * C;

    public bool IsMirrored => Determinant < 0;

    /// <summary>Returns a transform that applies this one first, then <paramref name="next"/>.</summary>
    public Transform2D Then(Transform2D next) => new(
        next.A * A + next.C * B,
        next.B * A + next.D * B,
        next.A * C + next.C * D,
        next.B * C + next.D * D,
        next.A * Tx + next.C * Ty + next.Tx,
        next.B * Tx + next.D * Ty + next.Ty);

    public Transform2D Inverse()
    {
        double det = Determinant;
        if (det == 0)
        {
            throw new InvalidOperationException("Transform is not invertible.");
        }

        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        return new Transform2D(ia, ib, ic, id, -(ia * Tx + ic * Ty), -(ib * Tx + id * Ty));
    }

    public Vector2D Apply(Vector2D p) => new(A * p.X + C * p.Y + Tx, B * p.X + D * p.Y + Ty);

    public Vector2D Apply(Vector2L p) => Apply(p.ToDouble());

    public Vector2L ApplyRounded(Vector2L p) => Apply(p.ToDouble()).Round();

    /// <summary>Applies only the linear part (no translation), for direction vectors.</summary>
    public Vector2D ApplyVector(Vector2D v) => new(A * v.X + C * v.Y, B * v.X + D * v.Y);

    /// <summary>Uniform scale factor (geometric mean of axis scales).</summary>
    public double ScaleFactor => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>Sin and cos with exact values at multiples of 90° to keep orthogonal geometry exact.</summary>
    public static (double Sin, double Cos) SinCos(double degrees)
    {
        double normalized = degrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized switch
        {
            0 => (0, 1),
            90 => (1, 0),
            180 => (0, -1),
            270 => (-1, 0),
            _ => (Math.Sin(normalized * Math.PI / 180), Math.Cos(normalized * Math.PI / 180)),
        };
    }
}
