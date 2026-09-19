using System.Globalization;
using System.Text.RegularExpressions;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Render.Fonts;
using Anode.Sexpr;

namespace Anode.Render;

/// <summary>
/// KiCad's default drawing sheet — the frame and title block around a schematic, and around a board's page — as
/// strokes on the page. Written once for both kinds of file: a sheet and a board keep their paper and title block
/// the same way, and each scene only says where the strokes go.
/// </summary>
public static partial class DrawingSheet
{
    /// <summary>The drawing sheet's line and text pen: KiCad's 0.15 mm.</summary>
    public const long LineWidth = 150_000;

    private const double Mm = Units.NmPerMm;

    /// <summary>
    /// The paper under a file's root, in nanometres: a named size, landscape unless the file says portrait, or
    /// KiCad's "User" size with its own width and height.
    /// </summary>
    public static Vector2L PaperOf(SList root)
    {
        var node = root.Find("paper");
        string name = node?.AtomAt(1)?.Value ?? "A4";
        if (name == "User"
            && node?.AtomAt(2)?.TryGetDouble(out double w) == true
            && node.AtomAt(3)?.TryGetDouble(out double h) == true)
        {
            return new Vector2L(Units.MmToNm(w), Units.MmToNm(h));
        }

        var (width, height) = name switch
        {
            "A5" => (210.0, 148.0),
            "A4" => (297.0, 210.0),
            "A3" => (420.0, 297.0),
            "A2" => (594.0, 420.0),
            "A1" => (841.0, 594.0),
            "A0" => (1189.0, 841.0),
            "A" or "USLetter" => (279.4, 215.9),
            "B" or "USLedger" => (431.8, 279.4),
            "C" => (558.8, 431.8),
            "D" => (863.6, 558.8),
            "E" => (1117.6, 863.6),
            "USLegal" => (355.6, 215.9),
            _ => (297.0, 210.0),
        };

        return node?.AtomAt(2)?.Value == "portrait"
            ? new Vector2L(Units.MmToNm(height), Units.MmToNm(width))
            : new Vector2L(Units.MmToNm(width), Units.MmToNm(height));
    }

    /// <summary>
    /// Draws a drawing sheet — the frame's template, or KiCad's default — the way KiCad does: every item from its
    /// corner of the area inside the margins, each repeat moved by its increment and dropped past the first where it
    /// would leave that area, repeated labels counting up from their last character, and <c>${NAME}</c> filled in from
    /// the title block, the page and the project, or left as written when nothing answers to it. Text with a colour
    /// of its own keeps it; text naming a face is drawn in that face's outlines, or in a stand-in's when this machine
    /// does not have it, as KiCad does.
    /// </summary>
    /// <param name="paper">The paper, in nanometres.</param>
    /// <returns>How many items could not be drawn: pictures that are not PNGs.</returns>
    public static int Draw(Vector2L paper, SchTitleBlock block, string paperName, SheetFrameText frame, IDrawingSheetSink sink)
    {
        var sheet = frame.Template ?? DrawingSheetFile.Default;
        var setup = sheet.Setup;
        double left = setup.LeftMargin, top = setup.TopMargin;
        double right = (paper.X / Mm) - setup.RightMargin, bottom = (paper.Y / Mm) - setup.BottomMargin;
        bool firstPage = frame.Page <= 1;
        int skipped = 0;

        Vector2D At(WksPoint p, WksItem item, int copy)
        {
            double x = p.X + (item.IncrementX * copy), y = p.Y + (item.IncrementY * copy);
            return p.Corner switch
            {
                WksCorner.LeftTop => new Vector2D(left + x, top + y),
                WksCorner.LeftBottom => new Vector2D(left + x, bottom - y),
                WksCorner.RightTop => new Vector2D(right - x, top + y),
                _ => new Vector2D(right - x, bottom - y),
            };
        }

        bool Inside(Vector2D mm) => mm.X >= left && mm.X <= right && mm.Y >= top && mm.Y <= bottom;

        foreach (var item in sheet.Items)
        {
            if ((item.Pages == WksPages.FirstOnly && !firstPage) || (item.Pages == WksPages.NotOnFirst && firstPage))
            {
                continue;
            }

            switch (item)
            {
                case WksLine line:
                {
                    double pen = (line.LineWidth != 0 ? line.LineWidth : setup.LineWidth) * Mm;
                    for (int j = 0; j < line.Repeat; j++)
                    {
                        var (a, b) = (At(line.Start, line, j), At(line.End, line, j));
                        if (j > 0 && !(Inside(a) && Inside(b)))
                        {
                            continue;
                        }

                        if (line.IsRectangle)
                        {
                            sink.Stroke(a * Mm, new Vector2D(b.X, a.Y) * Mm, pen, null);
                            sink.Stroke(new Vector2D(b.X, a.Y) * Mm, b * Mm, pen, null);
                            sink.Stroke(b * Mm, new Vector2D(a.X, b.Y) * Mm, pen, null);
                            sink.Stroke(new Vector2D(a.X, b.Y) * Mm, a * Mm, pen, null);
                        }
                        else
                        {
                            sink.Stroke(a * Mm, b * Mm, pen, null);
                        }
                    }

                    break;
                }

                case WksText text:
                    DrawText(text, setup, Expand(text.Text, block, paperName, frame), j => At(text.Start, text, j),
                        j => At(default, text, j), Inside, sink);
                    break;

                case WksPolygon polygon:
                    DrawPolygon(polygon, j => At(polygon.Start, polygon, j), Inside, sink);
                    break;

                case WksBitmap { Image: { } image, SizeMm: var (width, height) } bitmap:
                    for (int j = 0; j < bitmap.Repeat; j++)
                    {
                        var at = At(bitmap.Start, bitmap, j);
                        if (j > 0 && !(Inside(at) && Inside(At(default, bitmap, j))))
                        {
                            continue;
                        }

                        sink.Picture(at * Mm, new Vector2D(width, height) * Mm, image);
                    }

                    break;

                default:
                    skipped++;
                    break;
            }
        }

        return skipped;
    }

