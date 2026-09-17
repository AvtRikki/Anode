using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Cutting a wire where another ran into it. The dot says the two are connected; the cut is what makes each side its
/// own item, so half a run can be picked up without the other half. A point at a wire's own end cuts nothing.
/// </summary>
public class SchSplitsTests
{
    private static readonly Vector2L Left = new(25_400_000, 50_800_000);
    private static readonly Vector2L Middle = new(50_800_000, 50_800_000);
    private static readonly Vector2L Right = new(76_200_000, 50_800_000);

    private static Schematic Sheet(string wire) => Schematic.Parse($$"""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b12")
        	(paper "A4")
        	(lib_symbols)
        {{wire}}
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """);

    private const string Straight = """
        	(wire
        		(pts
        			(xy 25.4 50.8) (xy 76.2 50.8)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000001")
        	)
        """;

    [Fact]
    public void A_point_inside_a_wire_cuts_it_in_two()
    {
        var sheet = Sheet(Straight);

        var split = Assert.Single(SchSplits.At(sheet, [Middle]));

        Assert.Same(sheet.Wires[0], split.Wire);
        Assert.Equal(2, split.Pieces.Count);
        Assert.Equal([Left, Middle], split.Pieces[0].Points);
        Assert.Equal([Middle, Right], split.Pieces[1].Points);
    }

    [Fact]
    public void A_point_at_an_end_cuts_nothing()
    {
        var sheet = Sheet(Straight);

        Assert.Empty(SchSplits.At(sheet, [Left]));
        Assert.Empty(SchSplits.At(sheet, [Right]));
    }

    [Fact]
    public void A_point_off_the_wire_cuts_nothing()
    {
        var sheet = Sheet(Straight);

        Assert.Empty(SchSplits.At(sheet, [new Vector2L(50_800_000, 25_400_000)]));
        Assert.Empty(SchSplits.At(sheet, []));
    }

    [Fact]
    public void A_polyline_is_cut_in_the_segment_the_point_belongs_to()
    {
        // A corner at (76.2, 50.8): the cut is in the second leg, and the corner stays in the first piece.
        var sheet = Sheet("""
        	(wire
        		(pts
        			(xy 25.4 50.8) (xy 76.2 50.8) (xy 76.2 76.2)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000002")
        	)
        """);

        var point = new Vector2L(76_200_000, 63_500_000);
        var split = Assert.Single(SchSplits.At(sheet, [point]));

        Assert.Equal([Left, Right, point], split.Pieces[0].Points);
        Assert.Equal([point, new Vector2L(76_200_000, 76_200_000)], split.Pieces[1].Points);
    }

    [Fact]
    public void Two_points_on_one_wire_cut_it_into_three_along_its_direction()
    {
        var sheet = Sheet(Straight);
        var near = new Vector2L(38_100_000, 50_800_000);

        // Given out of order, the pieces still follow the wire from its start.
        var split = Assert.Single(SchSplits.At(sheet, [Middle, near]));

        Assert.Equal(3, split.Pieces.Count);
        Assert.Equal([Left, near], split.Pieces[0].Points);
        Assert.Equal([near, Middle], split.Pieces[1].Points);
        Assert.Equal([Middle, Right], split.Pieces[2].Points);
    }

    [Fact]
    public void A_bus_is_never_cut()
    {
        var sheet = Sheet("""
        	(bus
        		(pts
        			(xy 25.4 50.8) (xy 76.2 50.8)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000003")
        	)
        """);

        Assert.Empty(SchSplits.At(sheet, [Middle]));
    }
}
