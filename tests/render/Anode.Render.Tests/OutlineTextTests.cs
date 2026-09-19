using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Render.Fonts;
using SkiaSharp;

namespace Anode.Render.Tests;

/// <summary>
/// Text in an installed face, as KiCad draws it: filled outlines with their counters open, capitals about as tall as
/// the text height asks, and the stroke font when the face is not on this machine.
/// </summary>
public class OutlineTextTests
{
    /// <summary>A face this machine has: Helvetica on macOS, otherwise whatever is installed first.</summary>
    private static string Face { get; } = new[] { "Helvetica", "Arial", "DejaVu Sans", "Liberation Sans" }
        .Concat(SKFontManager.Default.FontFamilies)
        .First(OutlineText.IsInstalled);

    private static IReadOnlyList<OutlineText.Shape> Layout(string text, double height = 2e6) =>
        OutlineText.Layout(Face, false, false, text, default, height, height, TextHAlign.Left, TextVAlign.Center, 0)!;

    [Fact]
    public void A_capital_is_about_as_tall_as_the_text_height()
    {
        var points = Layout("H").SelectMany(s => s.Outline).ToList();
        double tall = points.Max(p => p.Y) - points.Min(p => p.Y);

        // The em is 1.4 times the height, KiCad's compensation; a capital then stands close to the height itself.
        Assert.InRange(tall / 2e6, 0.85, 1.15);

        // Centred on its anchor, as the stroke font centres it.
        Assert.InRange((points.Max(p => p.Y) + points.Min(p => p.Y)) / 2, -0.2e6, 0.2e6);
    }

    [Fact]
    public void A_counter_is_a_hole_in_its_letter()
    {
        var o = Assert.Single(Layout("o"));

        Assert.Single(o.Holes);
    }

    [Fact]
    public void A_face_this_machine_does_not_have_answers_nothing()
    {
        Assert.False(OutlineText.IsInstalled("No Such Face Anode"));
        Assert.Null(OutlineText.Layout("No Such Face Anode", false, false, "x", default, 1, 1, TextHAlign.Left, TextVAlign.Center, 0));
    }

    [Fact]
    public void A_template_text_keeps_its_colour_and_falls_back_to_strokes_without_its_face()
    {
        var template = DrawingSheetFile.Parse("""
            (kicad_wks
              (tbtext "Stroke" (pos 50 20) (font (color 30 64 255 1)))
              (tbtext "Missing" (pos 50 30) (font (face "No Such Face Anode") (color 200 0 0 1))))
            """);
        var sink = new Recording();

        DrawingSheet.Draw(new Vector2L(297_000_000, 210_000_000), SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, sink);

        Assert.Empty(sink.Fills);
        Assert.Contains(sink.Strokes, c => c == new ColorRgba(30, 64, 255));
        Assert.Contains(sink.Strokes, c => c == new ColorRgba(200, 0, 0));
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
