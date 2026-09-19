using Anode.Geometry;
using Anode.Kicad;
using Anode.Render.Fonts;
using Anode.Tests;

namespace Anode.Render.Tests;

/// <summary>
/// Texts of a sheet and a board that name a face become filled letters of their owner, on their own layer, while
/// texts without one keep the stroke font — on hand-written files and on the demos that use faces.
/// </summary>
public class SceneFontTests
{
    private const string Sheet = """
        (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
          (text "Faced" (exclude_from_sim no) (at 50 50 0)
            (effects (font (face "Helvetica") (size 2.54 2.54) (bold yes)) (justify left))
            (uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01"))
          (text "Stroked" (exclude_from_sim no) (at 50 70 0)
            (effects (font (size 2.54 2.54)) (justify left))
            (uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a02")))
        """;

    private const string Board = """
        (kicad_pcb (version 20241229) (generator "pcbnew")
          (layers (0 "F.Cu" signal) (2 "B.Cu" signal) (5 "F.SilkS" user) (7 "B.SilkS" user) (25 "Edge.Cuts" user))
          (gr_text "Faced" (at 10 10 0) (layer "F.SilkS")
            (effects (font (face "Helvetica") (size 1.5 1.5) (thickness 0.2))))
          (gr_text "Back" (at 10 20 0) (layer "B.SilkS")
            (effects (font (face "Helvetica") (size 1.5 1.5) (thickness 0.2)) (justify left mirror)))
          (gr_text "Stroked" (at 10 30 0) (layer "F.SilkS")
            (effects (font (size 1.5 1.5) (thickness 0.2)))))
        """;

    [Fact]
    public void A_sheet_text_in_a_face_is_filled_letters_of_its_own()
    {
        var sheet = Schematic.Parse(Sheet);
        var scene = SchematicSceneBuilder.Build(sheet);
        var layer = scene.Layers.Single(l => l.Name == LayerStyle.Sch.Text);

        var faced = scene.OwnersOf(sheet.Texts[0]).ToHashSet();
        var stroked = scene.OwnersOf(sheet.Texts[1]).ToHashSet();

        Assert.Contains(layer.Polygons, p => faced.Contains(p.Owner));
        Assert.DoesNotContain(layer.Lines, l => faced.Contains(l.Owner));
        Assert.Contains(layer.Lines, l => stroked.Contains(l.Owner));
        Assert.DoesNotContain(layer.Polygons, p => stroked.Contains(p.Owner));

        // Its bounds are those of its letters, so it can be picked and boxed like any other text.
        var box = scene.BoundsOf(sheet.Texts[0]);
        var anchor = scene.ToScene(sheet.Texts[0].Position.ToDouble());
        Assert.InRange(box.MinX - anchor.X, -0.5, 0.5);
        Assert.True(box.Width > 5, $"the text is {box.Width} mm wide");
    }

    [Fact]
    public void Bold_and_italic_reach_the_stroke_font_on_a_sheet()
    {
        string sheet = Sheet.Replace("(face \"Helvetica\") ", "", StringComparison.Ordinal);
        var scene = SchematicSceneBuilder.Build(Schematic.Parse(sheet));
        var widths = scene.Layers.Single(l => l.Name == LayerStyle.Sch.Text).Lines.Select(l => l.Width).Distinct().Order().ToList();

        // An eighth of 2.54 mm for the plain text, a fifth for the bold one.
        Assert.Equal([2.54f / 8, 2.54f / 5], widths, (a, b) => Math.Abs(a - b) < 1e-4);
    }

    [Fact]
    public void A_board_text_in_a_face_is_filled_and_a_mirrored_one_reads_backwards()
    {
        var board = Kicad.Board.Parse(Board);
        var scene = SceneBuilder.Build(board);
        var front = scene.Find("F.SilkS")!;
        var back = scene.Find("B.SilkS")!;

        var faced = scene.OwnersOf(board.Texts[0]).ToHashSet();
        Assert.Contains(front.Polygons, p => faced.Contains(p.Owner));
        Assert.DoesNotContain(front.Polygons, p => scene.OwnersOf(board.Texts[2]).Contains(p.Owner));

        // Left-justified and mirrored: the letters run from the anchor towards smaller x.
        var mirrored = back.Polygons.Select(p => p.Bounds).Aggregate(RectD.Empty, (r, b) => r.Union(b));
        var anchor = scene.ToScene(board.Texts[1].BoardPosition.ToDouble());
        Assert.True(mirrored.MaxX <= anchor.X + 0.1 && mirrored.MinX < anchor.X - 2, $"mirrored text spans {mirrored.MinX}..{mirrored.MaxX}, anchor {anchor.X}");
    }

    [Theory]
    [InlineData("demos/cm5_minima/CM5.kicad_sch")]
    public void A_demo_sheet_draws_its_faced_text_in_letters(string file)
    {
        string path = TestData.FullPath(file);
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var sheet = Schematic.Load(path);
        var scene = SchematicSceneBuilder.Build(sheet);
        var text = Assert.Single(sheet.Texts, t => t.Font.Face is not null);
        var owners = scene.OwnersOf(text).ToHashSet();

        Assert.Equal("Avenir Black", text.Font.Face);
        Assert.Contains(scene.Layers.SelectMany(l => l.Polygons), p => owners.Contains(p.Owner));
    }

