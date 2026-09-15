using System.Text;
using Ecad.Geometry;

namespace Ecad.Rendering.Fonts;

/// <param name="Width">Glyph width in board units (KiCad text size X).</param>
/// <param name="Height">Glyph height in board units (KiCad text size Y).</param>
/// <param name="PenWidth">Stroke width in board units.</param>
/// <param name="AngleDegrees">Counter-clockwise on screen, already adjusted for keep-upright.</param>
public readonly record struct StrokeTextStyle(
    double Width,
    double Height,
    double PenWidth,
    TextHAlign HAlign = TextHAlign.Center,
    TextVAlign VAlign = TextVAlign.Center,
    double AngleDegrees = 0,
    bool Mirrored = false,
    bool Italic = false,
    double LineSpacing = 1);

/// <summary>
/// Lays out text with a <see cref="StrokeFont"/> the way KiCad places stroke text: line boxes, alignment offsets,
/// italic tilt, mirroring about the anchor, rotation about the anchor, and <c>~{overbar}</c>,
/// <c>^{superscript}</c>, <c>_{subscript}</c> markup.
/// </summary>
public static class StrokeTextLayout
{
    private const double ItalicTilt = 1.0 / 8;
    private const double InterlinePitch = 1.68;
    private const double LegacyInterlineFactor = 0.9583;
    private const double FirstLineHeightFactor = 1.17;
    private const double OverbarHeight = 1.23;
    private const double SuperSubScale = 0.8;
    private const double SuperscriptRise = 0.35;
    private const double SubscriptDrop = 0.15;
    private const int TabWidth = 4;

    [Flags]
    private enum Markup
    {
        None = 0,
        Overbar = 1,
        Superscript = 2,
        Subscript = 4,
    }

    /// <summary>Emits every stroke segment of <paramref name="text"/> anchored at <paramref name="anchor"/>.</summary>
    public static void Layout(StrokeFont font, string text, Vector2D anchor, in StrokeTextStyle style, Action<Vector2D, Vector2D> segment)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        double interline = style.Height * InterlinePitch * LegacyInterlineFactor * style.LineSpacing;

        double height = 0;
        var widths = new double[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            widths[i] = MeasureLine(font, lines[i], style);
            height += i == 0 ? style.Height * FirstLineHeightFactor : interline;
        }

        // KiCad: the pen offsets are "fudge factors to match 6.0 positioning".
        double offsetX = style.PenWidth / 1.52;
        double offsetY = style.Height - style.PenWidth * 0.052;
        offsetY -= style.VAlign switch
        {
            TextVAlign.Center => height / 2,
            TextVAlign.Bottom => height,
            _ => 0,
        };

        var (sin, cos) = Transform2D.SinCos(style.AngleDegrees);
        var emitter = new Emitter(anchor, style.Mirrored, sin, cos, segment);

