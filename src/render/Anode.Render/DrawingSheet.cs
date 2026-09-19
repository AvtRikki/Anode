using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Render.Fonts;
using Anode.Sexpr;

namespace Anode.Render;

/// <summary>
/// KiCad's default drawing sheet — the frame and title block around a schematic, and around a board's page — as
/// strokes on the page. Written once for both kinds of file: a sheet and a board keep their paper and title block
/// the same way, and each scene only says where the strokes go.
/// </summary>
public static class DrawingSheet
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

    private enum Corner
    {
        RightBottom,
        LeftTop,
        LeftBottom,
        RightTop,
    }

    /// <summary>
    /// KiCad's default drawing sheet, item for item from its built-in description: a double border 2 mm apart,
    /// a tick every 50 mm numbered along the top and bottom and lettered down the sides, and the title block in
    /// the bottom-right corner. Distances are in millimetres from a corner of the area inside the 10 mm margins,
    /// the bottom-right one unless said otherwise; repeated items stop where they would leave that area.
    /// </summary>
    /// <param name="paper">The paper, in nanometres.</param>
    /// <param name="segment">Receives every stroke: its ends in nanometres on the page, and its pen width.</param>
    public static void Draw(Vector2L paper, SchTitleBlock block, string paperName, SheetFrameText frame, Action<Vector2D, Vector2D, double> segment)
    {
        const double margin = 10;
        double right = (paper.X / Mm) - margin, bottom = (paper.Y / Mm) - margin;

        Vector2D At(double x, double y, Corner corner = Corner.RightBottom) => corner switch
        {
            Corner.LeftTop => new Vector2D(margin + x, margin + y) * Mm,
            Corner.LeftBottom => new Vector2D(margin + x, bottom - y) * Mm,
            Corner.RightTop => new Vector2D(right - x, margin + y) * Mm,
            _ => new Vector2D(right - x, bottom - y) * Mm,
        };

        bool Inside(Vector2D p) =>
            p.X >= (margin * Mm) - 1 && p.X <= (right * Mm) + 1 && p.Y >= (margin * Mm) - 1 && p.Y <= (bottom * Mm) + 1;

        void Segment(Vector2D a, Vector2D b) => segment(a, b, LineWidth);

        void Box(Vector2D a, Vector2D b)
        {
            Segment(a, new Vector2D(b.X, a.Y));
            Segment(new Vector2D(b.X, a.Y), b);
            Segment(b, new Vector2D(a.X, b.Y));
            Segment(new Vector2D(a.X, b.Y), a);
        }

        // The border, twice, and the scale along its edges.
        Box(At(0, 0, Corner.LeftTop), At(0, 0));
        Box(At(2, 2, Corner.LeftTop), At(2, 2));

        foreach (var corner in new[] { Corner.LeftTop, Corner.LeftBottom })
        {
            for (int i = 0; i < 30; i++)
            {
                var (a, b) = (At(50 + (50 * i), 2, corner), At(50 + (50 * i), 0, corner));
                if (Inside(a) && Inside(b))
                {
                    Segment(a, b);
                }
            }

            for (int i = 0; i < 100 && Inside(At(25 + (50 * i), 1, corner)); i++)
            {
                FrameText(segment, (i + 1).ToString(CultureInfo.InvariantCulture), At(25 + (50 * i), 1, corner), 1.3, TextHAlign.Left);
            }
        }

        foreach (var corner in new[] { Corner.LeftTop, Corner.RightTop })
        {
            for (int i = 0; i < 30; i++)
            {
                var (a, b) = (At(0, 50 + (50 * i), corner), At(2, 50 + (50 * i), corner));
                if (Inside(a) && Inside(b))
                {
                    Segment(a, b);
                }
            }

            for (int i = 0; i < 26 && Inside(At(1, 25 + (50 * i), corner)); i++)
            {
                FrameText(segment, ((char)('A' + i)).ToString(), At(1, 25 + (50 * i), corner), 1.3, TextHAlign.Center);
            }
        }

        // The title block.
        Box(At(110, 34), At(2, 2));
        Segment(At(110, 5.5), At(2, 5.5));
        Segment(At(110, 8.5), At(2, 8.5));
        Segment(At(110, 12.5), At(2, 12.5));
        Segment(At(110, 18.5), At(2, 18.5));
        Segment(At(90, 8.5), At(90, 5.5));
        Segment(At(26, 8.5), At(26, 2));

        FrameText(segment, "Date: " + block.Date, At(87, 6.9));
        FrameText(segment, frame.Application, At(109, 4.1));
        FrameText(segment, "Rev: " + block.Revision, At(24, 6.9), bold: true);
        FrameText(segment, "Size: " + paperName, At(109, 6.9));
        FrameText(segment, $"Id: {frame.Page}/{frame.PageCount}", At(24, 4.1));
        FrameText(segment, "Title: " + block.Title, At(109, 10.7), size: 2, bold: true, italic: true);
        FrameText(segment, "File: " + frame.FileName, At(109, 14.3));
        FrameText(segment, "Sheet: " + frame.SheetPath, At(109, 17));
        FrameText(segment, block.Company ?? string.Empty, At(109, 20), bold: true);
        for (int i = 1; i <= 4; i++)
        {
            FrameText(segment, block.Comment(i), At(109, 20 + (3 * i)));
        }
    }

    /// <summary>Drawing-sheet text: KiCad's 1.5 mm default, left-aligned and centred on its line unless said otherwise.</summary>
    private static void FrameText(
        Action<Vector2D, Vector2D, double> segment,
        string value,
        Vector2D anchorNm,
        double size = 1.5,
        TextHAlign align = TextHAlign.Left,
        bool bold = false,
        bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        double height = size * Mm;
        double pen = bold ? height / 5 : LineWidth;
        var style = new StrokeTextStyle(height, height, pen, align, TextVAlign.Center, 0, false, italic, 1.0);
        StrokeTextLayout.Layout(StrokeFont.Default, value, anchorNm, style, (a, b) => segment(a, b, pen));
    }
}
