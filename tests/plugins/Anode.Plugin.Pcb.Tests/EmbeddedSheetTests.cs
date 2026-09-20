using Anode.Kicad;
using Anode.Render;
using Anode.Sdk;
using Anode.Tests;

namespace Anode.Plugin.Pcb.Tests;

/// <summary>
/// A drawing sheet the board carries: the project names it <c>kicad-embed://…</c> rather than by a path, and KiCad
/// reads it out of the file's own embedded files. A name nothing answers to falls back to the default and says so.
/// </summary>
public class EmbeddedSheetTests
{
    private const string Sheet = "cern-ohl-left.kicad_wks";

    private static string Board(string block) => $$"""
        (kicad_pcb (version 20241229) (generator "pcbnew")
        	(layers (0 "F.Cu" signal) (5 "F.SilkS" user) (25 "Edge.Cuts" user))
        	(gr_rect (start 0 0) (end 100 80) (stroke (width 0.1) (type default)) (fill no) (layer "Edge.Cuts"))
        	(embedded_fonts no)
        	{{block}})

        """;

    private static string Project(string written) => $$"""
        {
          "board": {},
          "pcbnew": { "page_layout_descr_file": "{{written}}" },
          "schematic": { "page_layout_descr_file": "" },
          "text_variables": {}
        }
        """;

    [Fact]
    public async Task A_board_draws_the_sheet_it_carries_itself()
    {
        string template = TestData.FullPath("demos/vme-wren/cern-ohl-left.kicad_wks");
        Assert.SkipUnless(File.Exists(template), TestData.SkipReason);
        byte[] data = File.ReadAllBytes(template);

        string folder = Directory.CreateTempSubdirectory("anode-embedded-sheet-").FullName;
        File.WriteAllText(Path.Combine(folder, "carried.kicad_pro"), Project("kicad-embed://" + Sheet));
        string path = Path.Combine(folder, "carried.kicad_pcb");
        File.WriteAllText(path, Board(EmbeddedFile.Block(Sheet, "worksheet", data)));

        try
        {
            var type = new PcbDocumentType(new QuietLog());
            using var document = (PcbDocument)await type.OpenAsync(path, TestContext.Current.CancellationToken);

            // Drawn from the carried template, not KiCad's default, and nothing to complain about.
            Assert.DoesNotContain(document.Issues, i => i.Title == Tr.T("pcb.issue.drawingSheet.title"));
            var carried = Anode.Kicad.DrawingSheets.DrawingSheetFile.Load(template);
            var frame = document.Scene.Layers.Single(l => l.Name == LayerStyle.PageFrame);
            var plain = Frame(Board(string.Empty), folder, "plain");

            Assert.NotEqual(plain, frame.Lines.Count);
            Assert.NotEmpty(carried.Items);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_name_the_board_does_not_carry_falls_back_and_says_so()
    {
        string folder = Directory.CreateTempSubdirectory("anode-embedded-sheet-").FullName;
        File.WriteAllText(Path.Combine(folder, "missing.kicad_pro"), Project("kicad-embed://no-such.kicad_wks"));
        string path = Path.Combine(folder, "missing.kicad_pcb");
        File.WriteAllText(path, Board(string.Empty));

        try
        {
            var type = new PcbDocumentType(new QuietLog());
            using var document = (PcbDocument)await type.OpenAsync(path, TestContext.Current.CancellationToken);

            var issue = Assert.Single(document.Issues, i => i.Title == Tr.T("pcb.issue.drawingSheet.title"));
            Assert.Contains("no-such.kicad_wks", issue.Detail, StringComparison.Ordinal);
            Assert.NotEmpty(document.Scene.Layers.Single(l => l.Name == LayerStyle.PageFrame).Lines);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>How many strokes the frame of <paramref name="text"/> has, drawn from a folder of its own.</summary>
    private static int Frame(string text, string folder, string name)
    {
        string path = Path.Combine(folder, name + ".kicad_pcb");
        File.WriteAllText(path, text);
        var frame = SheetFrameText.ForProject(path, board: true, out _, _ => null);
        return SceneBuilder.Build(Anode.Kicad.Board.Load(path), frame)
            .Layers.Single(l => l.Name == LayerStyle.PageFrame).Lines.Count;
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
