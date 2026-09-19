using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;
using Anode.Sdk;

namespace Anode.Plugin.Pcb;

/// <summary>
/// What the inspector says when nothing is selected: the board itself. How big it is and how many layers it has,
/// what is placed and routed on it, and what the checks found.
/// </summary>
internal static class BoardOverview
{
    /// <param name="outline">The board edge on the canvas, in millimetres; empty when the board has none.</param>
    /// <param name="editTitleBlock">Writes a title block line: "title", "date", "rev", "company" or "comment3".</param>
    public static SelectionInfo Build(
        Board board,
        string? filePath,
        RectD outline,
        IReadOnlyList<Issue> issues,
        Action<string, string>? editTitleBlock = null)
    {
        string title = Path.GetFileNameWithoutExtension(filePath ?? Tr.T("pcb.document.untitled"));
        List<InspectorBlock> blocks = [Physical(board, outline, editTitleBlock), Contents(board)];
        if (Checks(issues) is { } checks)
        {
            blocks.Add(checks);
        }

        return new SelectionInfo(title, Path.GetFileName(filePath), [], Tr.T("pcb.overview.tag")) { Blocks = blocks };
    }

    private static InspectorBlock Physical(Board board, RectD outline, Action<string, string>? edit)
    {
        string mm = Tr.T("pcb.units.mm");
        List<InspectorRow> rows =
        [
            Row("size", outline.IsEmpty ? Tr.T("pcb.overview.noOutline") : $"{Mm(outline.Width)} × {Mm(outline.Height)} {mm}"),
            Row("thickness", $"{Mm(board.Thickness)} {mm}"),
            Row("copper", Count(board.Layers.Copper.Count)),
        ];

        // The title block, as a sheet offers it: every field, empty ones too, and the comment lines the default
        // drawing sheet prints, plus any further ones the file already uses.
        var block = board.TitleBlock;
        if (edit is not null)
        {
            rows.Add(Row("title", block.Title ?? string.Empty) with { Commit = v => edit("title", v) });
            rows.Add(Row("revision", block.Revision ?? string.Empty) with { Commit = v => edit("rev", v) });
            rows.Add(Row("date", block.Date ?? string.Empty) with { Commit = v => edit("date", v) });
            rows.Add(Row("company", block.Company ?? string.Empty) with { Commit = v => edit("company", v) });
            for (int i = 1; i <= TitleBlockWrites.CommentCount; i++)
            {
                int number = i;
                if (number <= 4 || block.Comment(number).Length > 0)
                {
                    rows.Add(new InspectorRow(Tr.T("pcb.overview.comment", number), block.Comment(number))
                    {
                        Commit = v => edit("comment" + number.ToString(CultureInfo.InvariantCulture), v),
                    });
                }
            }
        }

        return new InspectorBlock(Tr.T("pcb.overview.block.board"), rows);
    }

    private static InspectorBlock Contents(Board board)
    {
        string mm = Tr.T("pcb.units.mm");
        int tracks = board.Segments.Count + board.Arcs.Count;
        double length = board.Segments.Sum(s => Distance(s.Start, s.End)) + board.Arcs.Sum(a => ArcLength(a.Start, a.Mid, a.End));

        List<InspectorRow> rows =
        [
            Row("footprints", Count(board.Footprints.Count)),
            Row("pads", Count(board.Footprints.Sum(f => f.Pads.Count))),
            Row("tracks", tracks == 0 ? "0" : $"{tracks} · {Mm(Units.NmToMm((long)Math.Round(length)))} {mm}"),
            Row("vias", Count(board.Vias.Count)),
        ];

        if (board.Zones.Count > 0)
        {
            rows.Add(Row("zones", Count(board.Zones.Count)));
        }

        rows.Add(Row("nets", Count(board.Nets.All.Count(n => !n.IsUnconnected))));
        return new InspectorBlock(Tr.T("pcb.overview.block.contents"), rows);
    }

    private static InspectorBlock? Checks(IReadOnlyList<Issue> issues)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        return new InspectorBlock(Tr.T("pcb.overview.block.checks"),
        [
            Row("errors", Count(errors)) with { IsUnresolved = errors > 0 },
            Row("warnings", Count(issues.Count - errors)),
        ])
        {
            IsAlert = errors > 0,
        };
    }

    /// <summary>
    /// The length along an arc given by three points: the radius of the circle through them times the angle swept.
    /// Its chord would understate every rounded corner of a routed board.
    /// </summary>
    internal static double ArcLength(Vector2L start, Vector2L mid, Vector2L end)
    {
        double a = Distance(start, mid), b = Distance(mid, end), c = Distance(start, end);
        double twiceArea = Math.Abs(((double)(mid.X - start.X) * (end.Y - start.Y)) - ((double)(mid.Y - start.Y) * (end.X - start.X)));
        if (twiceArea < 1e-9)
        {
            return a + b;
        }

        double radius = a * b * c / (2 * twiceArea);
        return radius * (Sweep(a) + Sweep(b));

        double Sweep(double chord) => 2 * Math.Asin(Math.Min(1, chord / (2 * radius)));
    }

    private static InspectorRow Row(string key, string value) => new(Tr.T($"pcb.overview.{key}"), value);

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string Mm(double mm) => mm.ToString("0.##", CultureInfo.InvariantCulture);

    private static double Distance(Vector2L a, Vector2L b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}
