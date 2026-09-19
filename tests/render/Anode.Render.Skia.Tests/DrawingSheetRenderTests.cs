using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;
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

    [Fact]
    public void A_schematic_is_framed_with_the_drawing_sheet_its_project_names()
    {
        string path = Path.Combine(TestData.KiCadDir, "demos", "vme-wren", "vme-wren.kicad_sch");
        Assert.SkipWhen(!File.Exists(path), TestData.SkipReason);

        var frame = SheetFrameText.ForProject(path, board: false, out string? missing);
        Assert.Null(missing);
        Assert.NotNull(frame.Template);
        Assert.Equal("BE-CEM", frame.Variables["DIVGRP"]);

        var scene = SchematicSceneBuilder.Build(Schematic.Load(path), frame: frame);
        Render(scene, path, "custom-sheet");
    }

    [Fact]
    public void A_board_is_framed_with_the_drawing_sheet_its_project_names()
    {
        string path = Path.Combine(TestData.KiCadDir, "demos", "interf_u", "interf_u.kicad_pcb");
        Assert.SkipWhen(!File.Exists(path) || !File.Exists(Path.ChangeExtension(path, ".kicad_pro")), TestData.SkipReason);

        var frame = SheetFrameText.ForProject(path, board: true, out string? missing);
        Assert.Null(missing);
        Assert.NotNull(frame.Template);

        var scene = SceneBuilder.Build(Board.Load(path), frame);
        var page = Assert.Single(scene.Layers, l => l.Name == LayerStyle.PageFrame);

        // The logo is an outline the page fills.
        Assert.NotEmpty(page.Polygons);
        Render(scene, page.Bounds, path, "custom-board-page", corner: false);
        Render(scene, page.Bounds, path, "custom-board-corner", corner: true);
    }

    [Fact]
    public void A_drawing_sheet_that_is_missing_falls_back_and_says_so()
    {
        string folder = Directory.CreateTempSubdirectory("anode-wks-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "p.kicad_pro"), """{ "schematic": { "page_layout_descr_file": "gone.kicad_wks" } }""");
            string sheet = Path.Combine(folder, "p.kicad_sch");

            var frame = SheetFrameText.ForProject(sheet, board: false, out string? missing);

            Assert.Null(frame.Template);
            Assert.Equal(Path.Combine(folder, "gone.kicad_wks"), missing);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_picture_in_a_drawing_sheet_is_drawn_where_and_as_large_as_it_says()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        // A 400 × 120 px picture at KiCad's default 300 PPI: 33.87 × 10.16 mm, centred 75 mm left of and 25 mm up
        // from the bottom-right corner of the frame — the empty band at the top of the title block.
        byte[] png = Logo(400, 120);
        string text = DrawingSheetFile.DefaultText.TrimEnd()[..^1]
            + $"(bitmap (name \"logo\") (pos 75 25) (scale 1) (data \"{Convert.ToBase64String(png)}\")))";
        var template = DrawingSheetFile.Parse(text);

        var sheet = Schematic.Load(path!);
        var scene = SchematicSceneBuilder.Build(sheet, frame: new SheetFrameText(Path.GetFileName(path!)) { Template = template });
        var frame = Assert.Single(scene.Layers, l => l.Name == LayerStyle.Sch.Frame);
        var picture = Assert.Single(frame.Images);

        Assert.Equal(png, picture.Encoded);
        Assert.Equal(400 * 25.4 / 300, picture.Bounds.Width, 3);
        Assert.Equal(120 * 25.4 / 300, picture.Bounds.Height, 3);

        // Centred on its position: the frame's right edge is 10 mm in from the paper's, its bottom likewise.
        var paper = scene.BoardOutline;
        Assert.Equal(paper.MaxX - 10 - 75, (picture.Bounds.MinX + picture.Bounds.MaxX) / 2, 3);
        Assert.Equal(paper.MaxY - 10 - 25, (picture.Bounds.MinY + picture.Bounds.MaxY) / 2, 3);

        Render(scene, path!, "drawing-sheet-picture");
    }

    /// <summary>A picture that shows it is one: a coloured band with a word on it.</summary>
    private static byte[] Logo(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, 0),
            [new SKColor(0x00, 0x88, 0xb0), new SKColor(0xd6, 0x00, 0x6c)], SKShaderTileMode.Clamp) };
        canvas.DrawRect(0, 0, width, height, paint);
        using var font = new SKFont(SKTypeface.Default, height * 0.6f);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("ANODE", width / 2f, height * 0.72f, SKTextAlign.Center, font, ink);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
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
