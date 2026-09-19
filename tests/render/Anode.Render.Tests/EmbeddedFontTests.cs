using Anode.Geometry;
using Anode.Kicad;
using Anode.Render.Fonts;
using Anode.Tests;
using SkiaSharp;

namespace Anode.Render.Tests;

/// <summary>
/// A font carried in the file, as KiCad 9 embeds it: a board or a sheet naming a face this machine lacks draws its
/// texts in the embedded font itself rather than a stand-in, and a font that fails its checksum is not used.
/// Noto Sans, from KiCad's own test resources, is the font; it is not installed here.
/// </summary>
public class EmbeddedFontTests
{
    private const string Face = "Noto Sans";

    private static string FontPath => TestData.FullPath("qa/resources/fonts/NotoSans-Regular.ttf");

    private static string Board(string block) => $$"""
        (kicad_pcb (version 20241229) (generator "pcbnew")
          (layers (0 "F.Cu" signal) (5 "F.SilkS" user))
          (gr_text "Anode" (at 10 10 0) (layer "F.SilkS")
            (effects (font (face "{{Face}}") (size 2 2) (thickness 0.2)) (justify left)))
          (embedded_fonts yes)
          {{block}})
        """;

    [Fact]
    public void A_board_draws_its_text_in_the_font_it_carries()
    {
        Assert.SkipUnless(File.Exists(FontPath), TestData.SkipReason);
        byte[] font = File.ReadAllBytes(FontPath);
        Assert.SkipWhen(SKFontManager.Default.MatchFamily(Face) is { FamilyName.Length: > 0 }, $"{Face} is installed here");

        var board = Kicad.Board.Parse(Board(EmbeddedFile.Block("NotoSans-Regular.ttf", "font", font)));
        var scene = SceneBuilder.Build(board);

        Assert.True(OutlineText.IsEmbedded(Face));
        Assert.Null(OutlineText.Substitute(Face));
        Assert.Empty(OutlineText.StandIns([Face]));

        // The letters are Noto Sans's own: the line is as wide as the font file itself sets it.
        var letters = scene.Find("F.SilkS")!.Polygons.Select(p => p.Bounds).Aggregate(RectD.Empty, (r, b) => r.Union(b));
        using var typeface = SKTypeface.FromData(SKData.CreateCopy(font));
        using var skFont = new SKFont(typeface, 1000);
        double advance = skFont.MeasureText("Anode") / 1000 * 1.4 * 2;
        Assert.Equal(advance, letters.Width, advance * 0.03);
    }

    [Fact]
    public void A_sheet_draws_its_text_in_the_font_it_carries()
    {
        Assert.SkipUnless(File.Exists(FontPath), TestData.SkipReason);
        string block = EmbeddedFile.Block("NotoSans-Regular.ttf", "font", File.ReadAllBytes(FontPath));

        var sheet = Schematic.Parse($$"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
              (text "Anode" (exclude_from_sim no) (at 50 50 0)
                (effects (font (face "{{Face}}") (size 2.54 2.54)) (justify left))
                (uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01"))
              (embedded_fonts yes)
              {{block}})
            """);
        var scene = SchematicSceneBuilder.Build(sheet);

        Assert.True(OutlineText.IsEmbedded(Face));
        Assert.NotEmpty(scene.Layers.Single(l => l.Name == LayerStyle.Sch.Text).Polygons);
    }

    [Fact]
    public void An_embedded_font_that_fails_its_checksum_is_not_used()
    {
        Assert.SkipUnless(File.Exists(FontPath), TestData.SkipReason);
        byte[] font = File.ReadAllBytes(FontPath);

        string block = EmbeddedFile.Block("Broken.ttf", "font", font);
        string checksum = EmbeddedFile.ChecksumOf(font);
        var broken = EmbeddedFile.In(Kicad.Board.Parse(Board(block.Replace(checksum, new string('0', 32), StringComparison.Ordinal))).Document.Root);

        Assert.Null(Assert.Single(broken).Data);
        Assert.Equal(0, OutlineText.Embed(broken));
    }

    [Fact]
    public void The_block_reads_back_as_KiCad_lays_it_out()
    {
        byte[] data = [.. Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 % 251))];
        string block = EmbeddedFile.Block("data.bin", "other", data);

        // Lines of 76 between bars, as KiCad writes them.
        string[] lines = [.. block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] is not ('(' or ')'))];
        Assert.StartsWith("|", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("|", lines[^1], StringComparison.Ordinal);
        Assert.All(lines[1..^1], l => Assert.Equal(76, l.Length));

        var read = Assert.Single(EmbeddedFile.In(Schematic.Parse($"(kicad_sch (version 20250114) (generator \"eeschema\") {block})").Document.Root));
        Assert.Equal(data, read.Data);
        Assert.Equal("other", read.Type);
    }
}
