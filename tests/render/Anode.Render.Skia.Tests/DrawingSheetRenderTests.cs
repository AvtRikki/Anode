using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Tests;
using SkiaSharp;

namespace Anode.Render.Skia.Tests;

/// <summary>
/// The drawing sheet around a schematic: KiCad's default frame and title block, drawn on a layer of its own that is
/// rebuilt when the title block changes. Renders the corner to PNG for comparison by eye.
/// </summary>
public class DrawingSheetRenderTests
{
    [Fact]
    public void The_title_block_is_drawn_and_follows_an_edit()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        var scene = SchematicSceneBuilder.Build(sheet, frame: new SheetFrameText(Path.GetFileName(path!), "/", 1, 2));
        var frame = Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame);

        // KiCad's frame sits 10 mm inside the paper on every side.
        Assert.InRange(frame.Bounds.Width, scene.BoardOutline.Width - 20.5, scene.BoardOutline.Width - 19.5);
        Assert.InRange(frame.Bounds.Height, scene.BoardOutline.Height - 20.5, scene.BoardOutline.Height - 19.5);

        Render(scene, path!, "drawing-sheet");

        var before = frame.Lines.ToList();
        int version = frame.Version;
        TitleBlockWrites.Set(sheet.Root, "title", "A title long enough to show on the frame");
        SchematicSceneBuilder.RedrawFrame(scene);

        Assert.True(frame.Version > version);
        Assert.NotEqual(before, frame.Lines);
        Render(scene, path!, "drawing-sheet-edited");
    }

    [Fact]
    public void A_board_is_drawn_on_its_page_and_the_page_follows_an_edit()
    {
        string? path = TestData.AnyBoard();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var board = Board.Load(path!);
        var scene = SceneBuilder.Build(board, new SheetFrameText(Path.GetFileName(path!), string.Empty));
        var page = Assert.Single(scene.Layers, l => l.Name == LayerStyle.PageFrame);

        // Board coordinates are page coordinates: the page layer spans the paper the file names, board or not.
        var paper = DrawingSheet.PaperOf(board.Root);
        Assert.InRange(page.Bounds.Width, Units.NmToMm(paper.X) - 0.5, Units.NmToMm(paper.X) + 0.5);
        Assert.InRange(page.Bounds.Height, Units.NmToMm(paper.Y) - 0.5, Units.NmToMm(paper.Y) + 0.5);

        // Paper, not board: it neither dims nor picks.
        Assert.True(page.IsDecoration);

        Render(scene, page.Bounds, path!, "board-page", corner: false);
        Render(scene, page.Bounds, path!, "board-page-corner", corner: true);

        var before = page.Lines.ToList();
        TitleBlockWrites.Set(board.Root, "rev", "B");
        SceneBuilder.RedrawFrame(scene);
        Assert.NotEqual(before, page.Lines);
    }

    private static void Render(SchematicScene scene, string path, string name) =>
        Render(scene, scene.BoardOutline, path, name, corner: true);

    private static void Render(IRenderScene scene, RectD paper, string path, string name, bool corner)
    {
        const int width = 1400, height = 560;
        var camera = new Camera2D { ViewportWidth = width, ViewportHeight = corner ? height : 900 };
        camera.Fit(corner ? new RectD(paper.MaxX - 125, paper.MaxY - 50, paper.MaxX - 5, paper.MaxY - 5) : paper);
        int h = corner ? height : 900;

        using var surface = SKSurface.Create(new SKImageInfo(width, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var renderer = new SkiaSceneRenderer(scene);
        renderer.Render(surface.Canvas, new ViewState(camera.WorldToScreenTransform, camera.PixelsPerMm, width, h, false));

        string dir = Environment.GetEnvironmentVariable("ANODE_SNAPSHOT_DIR")
            ?? Path.Combine(Path.GetDirectoryName(TestData.KiCadDir)!, "..", "test-output", "renders");
        Directory.CreateDirectory(dir);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(Path.Combine(dir, $"{name}-{Path.GetFileNameWithoutExtension(path)}.png"));
        data.SaveTo(stream);
    }
}
