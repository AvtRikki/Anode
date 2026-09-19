using System.Runtime.InteropServices;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
using SkiaSharp;

namespace Anode.Render.OpenGl.Tests;

/// <summary>
/// A picture in a drawing sheet on the GPU, checked by its pixels: it shows in its own colours where the template
/// puts it, and fades with its layer when the view is dimmed.
/// </summary>
public class GlPictureTests
{
    private const int Width = 800;
    private const int Height = 400;

    [Fact]
    public void A_picture_is_drawn_in_its_own_colours_and_fades_with_its_layer()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Headless GL tests use CGL (macOS).");

        // Red over blue, 200 × 100 px, over the top band of the default title block: the halves say which way up.
        byte[] png = Halves(200, 100, SKColors.Red, SKColors.Blue);
        string text = DrawingSheetFile.DefaultText.TrimEnd()[..^1]
            + $"(bitmap (pos 75 25) (scale 1) (data \"{Convert.ToBase64String(png)}\")))";
        var sheet = Schematic.Parse("(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\"))");
        var scene = SchematicSceneBuilder.Build(sheet, frame: new SheetFrameText("t.kicad_sch") { Template = DrawingSheetFile.Parse(text) });
        var picture = Assert.Single(Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame).Images);

        using var context = CglContext.Create();
        var gl = context.Gl;
        using var target = new OffscreenTarget(gl, Width, Height);
        using var renderer = new GlSceneRenderer(gl, GlslDialect.Desktop330);
        renderer.SetScene(scene);

        var camera = new Camera2D { ViewportWidth = Width, ViewportHeight = Height };
        camera.Fit(picture.Bounds.Inflate(5));
        var view = new ViewState(camera.WorldToScreenTransform, camera.PixelsPerMm, Width, Height, camera.FlipX, ShowGrid: false);

        renderer.Render(view, target.Framebuffer, Width, Height);
        byte[] plain = target.ReadPixels();
        Save(plain, "picture");

        var top = Pixel(plain, Width / 2, (Height / 2) - 20);
        var bottom = Pixel(plain, Width / 2, (Height / 2) + 20);
        Assert.True(top.R > 240 && top.G < 15 && top.B < 15, $"the picture's upper half reads {top}");
        Assert.True(bottom.B > 240 && bottom.R < 15 && bottom.G < 15, $"the picture's lower half reads {bottom}");

        // Something selected dims the rest of the drawing; the picture fades with its layer towards the paper.
        renderer.Render(view with { SelectedOwners = new HashSet<int> { 0 } }, target.Framebuffer, Width, Height);
        var faded = Pixel(target.ReadPixels(), Width / 2, (Height / 2) - 20);
        Assert.True(faded.G > 100 && faded.B > 100, $"the dimmed picture's centre reads {faded}");
    }

    private static (byte R, byte G, byte B) Pixel(byte[] rgba, int x, int y)
    {
        int at = ((y * Width) + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2]);
    }

    private static byte[] Halves(int width, int height, SKColor upper, SKColor lower)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(lower);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = upper })
        {
            canvas.DrawRect(0, 0, width, height / 2f, paint);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Save(byte[] rgba, string name)
    {
        string dir = Environment.GetEnvironmentVariable("ANODE_SNAPSHOT_DIR")
            ?? Path.Combine(Path.GetDirectoryName(Anode.Tests.TestData.KiCadDir)!, "..", "test-output", "renders-gl");
        Directory.CreateDirectory(dir);
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(Path.Combine(dir, $"gl-{name}.png"));
        data.SaveTo(stream);
    }
}