    /// <summary>The same drawing handed to plain callbacks, colours and holes aside.</summary>
    /// <param name="segment">Receives every stroke: its ends in nanometres on the page, and its pen width.</param>
    /// <param name="fill">Receives every filled outline, in nanometres; outlines are dropped when null.</param>
    /// <param name="picture">Receives every picture: its centre and size in nanometres, and the image file.</param>
    public static int Draw(
        Vector2L paper,
        SchTitleBlock block,
        string paperName,
        SheetFrameText frame,
        Action<Vector2D, Vector2D, double> segment,
        Action<IReadOnlyList<Vector2D>>? fill = null,
        Action<Vector2D, Vector2D, byte[]>? picture = null) =>
        Draw(paper, block, paperName, frame, new CallbackSink(segment, fill, picture));

    private static void DrawText(
        WksText text,
        WksSetup setup,
        string full,
        Func<int, Vector2D> start,
        Func<int, Vector2D> end,
        Func<Vector2D, bool> inside,
        IDrawingSheetSink sink)
    {
        full = Unescape(full);
        bool multiline = full.Contains('\n');
        ColorRgba? colour = text.Color is var (r, g, b, a) ? new ColorRgba(r, g, b, (byte)Math.Round(a * 255)) : null;

        double width = text.Width != 0 ? text.Width : setup.TextWidth;
        double height = text.Height != 0 ? text.Height : setup.TextHeight;
        var align = text.HorizontalAlign switch { "center" => TextHAlign.Center, "right" => TextHAlign.Right, _ => TextHAlign.Left };
        var valign = text.VerticalAlign switch { "top" => TextVAlign.Top, "bottom" => TextVAlign.Bottom, _ => TextVAlign.Center };

        // Squeezed, never stretched, to the box a template allows it.
        if (text.MaxLength > 0 || text.MaxHeight > 0)
        {
            var probe = new TextStyle(width * Mm, height * Mm, 0, align, valign, 0, false, text.Italic, 1.0);
            string[] lines = full.Split('\n');
            double measured = lines.Max(l => text.Face is { } f
                ? OutlineText.MeasureLine(f, text.Bold, l, probe)
                : StrokeTextLayout.MeasureLine(StrokeFont.Default, l, probe)) / Mm;
            double tall = height * (1 + ((lines.Length - 1) * 1.62));
            if (text.MaxLength > 0 && measured > text.MaxLength)
            {
                width *= text.MaxLength / measured;
            }

            if (text.MaxHeight > 0 && tall > text.MaxHeight)
            {
                height *= text.MaxHeight / tall;
            }
        }

        double pen = text.Bold ? Math.Min(width, height) / 5 : text.LineWidth != 0 ? text.LineWidth : setup.TextLineWidth;
        var style = new TextStyle(width * Mm, height * Mm, pen * Mm, align, valign, text.Rotation, false, text.Italic, 1.0);

        for (int j = 0; j < text.Repeat; j++)
        {
            if (j > 0 && !(inside(start(j)) && inside(end(j))))
            {
                continue;
            }

            // Each copy after the first counts on from the label as written, as KiCad's do.
            string label = j > 0 && text.Repeat > 1 && !multiline ? Increment(text.Text, j * text.IncrementLabel) : full;
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (text.Face is { } face)
            {
                OutlineText.Layout(face, text.Bold, label, start(j) * Mm, style,
                    shape => sink.Fill(shape.Outline, shape.Holes, colour), (p, q) => sink.Stroke(p, q, pen * Mm, colour));
                continue;
            }

            StrokeTextLayout.Layout(StrokeFont.Default, label, start(j) * Mm, style, (p, q) => sink.Stroke(p, q, pen * Mm, colour));
        }
    }

