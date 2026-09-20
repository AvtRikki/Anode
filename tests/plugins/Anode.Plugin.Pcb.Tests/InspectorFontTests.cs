using Anode.Kicad;
using Anode.Render.Fonts;
using Anode.Sdk;

namespace Anode.Plugin.Pcb.Tests;

/// <summary>
/// The board inspector's type block: which face a text is set in and whether it is bold or italic, chosen from a
/// list and written through to the file. The rest of a board's rows are still computed, so only these offer a commit.
/// </summary>
public class InspectorFontTests
{
    private const string Board = """
        (kicad_pcb (version 20241229) (generator "pcbnew")
        	(layers (0 "F.Cu" signal) (5 "F.SilkS" user))
        	(gr_text "Anode" (at 10 10 0) (layer "F.SilkS")
        		(effects (font (size 2 2) (thickness 0.3))))
        	(footprint "Lib:Part" (layer "F.Cu") (at 20 20)
        		(property "Reference" "U1" (at 0 -2 0) (layer "F.SilkS")
        			(effects (font (size 1 1))))))
        """;

    private static string Stroke => Tr.T("pcb.property.strokeFont");

    private static List<InspectorRow> Rows(BoardItem item) =>
        [.. ItemProperties.Blocks(item, (_, mutate) => mutate()).SelectMany(b => b.Rows)];

    [Fact]
    public void A_board_text_says_which_face_it_is_set_in_and_offers_the_others()
    {
        var text = Kicad.Board.Parse(Board).Texts[0];
        var rows = Rows(text);
        var row = Assert.Single(rows, r => r.Name == Tr.T("pcb.property.font"));

        Assert.Equal(Stroke, row.Value);
        Assert.Equal(Stroke, row.Choices![0]);
        Assert.Equal(OutlineText.Families().Count + 1, row.Choices.Count);

        // The only rows a board offers to write, for now.
        Assert.Equal(
            [Tr.T("pcb.property.font"), Tr.T("pcb.property.style")],
            rows.Where(r => r.Commit is not null).Select(r => r.Name));
    }

    [Fact]
    public void Choosing_a_face_and_a_style_reaches_the_file()
    {
        var board = Kicad.Board.Parse(Board);
        var text = board.Texts[0];
        string face = OutlineText.Families()[0];

        Assert.Single(Rows(text), r => r.Name == Tr.T("pcb.property.font")).Commit!(face);
        Assert.Single(Rows(text), r => r.Name == Tr.T("pcb.property.style")).Commit!(Tr.T("pcb.style.bold"));

        Assert.Equal(face, text.FontFace);
        Assert.True(text.IsBold);
        Assert.False(text.IsItalic);

        Assert.Single(Rows(text), r => r.Name == Tr.T("pcb.property.font")).Commit!(Stroke);
        Assert.Null(text.FontFace);
        Assert.DoesNotContain("(face", board.Document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_of_a_footprint_is_set_the_same_way()
    {
        var board = Kicad.Board.Parse(Board);
        var text = board.Footprints[0].Texts[0];

        var block = ItemProperties.Typography(text, (_, mutate) => mutate());
        Assert.NotNull(block);
        Assert.Single(block!.Rows, r => r.Name == Tr.T("pcb.property.style")).Commit!(Tr.T("pcb.style.italic"));

        Assert.True(text.IsItalic);
    }

    [Fact]
    public void Anything_else_has_no_type_block_at_all()
    {
        var board = Kicad.Board.Parse(Board);

        Assert.Null(ItemProperties.Typography(board.Footprints[0], (_, mutate) => mutate()));
        Assert.DoesNotContain(Rows(board.Footprints[0]), r => r.Name == Tr.T("pcb.property.font"));
    }

    /// <summary>
    /// Through the document as the panel drives it: the text is selected, a face chosen, and the canvas has the
    /// letters filled where it had strokes — and undo puts the strokes back.
    /// </summary>
    [Fact]
    public async Task A_face_chosen_through_the_document_redraws_the_canvas_and_undoes()
    {
        string face = OutlineText.Families().FirstOrDefault(f => OutlineText.IsInstalled(f)) ?? string.Empty;
        Assert.SkipWhen(face.Length == 0, "no face installed");

        string folder = Directory.CreateTempSubdirectory("anode-pcb-font-").FullName;
        string path = Path.Combine(folder, "text.kicad_pcb");
        File.WriteAllText(path, Board + "\n");

        try
        {
            var type = new PcbDocumentType(new QuietLog());
            using var document = (PcbDocument)await type.OpenAsync(path, TestContext.Current.CancellationToken);
            var text = document.Scene.Board.Texts[0];
            document.Editor.SetSelection([text]);

            // Only this text's own primitives; the footprint's reference is on the same layer.
            (int Lines, int Polygons) Drawn()
            {
                var owners = document.Scene.OwnersOf(document.Scene.Board.Texts[0]).ToHashSet();
                var layer = document.Scene.Find("F.SilkS")!;
                return (layer.Lines.Count(l => owners.Contains(l.Owner)), layer.Polygons.Count(p => owners.Contains(p.Owner)));
            }

            Assert.Equal(0, Drawn().Polygons);
            Assert.True(Drawn().Lines > 0);

            InspectorRow Row(string key) => document.Selection!.Blocks.SelectMany(b => b.Rows).Single(r => r.Name == Tr.T(key));
            Row("pcb.property.font").Commit!(face);

            Assert.True(document.IsDirty);
            Assert.Equal(face, text.FontFace);
            Assert.True(Drawn().Polygons > 0);
            Assert.Equal(0, Drawn().Lines);

            document.Editor.Undo();
            Assert.Null(document.Scene.Board.Texts[0].FontFace);
            Assert.True(Drawn().Lines > 0);
            Assert.Equal(0, Drawn().Polygons);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class QuietLog : ILog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
