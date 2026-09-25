using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Pulling the handles of drawn shapes, as KiCad's point editor does: what the handles of each shape are, what
/// pulling each one does to the file, and the corners that can be put in and taken out.
/// </summary>
public class SchPointsTests
{
    [Fact]
    public void A_polyline_has_a_handle_per_point_and_a_closed_outline_one_for_its_first_and_last()
    {
        var sheet = Sheet(
            Poly(1, (10, 10), (20, 10), (20, 20)),
            Poly(2, (40, 10), (50, 10), (50, 20), (40, 10)));

        Assert.Equal(3, SchPoints.Handles(sheet.Graphics[0]).Count);
        Assert.Equal(3, SchPoints.Handles(sheet.Graphics[1]).Count);
    }

    [Fact]
    public void Pulling_a_point_moves_that_point_only()
    {
        var sheet = Sheet(Poly(1, (10, 10), (20, 10), (20, 20)));
        var line = sheet.Graphics[0];

        SchPoints.Move(sheet, line, SchPoints.Handles(line)[1], Mm(25, 12));

        Assert.Equal([Mm(10, 10), Mm(25, 12), Mm(20, 20)], line.Points);
    }

    /// <summary>The first point of a closed outline is also its last: pulling it keeps the outline closed.</summary>
    [Fact]
    public void A_closed_outline_stays_closed()
    {
        var sheet = Sheet(Poly(1, (40, 10), (50, 10), (50, 20), (40, 10)));
        var outline = sheet.Graphics[0];

        SchPoints.Move(sheet, outline, SchPoints.Handles(outline)[0], Mm(35, 5));

        Assert.Equal(outline.Points[0], outline.Points[^1]);
        Assert.Equal(Mm(35, 5), outline.Points[0]);
    }

    /// <summary>Two lines drawn to meet at a corner still meet there when the corner is pulled — KiCad's rule.</summary>
    [Fact]
    public void Lines_that_meet_at_the_point_pulled_are_pulled_along()
    {
        var sheet = Sheet(Poly(1, (10, 10), (20, 10)), Poly(2, (20, 10), (20, 20)), Poly(3, (30, 10), (40, 10)));
        var first = sheet.Graphics[0];

        var changed = SchPoints.Move(sheet, first, SchPoints.Handles(first)[1], Mm(22, 12));

        Assert.Equal(2, changed.Count);
        Assert.Equal(Mm(22, 12), sheet.Graphics[1].Points[0]);
        Assert.Equal(Mm(30, 10), sheet.Graphics[2].Points[0]);
    }

    [Theory]
    [InlineData(0, 5, 5, 5, 5, 30, 20)]
    [InlineData(1, 35, 5, 10, 5, 35, 20)]
    [InlineData(2, 35, 25, 10, 10, 35, 25)]
    [InlineData(3, 5, 25, 5, 10, 30, 25)]
    public void A_rectangle_corner_takes_its_two_sides_along(int corner, double x, double y, double sx, double sy, double ex, double ey)
    {
        var sheet = Sheet("\t(rectangle (start 10 10) (end 30 20) (stroke (width 0) (type default)) (fill (type none)) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\"))\n");
        var box = sheet.Graphics[0];

        SchPoints.Move(sheet, box, SchPoints.Handles(box).Single(h => h.Kind == SchHandleKind.Corner && h.Index == corner), Mm(x, y));

        Assert.Equal((Mm(sx, sy), Mm(ex, ey)), (box.Start, box.End));
    }

    [Fact]
    public void A_rectangle_side_moves_across_only()
    {
        var sheet = Sheet("\t(rectangle (start 10 10) (end 30 20) (stroke (width 0) (type default)) (fill (type none)) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\"))\n");
        var box = sheet.Graphics[0];

        // The right side, pulled out and up: only out counts.
        SchPoints.Move(sheet, box, SchPoints.Handles(box).Single(h => h.Kind == SchHandleKind.Side && h.Index == 1), Mm(40, 2));

        Assert.Equal((Mm(10, 10), Mm(40, 20)), (box.Start, box.End));
    }

