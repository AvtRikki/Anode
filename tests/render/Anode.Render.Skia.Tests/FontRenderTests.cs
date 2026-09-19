using Anode.Geometry;
using Anode.Kicad;
using Anode.Tests;
using SkiaSharp;

namespace Anode.Render.Skia.Tests;

/// <summary>
/// Texts in their own faces, drawn: a sheet setting the same words in the stroke font and in a face side by side,
/// with markup, turns and several lines, and the demos that name faces. Renders to PNG for comparison by eye; the
/// assertions only make sure the letters reached the pixels.
/// </summary>
public class FontRenderTests
{
    [Fact]
    public void Stroke_and_face_side_by_side()
    {
        // Each pair shares an anchor, a size and a justification; only the face differs.
        var texts = new List<string>();
        void Pair(string text, double x, double y, string justify, double angle = 0, string extra = "")
        {
            foreach (var (dx, face) in new[] { (0.0, ""), (90.0, "(face \"Helvetica\") ") })
            {
                texts.Add($"(text \"{text}\" (exclude_from_sim no) (at {x + dx} {y} {angle}) (effects (font {face}(size 2.54 2.54){extra}) (justify {justify})) (uuid \"{Guid.NewGuid()}\"))");
            }
        }

        Pair("Anode AV To ~{RESET}", 20, 20, "left");
        Pair("V_{CC} = 3V3^{+5%}", 20, 35, "left");
        Pair("Bold italic", 20, 50, "left", extra: " (bold yes) (italic yes)");
        Pair("Two lines\\nof text", 20, 65, "left top");
        Pair("Centred", 50, 90, "");
        Pair("Right", 70, 105, "right bottom");
        Pair("Turned", 30, 150, "left", angle: 90);

        var sheet = Schematic.Parse($"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
              {string.Join("\n  ", texts)})
            """);
        var scene = SchematicSceneBuilder.Build(sheet);
        var layer = scene.Layers.Single(l => l.Name == LayerStyle.Sch.Text);
        Assert.NotEmpty(layer.Polygons);

        var bounds = layer.Bounds.Inflate(5);
        Assert.True(Render(scene, bounds, "fonts-side-by-side") > 0);
    }

    [Fact]
    public void An_embedded_face_beside_a_stand_in()
    {
        string font = TestData.FullPath("qa/resources/fonts/NotoSans-Regular.ttf");
        Assert.SkipUnless(File.Exists(font), TestData.SkipReason);

        var sheet = Schematic.Parse($$"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
              (text "Noto Sans, carried in the file: Qgy 0123" (exclude_from_sim no) (at 20 20 0)
                (effects (font (face "Noto Sans") (size 2.54 2.54)) (justify left)) (uuid "{{Guid.NewGuid()}}"))
              (text "Not carried, stood in for: Qgy 0123" (exclude_from_sim no) (at 20 30 0)
                (effects (font (face "Missing Face Anode") (size 2.54 2.54)) (justify left)) (uuid "{{Guid.NewGuid()}}"))
              (embedded_fonts yes)
              {{EmbeddedFile.Block("NotoSans-Regular.ttf", "font", File.ReadAllBytes(font))}})
            """);
        var scene = SchematicSceneBuilder.Build(sheet);

        Assert.True(Render(scene, scene.Layers.Single(l => l.Name == LayerStyle.Sch.Text).Bounds.Inflate(4), "fonts-embedded") > 0);
    }

    [Fact]
    public void The_demo_sheet_title_in_its_face()
    {
        string path = TestData.FullPath("demos/cm5_minima/CM5.kicad_sch");
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var sheet = Schematic.Load(path);
        var scene = SchematicSceneBuilder.Build(sheet);
        var text = sheet.Texts.Single(t => t.Font.Face is not null);

        Assert.True(Render(scene, scene.BoundsOf(text).Inflate(15), "fonts-cm5") > 0);
    }

    [Theory]
    [InlineData("demos/tiny_tapeout/tinytapeout-demo.kicad_pcb")]
    [InlineData("demos/jetson-agx-thor-baseboard/jetson-agx-thor-baseboard.kicad_pcb")]
    [InlineData("demos/royalblue54L_feather/RoyalBlue54L-Feather.kicad_pcb")]
    public void The_demo_board_texts_in_their_faces(string file)
    {
        string path = TestData.FullPath(file);
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var board = Board.Load(path);
        var scene = SceneBuilder.Build(board);
        var text = board.Texts.First(t => t.FontFace is not null && !t.IsHidden && t.DisplayValue.Trim().Length > 2);
        var box = scene.OwnersOf(text.TopLevel).Select(scene.OwnerBounds).Aggregate(RectD.Empty, (r, b) => r.Union(b));

        Assert.True(Render(scene, box.Inflate(Math.Max(box.Width, box.Height) * 0.6), "fonts-" + Path.GetFileNameWithoutExtension(file)) > 0);
    }

    /// <summary>Renders <paramref name="area"/> and answers how many pixels are not the background.</summary>
    private static int Render(IRenderScene scene, RectD area, string name)
    {
        const int width = 1400, height = 900;
        var camera = new Camera2D { ViewportWidth = width, ViewportHeight = height };
        camera.Fit(area);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var renderer = new SkiaSceneRenderer(scene);
        renderer.Render(surface.Canvas, new ViewState(camera.WorldToScreenTransform, camera.PixelsPerMm, width, height, false));

        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();
        var background = pixels.GetPixelColor(0, 0);
        int inked = 0;
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                if (pixels.GetPixelColor(x, y) != background)
                {
                    inked++;
                }
            }
        }

        string dir = Environment.GetEnvironmentVariable("ANODE_SNAPSHOT_DIR")
            ?? Path.Combine(Path.GetDirectoryName(TestData.KiCadDir)!, "..", "test-output", "renders");
        Directory.CreateDirectory(dir);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(Path.Combine(dir, name + ".png"));
        data.SaveTo(stream);
        return inked;
    }
}
