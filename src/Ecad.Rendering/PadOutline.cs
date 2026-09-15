using Ecad.Geometry;
using Ecad.KiCad;

namespace Ecad.Rendering;

/// <summary>Pad copper shape in pad frame (nm, centred on the hole, unrotated).</summary>
internal static class PadOutline
{
    public abstract record Part;

    public sealed record Disc(Vector2D Center, double Radius) : Part;

    /// <summary>Stadium: a round-capped segment.</summary>
    public sealed record Stadium(Vector2D A, Vector2D B, double Width) : Part;

    public sealed record Polygon(Vector2D[] Points) : Part;

    public static IEnumerable<Part> Build(Pad pad, PadShape shape, Vector2L sizeL, Vector2D offset)
    {
        double w = sizeL.X, h = sizeL.Y;
        switch (shape)
        {
            case PadShape.Circle:
                yield return new Disc(offset, w / 2);
                break;

            case PadShape.Oval:
                if (Math.Abs(w - h) < 1)
                {
                    yield return new Disc(offset, w / 2);
                }
                else if (w > h)
                {
                    double d = (w - h) / 2;
                    yield return new Stadium(offset + new Vector2D(-d, 0), offset + new Vector2D(d, 0), h);
                }
                else
                {
                    double d = (h - w) / 2;
                    yield return new Stadium(offset + new Vector2D(0, -d), offset + new Vector2D(0, d), w);
                }

                break;

            case PadShape.RoundRect:
                yield return new Polygon(RoundRect(offset, w, h, pad.RoundRectRatio * Math.Min(w, h), pad.ChamferRatio * Math.Min(w, h), pad.Chamfers));
                break;

            case PadShape.Trapezoid:
                var delta = pad.RectDelta;
                double dx = delta.X / 2.0, dy = delta.Y / 2.0, hx = w / 2, hy = h / 2;
                yield return new Polygon(
                [
                    offset + new Vector2D(-hx - dy, hy + dx),
                    offset + new Vector2D(-hx + dy, -hy - dx),
                    offset + new Vector2D(hx - dy, -hy + dx),
                    offset + new Vector2D(hx + dy, hy - dx),
                ]);
                break;

            case PadShape.Custom:
                // Primitives are emitted separately by the scene builder; the anchor gives the base copper.
                if (pad.AnchorShape == PadShape.Circle)
                {
                    yield return new Disc(offset, w / 2);
                }
                else
                {
                    yield return new Polygon(Rect(offset, w, h));
                }

                break;

            default:
                yield return new Polygon(Rect(offset, w, h));
                break;
        }
    }

    private static Vector2D[] Rect(Vector2D c, double w, double h) =>
    [
        c + new Vector2D(-w / 2, -h / 2),
        c + new Vector2D(w / 2, -h / 2),
        c + new Vector2D(w / 2, h / 2),
        c + new Vector2D(-w / 2, h / 2),
    ];

    private static Vector2D[] RoundRect(Vector2D c, double w, double h, double radius, double chamfer, PadChamfers chamfers)
    {
        radius = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        chamfer = Math.Clamp(chamfer, 0, Math.Min(w, h) / 2);
        var points = new List<Vector2D>();

        // Corners clockwise on screen starting top-left; each entry: corner, flag, angle where the rounding starts.
        (Vector2D Corner, PadChamfers Flag, double StartAngle, Vector2D Inward)[] corners =
        [
            (c + new Vector2D(-w / 2, -h / 2), PadChamfers.TopLeft, Math.PI, new Vector2D(1, 1)),
            (c + new Vector2D(w / 2, -h / 2), PadChamfers.TopRight, 1.5 * Math.PI, new Vector2D(-1, 1)),
            (c + new Vector2D(w / 2, h / 2), PadChamfers.BottomRight, 0, new Vector2D(-1, -1)),
            (c + new Vector2D(-w / 2, h / 2), PadChamfers.BottomLeft, 0.5 * Math.PI, new Vector2D(1, -1)),
        ];

        foreach (var (corner, flag, start, inward) in corners)
        {
            if (chamfer > 0 && chamfers.HasFlag(flag))
            {
                // Two points along the adjacent edges, in the same rotational order as the rounding.
                var alongPrev = RotateOrder(start) ? new Vector2D(0, inward.Y * chamfer) : new Vector2D(inward.X * chamfer, 0);
                var alongNext = RotateOrder(start) ? new Vector2D(inward.X * chamfer, 0) : new Vector2D(0, inward.Y * chamfer);
                points.Add(corner + alongPrev);
                points.Add(corner + alongNext);
            }
            else if (radius > 0)
            {
                var center = corner + new Vector2D(inward.X * radius, inward.Y * radius);
                var arc = new Arc(center, radius, start, Math.PI / 2);
                points.AddRange(ArcMath.Tessellate(arc, Math.Max(radius / 50, 1000)));
            }
            else
            {
                points.Add(corner);
            }
        }

        return [.. points];
    }

    // Top-left and bottom-right corners are entered from a vertical edge, the others from a horizontal edge.
    private static bool RotateOrder(double startAngle) => startAngle is Math.PI or 0;
}