    [Fact]
    public void A_circle_moves_by_its_centre_and_grows_by_its_rim()
    {
        var sheet = Sheet("\t(circle (center 20 20) (radius 5) (stroke (width 0) (type default)) (fill (type none)) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\"))\n");
        var circle = sheet.Graphics[0];
        var handles = SchPoints.Handles(circle);
        Assert.Equal(Mm(25, 20), handles[1].At);

        SchPoints.Move(sheet, circle, handles[0], Mm(30, 30));
        Assert.Equal((Mm(30, 30), 5_000_000L), (circle.Center, circle.Radius));

        SchPoints.Move(sheet, circle, SchPoints.Handles(circle)[1], Mm(30, 38));
        Assert.Equal(8_000_000L, circle.Radius);
    }

    [Fact]
    public void A_corner_goes_into_the_side_nearest_where_it_is_asked_for()
    {
        var sheet = Sheet(Poly(1, (10, 10), (30, 10), (30, 30)));
        var line = sheet.Graphics[0];

        var added = SchPoints.AddCorner(line, Mm(30, 20));

        Assert.Equal(2, added?.Index);
        Assert.Equal([Mm(10, 10), Mm(30, 10), Mm(30, 20), Mm(30, 30)], line.Points);
    }

    /// <summary>A line keeps two points and a rule area three; a closed outline stays closed when its first goes.</summary>
    [Fact]
    public void Corners_come_out_down_to_KiCads_limits()
    {
        var sheet = Sheet(Poly(1, (10, 10), (20, 10)), Poly(2, (40, 10), (50, 10), (50, 20), (40, 20), (40, 10)));
        var line = sheet.Graphics[0];
        var outline = sheet.Graphics[1];

        Assert.False(SchPoints.RemoveCorner(line, SchPoints.Handles(line)[0]));

        Assert.True(SchPoints.RemoveCorner(outline, SchPoints.Handles(outline)[0]));
        Assert.Equal([Mm(50, 10), Mm(50, 20), Mm(40, 20), Mm(50, 10)], outline.Points);
    }

    [Fact]
    public void A_rule_areas_outline_is_edited_and_keeps_three_corners()
    {
        var sheet = Sheet("\t(rule_area (polyline (pts (xy 10 10) (xy 30 10) (xy 30 30) (xy 10 10))"
            + " (stroke (width 0) (type dash)) (fill (type none)) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\")))\n");
        var area = Assert.Single(sheet.RuleAreas);

        var handles = SchPoints.Handles(area);
        Assert.Equal(3, handles.Count);
        Assert.False(SchPoints.CanRemoveCorner(area, handles[1]));

        SchPoints.Move(sheet, area, handles[1], Mm(35, 5));
        Assert.Equal(Mm(35, 5), area.Outline!.Points[1]);
    }

    [Fact]
    public void A_held_shape_offers_no_handles()
    {
        var sheet = Sheet("\t(polyline (pts (xy 10 10) (xy 20 10)) (locked yes) (stroke (width 0) (type default)) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\"))\n");

        Assert.Empty(SchPoints.Handles(sheet.Graphics[0]));
    }

    private static string Poly(int n, params (double X, double Y)[] points) =>
        "\t(polyline (pts " + string.Join(' ', points.Select(p => FormattableString.Invariant($"(xy {p.X} {p.Y})")))
        + $") (stroke (width 0) (type default)) (uuid \"2a1b2c3d-0000-4000-8000-00000000000{n}\"))\n";

    private static Schematic Sheet(params string[] items) => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + string.Concat(items)
        + "\t(embedded_fonts no))\n");

    private static Vector2L Mm(double x, double y) => new((long)Math.Round(x * 1_000_000), (long)Math.Round(y * 1_000_000));
}
