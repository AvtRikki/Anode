namespace Ecad.Geometry;

/// <summary>Axis-aligned bounding box in nanometres, inclusive on both ends.</summary>
public readonly record struct Box2L(long MinX, long MinY, long MaxX, long MaxY)
{
    public static readonly Box2L Empty = new(long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public long Width => IsEmpty ? 0 : MaxX - MinX;

    public long Height => IsEmpty ? 0 : MaxY - MinY;

    public Vector2L Center => new(MinX + (MaxX - MinX) / 2, MinY + (MaxY - MinY) / 2);

    public static Box2L FromPoints(Vector2L a, Vector2L b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static Box2L FromCircle(Vector2L center, long radius) =>
        new(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);

    public Box2L Union(Vector2L p) => IsEmpty
        ? new Box2L(p.X, p.Y, p.X, p.Y)
        : new Box2L(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Box2L Union(Box2L b)
    {
        if (b.IsEmpty)
        {
            return this;
        }

        return IsEmpty ? b : new Box2L(Math.Min(MinX, b.MinX), Math.Min(MinY, b.MinY), Math.Max(MaxX, b.MaxX), Math.Max(MaxY, b.MaxY));
    }

    public Box2L Inflate(long amount) =>
        IsEmpty ? this : new Box2L(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);

    public bool Contains(Vector2L p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Intersects(Box2L b) =>
        !IsEmpty && !b.IsEmpty && MinX <= b.MaxX && b.MinX <= MaxX && MinY <= b.MaxY && b.MinY <= MaxY;
}