    [Theory]
    [InlineData("demos/jetson-agx-thor-baseboard/jetson-agx-thor-baseboard.kicad_pcb")]
    [InlineData("demos/tiny_tapeout/tinytapeout-demo.kicad_pcb")]
    public void A_demo_board_draws_its_faced_texts_in_letters(string file)
    {
        string path = TestData.FullPath(file);
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var board = Kicad.Board.Load(path);
        var scene = SceneBuilder.Build(board);
        var faced = board.Texts.Where(t => t.FontFace is not null && !t.IsHidden && t.DisplayValue.Trim().Length > 0).ToList();
        Assert.NotEmpty(faced);

        var polygons = scene.Layers.SelectMany(l => l.Polygons).Select(p => p.Owner).ToHashSet();
        Assert.All(faced, t => Assert.Contains(scene.OwnersOf(t.TopLevel), polygons.Contains));
    }

    /// <summary>
    /// KiCad saves the letters it drew beside every text in a face. Laid out afresh — in the face, or a stand-in when
    /// this machine lacks it — each upright text must sit where KiCad put it: its anchored edge within a twentieth of
    /// its height, its top and bottom within a fifth. Only the far edge may differ, by how much wider the stand-in is.
    /// </summary>
    [Theory]
    [InlineData("demos/tiny_tapeout/tinytapeout-demo.kicad_pcb")]
    [InlineData("demos/jetson-agx-thor-baseboard/jetson-agx-thor-baseboard.kicad_pcb")]
    [InlineData("demos/royalblue54L_feather/RoyalBlue54L-Feather.kicad_pcb")]
    public void A_face_is_laid_out_where_KiCad_drew_it(string file)
    {
        string path = TestData.FullPath(file);
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var upright = Kicad.Board.Load(path).Texts
            .Where(t => t.RenderCache is { } c && c.Text == t.DisplayValue && t.DrawAngle == 0 && !t.IsMirrored)
            .ToList();
        Assert.NotEmpty(upright);

        foreach (var text in upright)
        {
            var kicad = RectD.Empty;
            foreach (var p in text.RenderCache!.Polygons.SelectMany(g => g.Outline))
            {
                kicad = kicad.Union(p.X, p.Y);
            }

            var style = new TextStyle(text.Size.X, text.Size.Y, text.PenWidth,
                text.HorizontalJustify switch { "left" => TextHAlign.Left, "right" => TextHAlign.Right, _ => TextHAlign.Center },
                text.VerticalJustify switch { "top" => TextVAlign.Top, "bottom" => TextVAlign.Bottom, _ => TextVAlign.Center },
                0, false, text.IsItalic, text.LineSpacing);
            var ours = RectD.Empty;
            OutlineText.Layout(text.FontFace!, text.IsBold, text.DisplayValue, text.BoardPosition.ToDouble(), style,
                s => { foreach (var p in s.Outline) { ours = ours.Union(p.X, p.Y); } },
                (a, b) => ours = ours.Union(a.X, a.Y).Union(b.X, b.Y));

            double height = text.Size.Y;
            string what = $"'{text.DisplayValue}' in {text.FontFace}";
            double anchored = text.HorizontalJustify switch
            {
                "left" => ours.MinX - kicad.MinX,
                "right" => ours.MaxX - kicad.MaxX,
                _ => ((ours.MinX + ours.MaxX) - (kicad.MinX + kicad.MaxX)) / 2,
            };
            Assert.True(Math.Abs(anchored) < height / 20, $"{what}: anchored edge off by {anchored / 1e6:F3} mm");
            Assert.True(Math.Abs(ours.MinY - kicad.MinY) < height / 5, $"{what}: top off by {(ours.MinY - kicad.MinY) / 1e6:F3} mm");
            Assert.True(Math.Abs(ours.MaxY - kicad.MaxY) < height / 5, $"{what}: bottom off by {(ours.MaxY - kicad.MaxY) / 1e6:F3} mm");
        }
    }

    [Fact]
    public void A_board_draws_the_letters_KiCad_saved_while_they_still_show_the_text()
    {
        const string cached = """
            (kicad_pcb (version 20241229) (generator "pcbnew")
              (layers (0 "F.Cu" signal) (5 "F.SilkS" user))
              (gr_text "Hi" (at 10 10 0) (layer "F.SilkS")
                (effects (font (face "No Such Face Anode") (size 1 1) (thickness 0.1)))
                (render_cache "Hi" 0
                  (polygon (pts (xy 9 9) (xy 9.5 9) (xy 9.5 10) (xy 9 10)))
                  (polygon (pts (xy 10 9) (xy 11 9) (xy 11 10) (xy 10 10)) (pts (xy 10.2 9.2) (xy 10.8 9.2) (xy 10.8 9.8) (xy 10.2 9.8))))))
            """;

        var board = Kicad.Board.Parse(cached);
        var scene = SceneBuilder.Build(board);
        var polygons = scene.Find("F.SilkS")!.Polygons;

        Assert.Equal(2, polygons.Count);
        Assert.Equal(scene.ToScene(new Vector2D(9e6, 9e6)), polygons[0].Points[0]);
        Assert.Equal(8, polygons[1].Points.Length);
        Assert.Equal([4], polygons[1].HoleStarts);

        // Another text, or another angle, and KiCad would set it afresh; so does the scene.
        var changed = SceneBuilder.Build(Kicad.Board.Parse(cached.Replace("(gr_text \"Hi\"", "(gr_text \"Ho\"", StringComparison.Ordinal)));
        Assert.NotEmpty(changed.Find("F.SilkS")!.Polygons);
        Assert.DoesNotContain(changed.Find("F.SilkS")!.Polygons, p => p.Points[0] == changed.ToScene(new Vector2D(9e6, 9e6)));
        var turned = SceneBuilder.Build(Kicad.Board.Parse(cached.Replace("(at 10 10 0)", "(at 10 10 90)", StringComparison.Ordinal)));
        Assert.DoesNotContain(turned.Find("F.SilkS")!.Polygons, p => p.Points[0] == turned.ToScene(new Vector2D(9e6, 9e6)));
    }
}
