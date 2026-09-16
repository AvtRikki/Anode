using System.Diagnostics;
using System.Runtime.InteropServices;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Tests;
using SkiaSharp;

namespace Anode.Render.OpenGl.Tests;

/// <summary>
/// Renders fixture boards through <see cref="GlSceneRenderer"/> into an offscreen framebuffer
/// (test-output/renders-gl) and measures GPU frame times with glFinish.
/// </summary>
public class GlRenderTests(ITestOutputHelper output)
{
    private const int Width = 1600;
    private const int Height = 1000;
    private const int AnimatedFrames = 60;

    [Fact]
    public void Headless_context_and_shaders_compile()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Headless GL tests use CGL (macOS).");

        using var context = CglContext.Create();
        using var renderer = new GlSceneRenderer(context.Gl, GlslDialect.Desktop330);
        output.WriteLine(context.Version);
    }

    public static TheoryData<string> Boards() => TestData.Files(".kicad_pcb");

    [Theory]
    [MemberData(nameof(Boards))]
    public void Board_renders_on_gpu(string file)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Headless GL tests use CGL (macOS).");
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        var scene = SceneBuilder.Build(Board.Load(TestData.FullPath(file)));
        var triangulation = Stopwatch.StartNew();
        SceneTriangulator.Triangulate(scene, TestContext.Current.CancellationToken);
        double triangulateMs = triangulation.Elapsed.TotalMilliseconds;

        using var context = CglContext.Create();
        var gl = context.Gl;
        using var target = new OffscreenTarget(gl, Width, Height);
        using var renderer = new GlSceneRenderer(gl, GlslDialect.Desktop330);
        renderer.SetScene(scene);

        var camera = new Camera2D { ViewportWidth = Width, ViewportHeight = Height };
        camera.Fit(scene.BoardOutline.IsEmpty ? scene.Bounds : scene.BoardOutline);

        var sw = Stopwatch.StartNew();
        renderer.Render(View(camera), target.Framebuffer, Width, Height);
        gl.Finish();
        double first = sw.Elapsed.TotalMilliseconds;

        byte[] pixels = target.ReadPixels();
        SavePng(file, pixels);

        // Fitted view, then zoom in towards the centre and pan: every frame shows a different region.
        var frameTimes = new List<double>(AnimatedFrames);
        for (int i = 0; i < AnimatedFrames; i++)
        {
            if (i < AnimatedFrames / 2)
            {
                camera.ZoomAt(new Vector2D(Width / 2.0, Height / 2.0), 1.08);
            }
            else
            {
                camera.PanPixels(25, 10);
            }

            sw.Restart();
            renderer.Render(View(camera), target.Framebuffer, Width, Height);
            gl.Finish();
            frameTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        frameTimes.Sort();
        var stats = renderer.Stats;
        output.WriteLine(
            $"{file}: triangulation {triangulateMs:F0} ms (parallel), first frame {first:F0} ms (upload {stats.LastUploadMs:F0} ms), " +
            $"median {frameTimes[frameTimes.Count / 2]:F1} ms, p95 {frameTimes[(int)(frameTimes.Count * 0.95)]:F1} ms, " +
            $"GPU buffers {stats.GpuBytes / 1024.0 / 1024.0:F1} MB, {stats.DrawCalls} draw calls, {scene.PrimitiveCount:N0} primitives");

        var bg = LayerStyle.Background;
        int drawn = 0;
        for (int i = 0; i < pixels.Length; i += 4 * 37)
        {
            if (pixels[i] != bg.R || pixels[i + 1] != bg.G || pixels[i + 2] != bg.B)
            {
                drawn++;
            }
        }

        Assert.True(drawn > 50, $"Only {drawn} sampled pixels differ from background.");
    }

    [Fact]
    public void Selection_highlight_uploads_and_draws()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Headless GL tests use CGL (macOS).");
        string path = TestData.FullPath("demos/pic_programmer/pic_programmer.kicad_pcb");
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var scene = SceneBuilder.Build(Board.Load(path));
        int padOwner = Enumerable.Range(0, scene.OwnerCount).First(i => scene.Owner(i) is Pad && scene.OwnerNet(i) is { IsUnconnected: false });

        using var context = CglContext.Create();
        using var target = new OffscreenTarget(context.Gl, Width, Height);
        using var renderer = new GlSceneRenderer(context.Gl, GlslDialect.Desktop330);
        renderer.SetScene(scene);

        var camera = new Camera2D { ViewportWidth = Width, ViewportHeight = Height };
        camera.Fit(scene.BoardOutline);
        var view = View(camera) with { SelectedOwners = new HashSet<int> { padOwner }, HighlightNet = scene.OwnerNet(padOwner) };

        renderer.Render(view, target.Framebuffer, Width, Height);
        int dimmedCalls = renderer.Stats.DrawCalls;
        SavePng("highlight.kicad_pcb", target.ReadPixels());

        renderer.Render(View(camera), target.Framebuffer, Width, Height);
        Assert.True(dimmedCalls > renderer.Stats.DrawCalls, "Highlight pass should add draw calls.");
    }

    private static ViewState View(Camera2D camera) =>
        new(camera.WorldToScreenTransform, camera.PixelsPerMm, camera.ViewportWidth, camera.ViewportHeight, camera.FlipX);

    private static void SavePng(string file, byte[] rgba)
    {
        string dir = Path.Combine(Path.GetDirectoryName(TestData.KiCadDir)!, "..", "test-output", "renders-gl");
        Directory.CreateDirectory(dir);

        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".png"));
        data.SaveTo(stream);
    }
}
