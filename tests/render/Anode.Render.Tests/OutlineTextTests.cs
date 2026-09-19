using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Render.Fonts;
using SkiaSharp;

namespace Anode.Render.Tests;

/// <summary>
/// Text in a face, as KiCad draws it: filled outlines with their counters open, capitals about as tall as the text
/// height asks, placed by KiCad's line rules, shaped with kerning, and a stand-in when the face is not on this machine.
/// </summary>
public class OutlineTextTests
{
    private const double H = 2e6;

    /// <summary>A face this machine has: Helvetica on macOS, otherwise whatever is installed first.</summary>
    private static string Face { get; } = new[] { "Helvetica", "Arial", "DejaVu Sans", "Liberation Sans" }
        .Concat(SKFontManager.Default.FontFamilies)
        .First(OutlineText.IsInstalled);

    private static TextStyle Style(TextHAlign h = TextHAlign.Left, TextVAlign v = TextVAlign.Center, bool mirrored = false, double angle = 0) =>
        new(H, H, H / 8, h, v, angle, mirrored);

    private static List<OutlineText.Shape> Layout(string text, TextStyle? style = null, string? face = null) =>
        Layout(text, out _, style, face);

    private static List<OutlineText.Shape> Layout(string text, out List<(Vector2D A, Vector2D B)> bars, TextStyle? style = null, string? face = null)
    {
        var shapes = new List<OutlineText.Shape>();
        var found = new List<(Vector2D, Vector2D)>();
        OutlineText.Layout(face ?? Face, false, text, default, style ?? Style(), shapes.Add, (a, b) => found.Add((a, b)));
        bars = found;
        return shapes;
    }

    private static RectD Bounds(IEnumerable<OutlineText.Shape> shapes)
    {
        var r = RectD.Empty;
        foreach (var p in shapes.SelectMany(s => s.Outline))
        {
            r = r.Union(p.X, p.Y);
        }

        return r;
    }

    [Fact]
    public void A_capital_is_about_as_tall_as_the_text_height()
    {
        var box = Bounds(Layout("H"));

        // The em is 1.4 times the height, KiCad's compensation; a capital then stands close to the height itself.
        Assert.InRange(box.Height / H, 0.85, 1.15);
    }

    [Fact]
    public void The_first_baseline_is_one_height_below_the_top()
    {
        // KiCad: offset.y = size.y, then for a centred block less half of 1.17 heights.
        Assert.Equal(H, Bounds(Layout("H", Style(v: TextVAlign.Top))).MaxY, H * 0.01);
        Assert.Equal(H - (1.17 * H / 2), Bounds(Layout("H", Style())).MaxY, H * 0.01);
        Assert.Equal(H - (1.17 * H), Bounds(Layout("H", Style(v: TextVAlign.Bottom))).MaxY, H * 0.01);
    }

    [Fact]
    public void Lines_are_one_and_68_hundredths_heights_apart()
    {
        var shapes = Layout("H\nH", Style(v: TextVAlign.Top));
        var bottoms = shapes.Select(s => s.Outline.Max(p => p.Y)).Order().ToList();

        Assert.Equal(1.68 * H, bottoms[^1] - bottoms[0], H * 0.01);
    }

    [Fact]
    public void A_counter_is_a_hole_in_its_letter()
    {
        var o = Assert.Single(Layout("o"));

        Assert.Single(o.Holes);
    }

    [Fact]
    public void Kerned_pairs_close_up_as_HarfBuzz_sets_them()
    {
        var style = Style();
        double pair = OutlineText.MeasureLine(Face, false, "AV", style);
        double apart = OutlineText.MeasureLine(Face, false, "A", style) + OutlineText.MeasureLine(Face, false, "V", style);

        Assert.True(pair < apart, $"AV {pair} is not narrower than A + V {apart}");
    }

    [Fact]
    public void Right_and_centre_alignment_hang_the_line_off_its_anchor()
    {
        double width = OutlineText.MeasureLine(Face, false, "Anode", Style());

        Assert.InRange(Bounds(Layout("Anode", Style(TextHAlign.Right))).MaxX, -width * 0.1, 0);
        var centred = Bounds(Layout("Anode", Style(TextHAlign.Center)));
        Assert.Equal(0, (centred.MinX + centred.MaxX) / 2, width * 0.05);
    }

    [Fact]
    public void A_mirrored_text_reads_leftwards_from_its_anchor()
    {
        var box = Bounds(Layout("Anode", Style(mirrored: true)));

        Assert.True(box.MaxX <= H * 0.05 && box.MinX < -H, $"mirrored text spans {box.MinX}..{box.MaxX}");
    }

