using System.Numerics;

namespace Anode.Render;

/// <summary>Scene coordinates are millimetres relative to <see cref="BoardScene.OriginNm"/>, Y down.</summary>
public readonly record struct RectD(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly RectD Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public double Width => IsEmpty ? 0 : MaxX - MinX;

    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public RectD Union(double x, double y) => new(Math.Min(MinX, x), Math.Min(MinY, y), Math.Max(MaxX, x), Math.Max(MaxY, y));

    public RectD Union(RectD r) => r.IsEmpty ? this : IsEmpty ? r : new(Math.Min(MinX, r.MinX), Math.Min(MinY, r.MinY), Math.Max(MaxX, r.MaxX), Math.Max(MaxY, r.MaxY));

    public RectD Inflate(double d) => IsEmpty ? this : new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public bool Intersects(RectD r) => !IsEmpty && !r.IsEmpty && MinX <= r.MaxX && r.MinX <= MaxX && MinY <= r.MaxY && r.MinY <= MaxY;
}

public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public ColorRgba WithAlpha(byte a) => this with { A = a };

    public static ColorRgba Rgb(byte r, byte g, byte b) => new(r, g, b);
}

/// <summary>Stroke with round caps: tracks, graphic lines, tessellated arcs.</summary>
public readonly record struct LinePrim(Vector2 A, Vector2 B, float Width, int Owner);

/// <summary>Filled disc: vias, round pads, holes.</summary>
public readonly record struct CirclePrim(Vector2 Center, float Radius, int Owner);

/// <summary>Filled simple polygon (implicitly closed): zone fills, pads, filled shapes.</summary>
public sealed class PolygonPrim(Vector2[] points, int owner)
{
    private int[]? _triangles;

    public Vector2[] Points { get; } = points;

    public int Owner { get; } = owner;

    public bool IsTriangulated => _triangles is not null;

    /// <summary>
    /// Triangle indices into <see cref="Points"/>, computed on first access and cached.
    /// Large zone fills take hundreds of milliseconds; see <see cref="SceneTriangulator"/> to do it up front.
    /// </summary>
    public int[] Triangles => _triangles ??= [.. Anode.Geometry.Earcut.Triangulate(Points)];

    public RectD Bounds
    {
        get
        {
            var r = RectD.Empty;
            foreach (var p in Points)
            {
                r = r.Union(p.X, p.Y);
            }

            return r;
        }
    }
}

public enum TextHAlign
{
    Left,
    Center,
    Right,
}

public enum TextVAlign
{
    Top,
    Center,
    Bottom,
}
