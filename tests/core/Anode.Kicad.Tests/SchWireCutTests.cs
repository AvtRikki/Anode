using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Cutting a wire in two. What the two halves cover together has to be exactly what the one covered — a cut that
/// moved the drawing, however slightly, would change what is wired to what.
/// </summary>
public class SchWireCutTests
{
    private const long Mm = 1_000_000;

    [Fact]
    public void The_halves_together_are_the_wire_that_was_cut()
    {
        var wire = SchNodes.Wire([new Vector2L(10 * Mm, 50 * Mm), new Vector2L(50 * Mm, 50 * Mm)]);

        var (first, second) = SchWires.Cut(wire, new Vector2L(30 * Mm, 50 * Mm))!.Value;

        Assert.Equal([new Vector2L(10 * Mm, 50 * Mm), new Vector2L(30 * Mm, 50 * Mm)], first.Points);
        Assert.Equal([new Vector2L(30 * Mm, 50 * Mm), new Vector2L(50 * Mm, 50 * Mm)], second.Points);

        // They meet: the end of one is the start of the other, so nothing is lost between them.
        Assert.Equal(first.Points[^1], second.Points[0]);
    }

    [Fact]
    public void A_run_of_several_legs_is_cut_on_the_leg_the_point_is_on()
    {
        Vector2L[] corner = [new(10 * Mm, 50 * Mm), new(30 * Mm, 50 * Mm), new(30 * Mm, 80 * Mm)];
        var wire = SchNodes.Wire(corner);

        var (first, second) = SchWires.Cut(wire, new Vector2L(30 * Mm, 60 * Mm))!.Value;

        // The corner stays on the first half; the cut is part way down the leg that leaves it.
        Assert.Equal([corner[0], corner[1], new Vector2L(30 * Mm, 60 * Mm)], first.Points);
        Assert.Equal([new Vector2L(30 * Mm, 60 * Mm), corner[2]], second.Points);
    }

    [Theory]
    [InlineData(10, 50)]
    [InlineData(50, 50)]
    [InlineData(30, 60)]
    [InlineData(70, 50)]
    public void A_point_that_is_not_part_way_along_cuts_nothing(long x, long y)
    {
        // An end of the wire, a point beside it, a point past it: none of them leaves two wires worth having.
        var wire = SchNodes.Wire([new Vector2L(10 * Mm, 50 * Mm), new Vector2L(50 * Mm, 50 * Mm)]);

        Assert.Null(SchWires.Cut(wire, new Vector2L(x * Mm, y * Mm)));
    }

    [Fact]
    public void Each_half_is_written_as_the_wire_was()
    {
        var bus = SchNodes.Wire([new Vector2L(10 * Mm, 50 * Mm), new Vector2L(50 * Mm, 50 * Mm)], bus: true);

        var (first, second) = SchWires.Cut(bus, new Vector2L(30 * Mm, 50 * Mm))!.Value;

        Assert.True(first.IsBus);
        Assert.True(second.IsBus);
    }

    [Fact]
    public void The_stroke_travels_to_both_halves()
    {
        var dashed = new SchWire(Anode.Sexpr.SDocument.Parse(
            "(wire (pts (xy 10 50) (xy 50 50)) (stroke (width 0.3) (type dash))"
            + " (uuid \"0a1b2c3d-0000-4000-8000-000000000001\"))").Root);

        var (first, second) = SchWires.Cut(dashed, new Vector2L(30 * Mm, 50 * Mm))!.Value;

        // Cutting a dashed line must not leave one half solid.
        Assert.Equal("dash", first.StrokeStyle);
        Assert.Equal("dash", second.StrokeStyle);
        Assert.Equal(300_000, first.StrokeWidth);
    }

    [Fact]
    public void The_wire_under_a_point_is_the_one_it_lies_on()
    {
        var across = SchNodes.Wire([new Vector2L(10 * Mm, 50 * Mm), new Vector2L(50 * Mm, 50 * Mm)]);
        var down = SchNodes.Wire([new Vector2L(80 * Mm, 10 * Mm), new Vector2L(80 * Mm, 90 * Mm)]);
        SchWire[] wires = [across, down];

        Assert.Same(across, SchWires.At(wires, new Vector2L(30 * Mm, 50 * Mm)));
        Assert.Same(down, SchWires.At(wires, new Vector2L(80 * Mm, 40 * Mm)));
        Assert.Null(SchWires.At(wires, new Vector2L(30 * Mm, 40 * Mm)));

        // An end counts for finding a wire, even though nothing can be cut there.
        Assert.Same(across, SchWires.At(wires, new Vector2L(10 * Mm, 50 * Mm)));
    }
}
