using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;
using Anode.Render.Fonts;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// What the inspector says when nothing is selected: the sheet itself. Its paper and title block, what is drawn on
/// it, how its nets and numbering stand, and what the checks found — the questions one asks of a sheet before
/// clicking anything on it.
/// </summary>
internal static class SheetOverview
{
    /// <param name="paper">The paper on the canvas, in millimetres; empty when unknown.</param>
    /// <param name="instance">The place on show, when the sheet appears in a hierarchy.</param>
    /// <param name="actions">What the footer offers; the document decides, it knows its commands.</param>
    public static SelectionInfo Build(
        Anode.Kicad.Schematic sheet,
        string? filePath,
        RectD paper,
        string? instance,
        IReadOnlyList<SheetInstance> appearances,
        IReadOnlyList<SchNet> nets,
        IReadOnlyList<Issue> issues,
        IReadOnlyList<InspectorAction> actions,
        Action<string, string>? editTitleBlock = null,
        IReadOnlyList<DocumentFonts.Use>? fonts = null,
        Action<bool>? embedFonts = null)
    {
        string file = Path.GetFileName(filePath ?? string.Empty);
        var shown = appearances.FirstOrDefault(a => a.Path == instance);
        string title = appearances.Count > 1 && shown is not null
            ? shown.Name
            : Path.GetFileNameWithoutExtension(filePath ?? Tr.T("sch.document.untitled"));

        List<InspectorBlock> blocks =
            [SheetBlock(sheet, paper, appearances, shown, editTitleBlock), Contents(sheet), Electrics(sheet, nets, instance)];
        if (Fonts(sheet.Document.Root, fonts ?? [], embedFonts) is { } faces)
        {
            blocks.Add(faces);
        }

        if (Checks(issues) is { } checks)
        {
            blocks.Add(checks);
        }

        return new SelectionInfo(title, file.Length > 0 ? file : null, [], Tr.T("sch.item.sheet"))
        {
            Blocks = blocks,
            Actions = actions,
        };
    }

    private static InspectorBlock SheetBlock(
        Anode.Kicad.Schematic sheet,
        RectD paper,
        IReadOnlyList<SheetInstance> appearances,
        SheetInstance? shown,
        Action<string, string>? editTitleBlock)
    {
        List<InspectorRow> rows = [];

        string size = paper.IsEmpty
            ? sheet.Paper
            : $"{sheet.Paper} · {Mm(paper.Width)} × {Mm(paper.Height)} {Tr.T("sch.units.mm")}";
        rows.Add(Row("paper", sheet.IsPortrait ? size + " · " + Tr.T("sch.overview.portrait") : size));

        var block = sheet.TitleBlock;
        if (editTitleBlock is { } edit)
        {
            // All four, empty ones too: an empty field that can be written is an invitation, not a missing value.
            rows.Add(Row("title", block.Title ?? string.Empty) with { Commit = v => edit("title", v) });
            rows.Add(Row("revision", block.Revision ?? string.Empty) with { Commit = v => edit("rev", v) });
            rows.Add(Row("date", block.Date ?? string.Empty) with { Commit = v => edit("date", v) });
            rows.Add(Row("company", block.Company ?? string.Empty) with { Commit = v => edit("company", v) });

            // The four the default drawing sheet prints, always; KiCad keeps nine, so the rest when a file uses them.
            for (int i = 1; i <= TitleBlockWrites.CommentCount; i++)
            {
                int number = i;
                if (number <= 4 || block.Comment(number).Length > 0)
                {
                    rows.Add(new InspectorRow(Tr.T("sch.overview.comment", number), block.Comment(number))
                    {
                        Commit = v => edit("comment" + number.ToString(CultureInfo.InvariantCulture), v),
                    });
                }
            }
        }
        else
        {
            // Read only, then only what the title block says; four empty rows would read as four things missing.
            AddIf(rows, "title", block.Title);
            AddIf(rows, "revision", block.Revision);
            AddIf(rows, "date", block.Date);
            AddIf(rows, "company", block.Company);
        }

        if (appearances.Count > 1 && shown is not null)
        {
            int index = appearances.ToList().IndexOf(shown) + 1;
            rows.Add(Row("place", Tr.T("sch.overview.placeOf", index, appearances.Count)));
        }

        // KiCad's own editor saved it: say which KiCad, not the editor's internal name and the format's date code.
        string format = sheet.Generator is "eeschema" && sheet.GeneratorVersion is { Length: > 0 } release
            ? $"KiCad {release}"
            : $"{sheet.Generator ?? "?"} · {sheet.Version.ToString(CultureInfo.InvariantCulture)}";
        rows.Add(Row("format", format));

        return new InspectorBlock(Tr.T("sch.overview.block.sheet"), rows);
    }