        for (int i = 0; i < lines.Length; i++)
        {
            double x = style.HAlign switch
            {
                TextHAlign.Center => -widths[i] / 2,
                TextHAlign.Right => -(widths[i] + offsetX),
                _ => offsetX,
            };

            DrawLine(font, lines[i], new Vector2D(anchor.X + x, anchor.Y + offsetY + i * interline), style, emitter);
        }
    }

    /// <summary>Advance width of a single line in board units.</summary>
    public static double MeasureLine(StrokeFont font, string line, in StrokeTextStyle style)
    {
        double width = 0;
        foreach (var (run, markup) in ParseMarkup(line))
        {
            width += Advance(font, run, GlyphScale(markup) * style.Width, width, style.Width);
        }

        return width;
    }

    private static void DrawLine(StrokeFont font, string line, Vector2D position, in StrokeTextStyle style, in Emitter emitter)
    {
        double cursorX = position.X;
        double tilt = style.Italic ? ItalicTilt : 0;

        foreach (var (run, markup) in ParseMarkup(line))
        {
            double scale = GlyphScale(markup);
            double glyphW = style.Width * scale;
            double glyphH = style.Height * scale;
            double baseline = position.Y
                              + (markup.HasFlag(Markup.Subscript) ? glyphH * SubscriptDrop : 0)
                              - (markup.HasFlag(Markup.Superscript) ? glyphH * SuperscriptRise : 0);
            double runStart = cursorX;

            foreach (var rune in run.EnumerateRunes())
            {
                int c = rune.Value;
                if (c == '\t')
                {
                    cursorX += Advance(font, "\t", glyphW, cursorX - position.X, style.Width);
                    continue;
                }

                if (c == ' ')
                {
                    cursorX += glyphW * font.SpaceAdvance;
                    continue;
                }

                var glyph = font.GetGlyph(c);
                foreach (var stroke in glyph.Strokes)
                {
                    var prev = Place(stroke[0], glyphW, glyphH, tilt, cursorX, baseline);
                    if (stroke.Length == 1)
                    {
                        emitter.Emit(prev, prev);
                    }

                    for (int k = 1; k < stroke.Length; k++)
                    {
                        var next = Place(stroke[k], glyphW, glyphH, tilt, cursorX, baseline);
                        emitter.Emit(prev, next);
                        prev = next;
                    }
                }

                cursorX += glyph.Advance * glyphW;
            }

            if (markup.HasFlag(Markup.Overbar) && cursorX > runStart)
            {
                double trim = style.Width * 0.1;
                double barY = position.Y - style.Height * OverbarHeight;
                emitter.Emit(new Vector2D(runStart + trim, barY), new Vector2D(cursorX - trim, barY));
            }
        }
    }

    private static Vector2D Place(Vector2D p, double glyphW, double glyphH, double tilt, double cursorX, double baseline)
    {
        double x = p.X * glyphW;
        double y = p.Y * glyphH;
        return new Vector2D(x - y * tilt + cursorX, y + baseline);
    }

    private static double Advance(StrokeFont font, string run, double glyphW, double startX, double baseWidth)
    {
        double width = 0;
        int column = 0;
        foreach (var rune in run.EnumerateRunes())
        {
            int c = rune.Value;
            if (c == '\t')
            {
                // Tabs snap to the next multiple of four base-width columns.
                column = (column / TabWidth + 1) * TabWidth;
                double target = column * baseWidth;
                width = Math.Max(width + glyphW * font.SpaceAdvance, target - startX);
                continue;
            }

            width += c == ' ' ? glyphW * font.SpaceAdvance : font.GetGlyph(c).Advance * glyphW;
            column++;
        }

        return width;
    }

    private static double GlyphScale(Markup markup) =>
        markup.HasFlag(Markup.Superscript) || markup.HasFlag(Markup.Subscript) ? SuperSubScale : 1;

    /// <summary>Splits KiCad markup into runs; unbalanced markup is kept literally.</summary>
    private static List<(string Run, Markup Style)> ParseMarkup(string line)
    {
        var runs = new List<(string, Markup)>();
        var plain = new StringBuilder();
        int i = 0;
        while (i < line.Length)
        {
            if (i + 1 < line.Length && line[i + 1] == '{' && line[i] is '~' or '^' or '_')
            {
                int close = line.IndexOf('}', i + 2);
                if (close > 0)
                {
                    if (plain.Length > 0)
                    {
                        runs.Add((plain.ToString(), Markup.None));
                        plain.Clear();
                    }

                    var style = line[i] switch
                    {
                        '~' => Markup.Overbar,
                        '^' => Markup.Superscript,
                        _ => Markup.Subscript,
                    };
                    runs.Add((line[(i + 2)..close], style));
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(line[i]);
            i++;
        }

        if (plain.Length > 0 || runs.Count == 0)
        {
            runs.Add((plain.ToString(), Markup.None));
        }

        return runs;
    }

    /// <summary>Mirrors about the anchor's X, then rotates counter-clockwise (on screen) about the anchor.</summary>
    private readonly struct Emitter(Vector2D anchor, bool mirrored, double sin, double cos, Action<Vector2D, Vector2D> segment)
    {
        public void Emit(Vector2D a, Vector2D b) => segment(Transform(a), Transform(b));

        private Vector2D Transform(Vector2D p)
        {
            double dx = p.X - anchor.X;
            double dy = p.Y - anchor.Y;
            if (mirrored)
            {
                dx = -dx;
            }

            return new Vector2D(anchor.X + dx * cos + dy * sin, anchor.Y - dx * sin + dy * cos);
        }
    }
}