    [Fact]
    public void An_overbar_runs_over_its_letters_and_a_subscript_sits_small_and_low()
    {
        Layout("~{RESET}", out var bars, Style(v: TextVAlign.Top));
        var bar = Assert.Single(bars);
        Assert.Equal(H - (1.23 * H), bar.A.Y, H * 0.01);

        var plain = Bounds(Layout("H", Style(v: TextVAlign.Top)));
        var sub = Bounds(Layout("_{H}", Style(v: TextVAlign.Top)));
        Assert.Equal(0.64, sub.Height / plain.Height, 0.02);
        Assert.Equal(H + (0.25 * 0.64 * 1.4 * H), sub.MaxY, H * 0.01);
    }

    [Fact]
    public void A_face_this_machine_does_not_have_is_stood_in_for()
    {
        Assert.False(OutlineText.IsInstalled("No Such Face Anode"));
        Assert.NotNull(OutlineText.Substitute("No Such Face Anode"));
        Assert.Null(OutlineText.Substitute(Face));

        // Drawn all the same, as KiCad draws a substituted face.
        Assert.NotEmpty(Layout("x", face: "No Such Face Anode"));
    }

    [Fact]
    public void A_missing_monospaced_face_is_stood_in_for_by_a_monospaced_one()
    {
        string? standIn = OutlineText.Substitute("No Such Mono Anode");
        Assert.SkipWhen(standIn is null || standIn == OutlineText.Substitute("No Such Face Anode"), "no monospaced face installed");

        var style = Style();
        Assert.Equal(
            OutlineText.MeasureLine("No Such Mono Anode", false, "iiii", style),
            OutlineText.MeasureLine("No Such Mono Anode", false, "MMMM", style),
            H * 0.01);
    }

    [Fact]
    public void A_template_text_keeps_its_colour_whether_its_face_is_here_or_stood_in_for()
    {
        var template = DrawingSheetFile.Parse("""
            (kicad_wks
              (tbtext "Stroke" (pos 50 20) (font (color 30 64 255 1)))
              (tbtext "Missing" (pos 50 30) (font (face "No Such Face Anode") (color 200 0 0 1))))
            """);
        var sink = new Recording();

        DrawingSheet.Draw(new Vector2L(297_000_000, 210_000_000), SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, sink);

        Assert.Contains(sink.Strokes, c => c == new ColorRgba(30, 64, 255));
        Assert.DoesNotContain(sink.Strokes, c => c == new ColorRgba(200, 0, 0));
        Assert.NotEmpty(sink.Fills);
        Assert.All(sink.Fills, c => Assert.Equal(new ColorRgba(200, 0, 0), c));
    }

    [Fact]
    public void A_template_text_in_an_installed_face_is_filled_in_its_colour()
    {
        var template = DrawingSheetFile.Parse($$"""
            (kicad_wks (tbtext "Anode" (pos 50 20) (font (face "{{Face}}") (size 3 3) (color 30 64 255 0.5))))
            """);
        var sink = new Recording();

        DrawingSheet.Draw(new Vector2L(297_000_000, 210_000_000), SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, sink);

        Assert.Empty(sink.Strokes);
        Assert.NotEmpty(sink.Fills);
        Assert.All(sink.Fills, c => Assert.Equal(new ColorRgba(30, 64, 255, 128), c));
    }

    [Fact]
    public void Coloured_text_gets_a_layer_of_its_own_and_is_not_drawn_twice_on_a_redraw()
    {
        var template = DrawingSheetFile.Parse("(kicad_wks (tbtext \"Blue\" (pos 50 20) (font (color 30 64 255 1))) (line (start 10 10) (end 20 10)))");
        var sheet = Schematic.Parse("(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\"))");
        var scene = SchematicSceneBuilder.Build(sheet, frame: new SheetFrameText { Template = template });

        var blue = Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame + "/1E40FFFF");
        Assert.Equal(new ColorRgba(30, 64, 255), blue.Color);
        Assert.Equal(LayerStyle.DrawOrder(LayerStyle.Sch.Frame), blue.DrawOrder);
        int strokes = blue.Lines.Count;

        SchematicSceneBuilder.RedrawFrame(scene);

        Assert.Equal(strokes, Assert.Single(scene.Layers, l => l.Name == blue.Name).Lines.Count);
        Assert.Single(Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame).Lines);
    }

    private sealed class Recording : IDrawingSheetSink
    {
        public List<ColorRgba?> Strokes { get; } = [];

        public List<ColorRgba?> Fills { get; } = [];

        public void Stroke(Vector2D a, Vector2D b, double width, ColorRgba? colour) => Strokes.Add(colour);

        public void Fill(IReadOnlyList<Vector2D> outline, IReadOnlyList<IReadOnlyList<Vector2D>> holes, ColorRgba? colour) => Fills.Add(colour);

        public void Picture(Vector2D centre, Vector2D size, byte[] image)
        {
        }
    }
}
