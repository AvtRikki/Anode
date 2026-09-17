using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Where a dot is needed. This is the rule that decides whether two wires are one net or two, so it is written down
/// here rather than felt out with a mouse: a T connects, a crossing does not, a corner needs nothing, and a branch of
/// three ends does.
/// </summary>
public class SchJunctionsTests
{
    private static readonly Vector2L Left = new(25_400_000, 50_800_000);
    private static readonly Vector2L Middle = new(50_800_000, 50_800_000);
    private static readonly Vector2L Right = new(76_200_000, 50_800_000);
    private static readonly Vector2L Above = new(50_800_000, 25_400_000);
    private static readonly Vector2L Below = new(50_800_000, 76_200_000);

    /// <summary>A sheet with one horizontal wire from left to right, plus whatever else is asked for.</summary>
    private static Schematic Sheet(string extra = "") => Schematic.Parse($$"""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
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
        {{extra}}
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """);

    [Fact]
    public void A_wire_ending_inside_another_needs_a_dot()
    {
        var sheet = Sheet();

        var needed = SchJunctions.Needed(sheet, [(Above, Middle)]);

        Assert.Equal([Middle], needed);
    }

    [Fact]
    public void A_wire_that_only_crosses_needs_nothing()
    {
        var sheet = Sheet();

        // Straight through: no end of the new wire lands on the old one, so the two are not connected.
        Assert.Empty(SchJunctions.Needed(sheet, [(Above, Below)]));
    }

    [Fact]
    public void A_corner_of_two_ends_needs_nothing()
    {
        var sheet = Sheet();

        // The new wire starts where the old one ends: two ends, one corner.
        Assert.Empty(SchJunctions.Needed(sheet, [(Right, new Vector2L(76_200_000, 25_400_000))]));
    }

    [Fact]
    public void Three_ends_meeting_need_a_dot()
    {
        // Two wires already end at the right-hand point; a third arriving there makes it a branch.
        var sheet = Sheet("""
        	(wire
        		(pts
        			(xy 76.2 50.8) (xy 76.2 25.4)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000002")
        	)
        """);

        Assert.Equal([Right], SchJunctions.Needed(sheet, [(Right, new Vector2L(101_600_000, 50_800_000))]));
    }

    [Fact]
    public void A_point_that_already_has_a_dot_is_left_alone()
    {
        var sheet = Sheet("""
        	(junction
        		(at 50.8 50.8)
        		(diameter 0)
        		(color 0 0 0 0)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000003")
        	)
        """);

        Assert.Empty(SchJunctions.Needed(sheet, [(Above, Middle)]));
    }

    [Fact]
    public void A_wire_landing_on_a_bus_gets_no_dot()
    {
        // A wire meets a bus through an entry, not through a dot, so buses are not counted at all.
        var sheet = Schematic.Parse("""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b11")
        	(paper "A4")
        	(lib_symbols)
        	(bus
        		(pts
        			(xy 25.4 50.8) (xy 76.2 50.8)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000004")
        	)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """);

        Assert.Empty(SchJunctions.Needed(sheet, [(Above, Middle)]));
    }

    [Fact]
    public void Both_ends_of_a_new_wire_are_considered()
    {
        // Ends inside the old wire at one end and inside a second wire at the other: two dots, in order.
        var sheet = Sheet("""
        	(wire
        		(pts
        			(xy 25.4 76.2) (xy 76.2 76.2)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000005")
        	)
        """);

        Assert.Equal([Middle, Below], SchJunctions.Needed(sheet, [(Middle, Below)]));
    }
}
