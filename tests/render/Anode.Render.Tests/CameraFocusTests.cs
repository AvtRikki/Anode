using Anode.Geometry;

namespace Anode.Render.Tests;

/// <summary>
/// What the view does when something is shown. The reader chose the zoom they are at, so it is taken away only when
/// what they are being shown would otherwise be too small to find.
/// </summary>
public class CameraFocusTests
{
    private static readonly RectD Visible = new(0, 0, 400, 300);

    [Fact]
    public void Something_small_is_moved_closer_to()
    {
        // A part a few millimetres across, on a view four hundred wide.
        var pin = new RectD(100, 100, 105, 103);

        var area = Assert.NotNull(CameraFocus.AreaFor(pin, Visible));

        // Room is left around it: what it is joined to matters as much as the thing itself.
        Assert.True(area.Width > pin.Width * 4, "the view would be filled by the thing alone");
        Assert.Equal((pin.MinX + pin.MaxX) / 2, (area.MinX + area.MaxX) / 2, 6);
        Assert.Equal((pin.MinY + pin.MaxY) / 2, (area.MinY + area.MaxY) / 2, 6);
    }

    [Fact]
    public void Something_already_big_enough_to_see_leaves_the_zoom_alone()
    {
        // A third of the view across: findable as it is, so the reader keeps the zoom they set.
        Assert.Null(CameraFocus.AreaFor(new RectD(0, 0, 140, 20), Visible));
        Assert.Null(CameraFocus.AreaFor(new RectD(0, 0, 20, 100), Visible));
    }

    [Fact]
    public void Nothing_to_show_asks_for_nothing()
    {
        Assert.Null(CameraFocus.AreaFor(RectD.Empty, Visible));
        Assert.Null(CameraFocus.AreaFor(new RectD(0, 0, 10, 10), RectD.Empty));
    }

    [Fact]
    public void The_closer_view_grows_with_what_is_being_shown()
    {
        var small = Assert.NotNull(CameraFocus.AreaFor(new RectD(0, 0, 1, 1), Visible));
        var larger = Assert.NotNull(CameraFocus.AreaFor(new RectD(0, 0, 10, 10), Visible));

        Assert.True(larger.Width > small.Width, "a larger thing was shown no larger");
    }
}
