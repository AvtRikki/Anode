using Anode.Geometry;

namespace Anode.Render.Tests;

public class CameraTests
{
    private static Camera2D Camera(bool flip = false) => new()
    {
        ViewportWidth = 800,
        ViewportHeight = 600,
        PixelsPerMm = 10,
        Center = new Vector2D(5, -3),
        FlipX = flip,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Screen_and_world_are_inverse(bool flip)
    {
        var camera = Camera(flip);
        var world = new Vector2D(12.5, 7.25);
        var back = camera.ScreenToWorld(camera.WorldToScreen(world));

        Assert.Equal(world.X, back.X, 9);
        Assert.Equal(world.Y, back.Y, 9);

        var viaMatrix = camera.WorldToScreenTransform.Apply(world);
        var direct = camera.WorldToScreen(world);
        Assert.Equal(direct.X, viaMatrix.X, 9);
        Assert.Equal(direct.Y, viaMatrix.Y, 9);
    }

    [Fact]
    public void Zoom_keeps_point_under_cursor()
    {
        var camera = Camera();
        var cursor = new Vector2D(100, 450);
        var before = camera.ScreenToWorld(cursor);

        camera.ZoomAt(cursor, 3.7);

        var after = camera.ScreenToWorld(cursor);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
        Assert.Equal(37, camera.PixelsPerMm, 9);
    }

    [Fact]
    public void Fit_shows_whole_rect()
    {
        var camera = Camera();
        var rect = new RectD(-50, -20, 150, 80);

        camera.Fit(rect);

        var visible = camera.VisibleWorld;
        Assert.True(visible.MinX <= rect.MinX && visible.MaxX >= rect.MaxX);
        Assert.True(visible.MinY <= rect.MinY && visible.MaxY >= rect.MaxY);
    }

    [Fact]
    public void Pan_moves_content_with_the_mouse()
    {
        var camera = Camera();
        var world = new Vector2D(1, 1);
        var screenBefore = camera.WorldToScreen(world);

        camera.PanPixels(30, -10);

        var screenAfter = camera.WorldToScreen(world);
        Assert.Equal(screenBefore.X + 30, screenAfter.X, 9);
        Assert.Equal(screenBefore.Y - 10, screenAfter.Y, 9);
    }
}
