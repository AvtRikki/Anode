using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using Anode.Render.Fonts;

namespace Anode.Render.OpenGl.Tests;

/// <summary>
/// A letter of an outline face on the GPU, checked by its pixels: the ring of an "O" is filled and its counter left
/// open. The triangulation joins the counter to the outline by a bridge; a mistake there would fill it.
/// </summary>
public class GlOutlineTextTests
{
    private const int Width = 600;
    private const int Height = 600;

    [Fact]
    public void The_counter_of_a_letter_stays_open()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Headless GL tests use CGL (macOS).");
        string face = new[] { "Helvetica", "Arial" }.FirstOrDefault(OutlineText.IsInstalled) ?? "";
        Assert.SkipWhen(face.Length == 0, "no common face installed");

        var template = DrawingSheetFile.Parse($$"""
            (kicad_wks (setup (left_margin 0) (right_margin 0) (top_margin 0) (bottom_margin 0))
              (tbtext "O" (pos 100 100 ltcorner) (font (face "{{face}}") (size 40 40) (color 0 0 0 1)) (justify center)))
            """);
        var sheet = Schematic.Parse("(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\"))");
        var scene = SchematicSceneBuilder.Build(sheet, frame: new SheetFrameText { Template = template });
        var letter = Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame + "/000000FF");
        SceneTriangulator.Triangulate(scene, TestContext.Current.CancellationToken);

        using var context = CglContext.Create();
        var gl = context.Gl;
        using var target = new OffscreenTarget(gl, Width, Height);
        using var renderer = new GlSceneRenderer(gl, GlslDialect.Desktop330);
        renderer.SetScene(scene);

        var camera = new Camera2D { ViewportWidth = Width, ViewportHeight = Height };
        camera.Fit(letter.Bounds.Inflate(2));
        renderer.Render(new ViewState(camera.WorldToScreenTransform, camera.PixelsPerMm, Width, Height, camera.FlipX, ShowGrid: false),
            target.Framebuffer, Width, Height);
        byte[] pixels = target.ReadPixels();

        // Middle of the letter: the counter, paper-coloured. Halfway to the left edge of its box: the ring, black.
        var counter = Pixel(pixels, Width / 2, Height / 2);
        var box = letter.Bounds;
        var ring = camera.WorldToScreenTransform.Apply(new Anode.Geometry.Vector2D(box.MinX + (box.Width * 0.06), (box.MinY + box.MaxY) / 2));
        var stroke = Pixel(pixels, (int)ring.X, (int)ring.Y);

        Assert.True(counter.R > 200 && counter.G > 200 && counter.B > 200, $"the counter reads {counter}");
        Assert.True(stroke.R < 60 && stroke.G < 60 && stroke.B < 60, $"the ring reads {stroke}");
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgba, int x, int y)
    {
        int at = ((y * Width) + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2]);
    }
}