    private static void DrawPolygon(
        WksPolygon polygon,
        Func<int, Vector2D> start,
        Func<Vector2D, bool> inside,
        IDrawingSheetSink sink)
    {
        var outlines = polygon.Outlines.Select(o => o.Select(c => Rotate(c.X, c.Y, polygon.Rotation)).ToList()).ToList();
        var corners = outlines.SelectMany(o => o).ToList();
        if (corners.Count == 0)
        {
            return;
        }

        var min = new Vector2D(corners.Min(c => c.X), corners.Min(c => c.Y));
        var max = new Vector2D(corners.Max(c => c.X), corners.Max(c => c.Y));
        double pen = polygon.LineWidth * Mm;

        for (int j = 0; j < polygon.Repeat; j++)
        {
            var at = start(j);
            if (j > 0 && !(inside(at + min) && inside(at + max)))
            {
                continue;
            }

            foreach (var outline in outlines)
            {
                var points = outline.Select(c => (at + c) * Mm).ToList();
                sink.Fill(points, [], null);
                if (pen > 0)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        sink.Stroke(points[i], points[(i + 1) % points.Count], pen, null);
                    }
                }
            }
        }
    }

    private sealed class CallbackSink(
        Action<Vector2D, Vector2D, double> segment,
        Action<IReadOnlyList<Vector2D>>? fill,
        Action<Vector2D, Vector2D, byte[]>? picture) : IDrawingSheetSink
    {
        public void Stroke(Vector2D a, Vector2D b, double width, ColorRgba? colour) => segment(a, b, width);

        public void Fill(IReadOnlyList<Vector2D> outline, IReadOnlyList<IReadOnlyList<Vector2D>> holes, ColorRgba? colour) =>
            fill?.Invoke(outline);

        public void Picture(Vector2D centre, Vector2D size, byte[] image) => picture?.Invoke(centre, size, image);
    }

    /// <summary>KiCad's RotatePoint, in the drawing sheet's y-down frame.</summary>
    private static Vector2D Rotate(double x, double y, double degrees)
    {
        double r = degrees * Math.PI / 180, sin = Math.Sin(r), cos = Math.Cos(r);
        return new Vector2D((x * cos) + (y * sin), (y * cos) - (x * sin));
    }

    /// <summary>"1" moved on by 3 is "4", and by 9 is "10"; "A" moved on by 2 is "C".</summary>
    public static string Increment(string label, int by)
    {
        if (label.Length == 0)
        {
            return label;
        }

        char last = label[^1];
        return last is >= '0' and <= '9'
            ? label[..^1] + (by + (last - '0')).ToString(CultureInfo.InvariantCulture)
            : label[..^1] + (char)(by + last);
    }

    /// <summary>A backslash-n written in a template is a line break; a doubled backslash is one backslash.</summary>
    private static string Unescape(string text)
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is 'n' or '\\')
            {
                result.Append(text[++i] == 'n' ? '\n' : '\\');
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Fills in <c>${NAME}</c>: the page's own names first, then the title block, then the project's variables. A
    /// title block field may itself name a variable, so its value is filled in once more.
    /// </summary>
    public static string Expand(string text, SchTitleBlock block, string paperName, SheetFrameText frame, int depth = 0) =>
        depth > 3 ? text : VariablePattern().Replace(text, m =>
        {
            string name = m.Groups[1].Value;
            string? value = name switch
            {
                "KICAD_VERSION" => frame.Application,
                "#" => frame.Page.ToString(CultureInfo.InvariantCulture),
                "##" => frame.PageCount.ToString(CultureInfo.InvariantCulture),
                "SHEETNAME" => frame.SheetName,
                "SHEETPATH" => frame.SheetPath,
                "FILENAME" or "FILEPATH" => frame.FileName,
                "PAPER" => paperName,
                "LAYER" => string.Empty,
                "ISSUE_DATE" => block.Date ?? string.Empty,
                "CURRENT_DATE" => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "REVISION" => block.Revision ?? string.Empty,
                "TITLE" => block.Title ?? string.Empty,
                "COMPANY" => block.Company ?? string.Empty,
                _ when name.StartsWith("COMMENT", StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int n) => block.Comment(n),
                _ => frame.Variables.TryGetValue(name, out var variable) ? variable : null,
            };

            return value is null ? m.Value : Expand(value, block, paperName, frame, depth + 1);
        });

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();
}

/// <summary>
/// Where a drawing sheet's strokes, fills and pictures go, all in nanometres on the page. A colour of null means the
/// sheet's own; a text with a colour of its own hands it over.
/// </summary>
public interface IDrawingSheetSink
{
    void Stroke(Vector2D a, Vector2D b, double width, ColorRgba? colour);

    void Fill(IReadOnlyList<Vector2D> outline, IReadOnlyList<IReadOnlyList<Vector2D>> holes, ColorRgba? colour);

    void Picture(Vector2D centre, Vector2D size, byte[] image);
}
