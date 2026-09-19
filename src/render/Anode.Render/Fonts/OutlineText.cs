using System.Collections.Concurrent;
using Anode.Geometry;
using SkiaSharp;

namespace Anode.Render.Fonts;

/// <summary>
/// Text in an installed typeface, as filled outlines — how KiCad draws text whose font names a face. Sized as KiCad
/// sizes it: the em is 1.4 times the text height, which puts capitals about as tall as the stroke font's. A face that
/// is not installed answers null, and the caller draws the stroke font instead.
/// </summary>
public static class OutlineText
{
    /// <summary>One filled region: an outline and the holes inside it, in the caller's units.</summary>
    public sealed record Shape(Vector2D[] Outline, IReadOnlyList<Vector2D[]> Holes);

    /// <summary>KiCad's outline font size compensation.</summary>
    private const double EmPerHeight = 1.4;

    /// <summary>Glyphs are read at this size and scaled; large enough that their curves flatten smoothly.</summary>
    private const float Units = 1000f;

    private const int CurveSteps = 8;

    private static readonly ConcurrentDictionary<(string Face, bool Bold, bool Italic), SKTypeface?> Faces = new();

    public static bool IsInstalled(string face) => Typeface(face, false, false) is not null;

    /// <param name="anchor">Where the text is anchored, in the caller's units (nanometres, say).</param>
    /// <param name="width">Glyph width in the caller's units; with <paramref name="height"/> it may stretch the text.</param>
    /// <param name="height">Glyph height in the caller's units.</param>
    /// <param name="angleDegrees">Turned about the anchor as the stroke font turns text.</param>
    public static IReadOnlyList<Shape>? Layout(
        string face,
        bool bold,
        bool italic,
        string text,
        Vector2D anchor,
        double width,
        double height,
        TextHAlign horizontal,
        TextVAlign vertical,
        double angleDegrees)
    {
        if (height <= 0 || Typeface(face, bold, italic) is not { } typeface)
        {
            return null;
        }

        double scale = EmPerHeight * height / Units;
        using var font = new SKFont(typeface, Units, (float)(width / height), 0);

        // Asked for a weight or a slant the family does not carry: made up, as a font engine would.
        font.Embolden = bold && typeface.FontWeight < (int)SKFontStyleWeight.SemiBold;
        if (italic && typeface.FontSlant == SKFontStyleSlant.Upright)
        {
            font.SkewX = -0.25f;
        }

        font.GetFontMetrics(out var metrics);
        double cap = (metrics.CapHeight > 0 ? metrics.CapHeight : -metrics.Ascent * 0.7) * scale;
        double pitch = font.Spacing * scale;

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        double block = cap + ((lines.Length - 1) * pitch);
        double top = vertical switch
        {
            TextVAlign.Top => 0,
            TextVAlign.Bottom => -block,
            _ => -block / 2,
        };

        var (sin, cos) = Transform2D.SinCos(angleDegrees);
        var shapes = new List<Shape>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            double lineWidth = font.MeasureText(lines[i]) * scale;
            double left = horizontal switch
            {
                TextHAlign.Center => -lineWidth / 2,
                TextHAlign.Right => -lineWidth,
                _ => 0,
            };
            double baseline = top + cap + (i * pitch);

            Vector2D Place(SKPoint p)
            {
                double dx = left + (p.X * scale), dy = baseline + (p.Y * scale);
                return new Vector2D(anchor.X + (dx * cos) + (dy * sin), anchor.Y - (dx * sin) + (dy * cos));
            }

            using var path = font.GetTextPath(lines[i], SKPoint.Empty);
            shapes.AddRange(Group([.. Flatten(path).Select(c => c.Select(Place).ToArray())]));
        }

        return shapes;
    }

    /// <summary>The face, or null when this machine does not have it — the engine would quietly hand back another.</summary>
    private static SKTypeface? Typeface(string face, bool bold, bool italic) => Faces.GetOrAdd((face, bold, italic), key =>
    {
        var typeface = SKTypeface.FromFamilyName(
            key.Face,
            key.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        if (typeface is not null && string.Equals(typeface.FamilyName, key.Face, StringComparison.OrdinalIgnoreCase))
        {
            return typeface;
        }

        typeface?.Dispose();
        return null;
    });

    /// <summary>The path's contours as polygons, curves cut into short straight steps.</summary>
    private static List<List<SKPoint>> Flatten(SKPath path)
    {
        var contours = new List<List<SKPoint>>();
        List<SKPoint>? current = null;
        using var iterator = path.CreateRawIterator();
        var p = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    current = [p[0]];
                    contours.Add(current);
                    break;
                case SKPathVerb.Line:
                    current?.Add(p[1]);
                    break;
                case SKPathVerb.Quad:
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        current?.Add(new SKPoint((u * u * p[0].X) + (2 * u * t * p[1].X) + (t * t * p[2].X), (u * u * p[0].Y) + (2 * u * t * p[1].Y) + (t * t * p[2].Y)));
                    }

                    break;
                case SKPathVerb.Conic:
                    float w = iterator.ConicWeight();
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        float a = u * u, b = 2 * w * u * t, c = t * t, d = a + b + c;
                        current?.Add(new SKPoint(((a * p[0].X) + (b * p[1].X) + (c * p[2].X)) / d, ((a * p[0].Y) + (b * p[1].Y) + (c * p[2].Y)) / d));
                    }

                    break;
                case SKPathVerb.Cubic:
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                        current?.Add(new SKPoint((a * p[0].X) + (b * p[1].X) + (c * p[2].X) + (d * p[3].X), (a * p[0].Y) + (b * p[1].Y) + (c * p[2].Y) + (d * p[3].Y)));
                    }

                    break;
            }
        }

        foreach (var contour in contours.Where(c => c.Count > 1 && c[0] == c[^1]))
        {
            contour.RemoveAt(contour.Count - 1);
        }

        return [.. contours.Where(c => c.Count >= 3)];
    }

    /// <summary>
    /// Contours sorted into shapes by how deeply each is nested: an outline at even depth, a hole at odd — the inside
    /// of an "o", the island in a "®" — each hole belonging to the smallest outline around it.
    /// </summary>
    private static IEnumerable<Shape> Group(List<Vector2D[]> contours)
    {
        int n = contours.Count;
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i != j && Inside(contours[i][0], contours[j]))
                {
                    depth[i]++;
                }
            }
        }

        var holes = Enumerable.Range(0, n).ToDictionary(i => i, _ => new List<Vector2D[]>());
        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 1)
            {
                int parent = Enumerable.Range(0, n)
                    .Where(j => depth[j] == depth[i] - 1 && Inside(contours[i][0], contours[j]))
                    .OrderBy(j => Math.Abs(Area(contours[j])))
                    .DefaultIfEmpty(-1)
                    .First();
                if (parent >= 0)
                {
                    holes[parent].Add(contours[i]);
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 0)
            {
                yield return new Shape(contours[i], holes[i]);
            }
        }
    }

    private static bool Inside(Vector2D p, Vector2D[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)
                && p.X < ((polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y)) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Area(Vector2D[] polygon)
    {
        double sum = 0;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            sum += (polygon[j].X * polygon[i].Y) - (polygon[i].X * polygon[j].Y);
        }

        return sum / 2;
    }
}
