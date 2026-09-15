using System.Diagnostics;
using Ecad.KiCad;
using Ecad.Tests;
using SkiaSharp;

namespace Ecad.Rendering.Skia.Tests;

/// <summary>Renders fixture boards to PNG (test-output/renders) for visual inspection and frame timing.</summary>
public class OffscreenRenderTests(ITestOutputHelper output)
{
    private const int Width = 1600;
    private const int Height = 1000;

    public static TheoryData<string> Boards() => TestData.Files(".kicad_pcb");

    [Theory]
    [MemberData(nameof(Boards))]
    public void Board_renders_to_png(string file)
    {
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        var scene = SceneBuilder.Build(Board.Load(TestData.FullPath(file)));
        var camera = new Camera2D { ViewportWidth = Width, ViewportHeight = Height };
        camera.Fit(scene.BoardOutline.IsEmpty ? scene.Bounds : scene.BoardOutline);

        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var renderer = new SkiaSceneRenderer(scene);
        var view = new ViewState(camera.WorldToScreenTransform, camera.PixelsPerMm, Width, Height, false);

        var sw = Stopwatch.StartNew();
        renderer.Render(surface.Canvas, view);
        double first = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        renderer.Render(surface.Canvas, view);
        double second = sw.Elapsed.TotalMilliseconds;

        output.WriteLine($"{file}: first frame {first:F0} ms (builds caches), cached frame {second:F0} ms, CPU raster {Width}x{Height}");

        string dir = Path.Combine(Path.GetDirectoryName(TestData.KiCadDir)!, "..", "test-output", "renders");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".png");
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
        using (var stream = File.Create(png))
        {
            data.SaveTo(stream);
        }

        // Something other than the background must have been drawn.
        using var pixels = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(pixels);
        var bg = LayerStyle.Background;
        int drawn = 0;
        for (int y = 0; y < Height; y += 10)
        {
            for (int x = 0; x < Width; x += 10)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red != bg.R || c.Green != bg.G || c.Blue != bg.B)
                {
                    drawn++;
                }
            }
        }

        Assert.True(drawn > 50, $"Only {drawn} sampled pixels differ from background.");
    }
}