    private static InspectorBlock Contents(Anode.Kicad.Schematic sheet)
    {
        int power = sheet.Symbols.Count(s => s.Definition?.IsPower == true);
        var wires = sheet.Wires.Where(w => !w.IsBus).ToList();
        int buses = sheet.Wires.Count - wires.Count;
        long length = wires.Sum(w => w.Points.Zip(w.Points.Skip(1)).Sum(s => (long)Math.Round(Distance(s.First, s.Second))));

        List<InspectorRow> rows =
        [
            Row("parts", Count(sheet.Symbols.Count - power)),
            Row("wires", wires.Count == 0 ? "0" : $"{wires.Count} · {Mm(Units.NmToMm(length))} {Tr.T("sch.units.mm")}"),
        ];

        // The rest only when there is some: a sheet with no buses has nothing to say about buses.
        AddIf(rows, "power", power);
        AddIf(rows, "buses", buses);
        AddIf(rows, "labels", sheet.Labels.Count);
        AddIf(rows, "junctions", sheet.Junctions.Count);
        AddIf(rows, "noConnects", sheet.NoConnects.Count);
        AddIf(rows, "sheets", sheet.Sheets.Count);
        AddIf(rows, "notes", sheet.Texts.Count + sheet.Graphics.Count);

        return new InspectorBlock(Tr.T("sch.overview.block.contents"), rows);
    }

    private static InspectorBlock Electrics(Anode.Kicad.Schematic sheet, IReadOnlyList<SchNet> nets, string? instance)
    {
        int named = nets.Count(n => n.IsNamed);
        int waiting = SchAnnotation.Unannotated(sheet, instance).Count(s => s.Definition?.IsPower != true);

        return new InspectorBlock(Tr.T("sch.block.electrics"),
        [
            Row("nets", Tr.T("sch.overview.netsNamed", nets.Count, named)),
            Row("unannotated", Count(waiting)) with { IsUnresolved = waiting > 0 },
        ]);
    }

    private static InspectorBlock? Checks(IReadOnlyList<Issue> issues)
    {
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count - errors;
        if (issues.Count == 0)
        {
            return null;
        }

        return new InspectorBlock(Tr.T("sch.block.checks"),
        [
            Row("errors", Count(errors)) with { IsUnresolved = errors > 0 },
            Row("warnings", Count(warnings)),
        ])
        {
            IsAlert = errors > 0,
        };
    }

    /// <summary>
    /// The faces the design's texts are set in and where each comes from — carried in the file, installed here,
    /// held back by its licence, or stood in for — and, on the sheet that keeps KiCad's setting, whether they are
    /// carried on saving. Absent when no text uses a face and the sheet carries none.
    /// </summary>
    private static InspectorBlock? Fonts(Anode.Sexpr.SList root, IReadOnlyList<DocumentFonts.Use> fonts, Action<bool>? embed)
    {
        bool? wanted = EmbeddedFonts.Wanted(root);
        var carried = EmbeddedFonts.Names(root);
        if (fonts.Count == 0 && carried.Count == 0 && wanted is not true)
        {
            return null;
        }

        List<InspectorRow> rows = [.. fonts.Select(u => new InspectorRow(
            string.Join(" ", new[] { u.Face, u.Bold ? Tr.T("sch.overview.fonts.bold") : null, u.Italic ? Tr.T("sch.overview.fonts.italic") : null }.OfType<string>()),
            u.File switch
            {
                null => Tr.T("sch.overview.fonts.standIn", OutlineText.Substitute(u.Face) ?? string.Empty),
                { } file when carried.Contains(file.Name) => Tr.T("sch.overview.fonts.carried"),
                { MayTravel: false } => Tr.T("sch.overview.fonts.restricted"),
                _ => Tr.T("sch.overview.fonts.installed"),
            })
        {
            IsUnresolved = u.File is null,
        })];

        if (wanted is { } on && embed is not null)
        {
            rows.Add(new InspectorRow(Tr.T("sch.overview.fonts.embed"), Tr.T("sch.overview.fonts.onSave"))
            {
                Switch = on,
                Commit = v => embed(v == "yes"),
            });
        }

        return new InspectorBlock(Tr.T("sch.overview.fonts.title"), rows);
    }

    private static InspectorRow Row(string key, string value) => new(Tr.T($"sch.overview.{key}"), value);

    private static void AddIf(List<InspectorRow> rows, string key, string? value)
    {
        if (value is { Length: > 0 })
        {
            rows.Add(Row(key, value));
        }
    }

    private static void AddIf(List<InspectorRow> rows, string key, int count)
    {
        if (count > 0)
        {
            rows.Add(Row(key, Count(count)));
        }
    }

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string Mm(double mm) => mm.ToString("0.#", CultureInfo.InvariantCulture);

    private static double Distance(Vector2L a, Vector2L b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}
