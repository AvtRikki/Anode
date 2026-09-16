using Anode.Geometry;

namespace Anode.Kicad.Tests;

public class BoardModelTests
{
    private const string LegacyBoard = """
        (kicad_pcb
        	(version 20241229)
        	(generator "pcbnew")
        	(generator_version "9.0")
        	(layers
        		(0 "F.Cu" signal)
        		(4 "In1.Cu" signal)
        		(2 "B.Cu" signal)
        		(25 "Edge.Cuts" user)
        	)
        	(net 0 "")
        	(net 1 "GND")
        	(footprint "Lib:Part"
        		(layer "F.Cu")
        		(at 10 20 90)
        		(property "Reference" "R1"
        			(at 0 -1 90)
        			(layer "F.SilkS")
        		)
        		(fp_line
        			(start 1 0)
        			(end 2 0)
        			(stroke
        				(width 0.1)
        				(type solid)
        			)
        			(layer "F.SilkS")
        		)
        		(pad "1" smd roundrect
        			(at 1 0 90)
        			(size 0.8 0.9)
        			(layers "F.Cu" "F.Mask")
        			(roundrect_rratio 0.2)
        			(net 1 "GND")
        		)
        		(pad "2" thru_hole oval
        			(at -1 0)
        			(size 1.7 1.7)
        			(drill oval 1 1.2 (offset 0.1 0))
        			(layers "*.Cu" "*.Mask")
        		)
        	)
        	(segment
        		(start 0 0)
        		(end 5 0)
        		(width 0.25)
        		(layer "B.Cu")
        		(net 1)
        	)
        	(via
        		(at 5 0)
        		(size 0.6)
        		(drill 0.3)
        		(layers "F.Cu" "B.Cu")
        		(net 1)
        	)
        	(gr_rect
        		(start 0 0)
        		(end 30 40)
        		(stroke
        			(width 0.1)
        			(type default)
        		)
        		(fill no)
        		(layer "Edge.Cuts")
        	)
        	(dimension
        		(type aligned)
        	)
        )

        """;

    private const string AffineBoard = """
        (kicad_pcb
        	(version 20260901)
        	(generator "pcbnew")
        	(generator_version "10.99")
        	(layers
        		(0 "F.Cu" signal)
        		(2 "B.Cu" signal)
        	)
        	(footprint "Lib:Part"
        		(layer "F.Cu")
        		(transform
        			(translate 10 20)
        			(rotate 90)
        			(scale 2 2)
        		)
        		(fp_line
        			(start 1 0)
        			(end 2 0)
        			(stroke
        				(width 0.1)
        				(type solid)
        			)
        			(layer "F.SilkS")
        		)
        		(pad "1" smd rect
        			(at 1 0)
        			(size 1 1)
        			(layers "F.Cu")
        			(net "GND")
        		)
        	)
        	(segment
        		(start 0 0)
        		(end 5 0)
        		(width 0.25)
        		(layer "F.Cu")
        		(net "GND")
        	)
        )

        """;

    private static Vector2L Mm(double x, double y) => new(Units.MmToNm(x), Units.MmToNm(y));

    [Fact]
    public void Legacy_footprint_places_children_counter_clockwise()
    {
        var board = Board.Parse(LegacyBoard);
        var fp = Assert.Single(board.Footprints);

        Assert.Equal(20241229, board.Version);
        Assert.Equal("R1", fp.Reference);
        Assert.Equal(Mm(10, 20), fp.Position);
        Assert.Equal(90, fp.Orientation);

        // A point 1 mm to the right, rotated 90° counter-clockwise on screen (Y down), ends up 1 mm above.
        Assert.Equal(Mm(10, 19), fp.Pads[0].BoardPosition);
        Assert.Equal(Mm(10, 19), fp.Shapes[0].ToBoard.ApplyRounded(fp.Shapes[0].Start));
        Assert.Equal(90, fp.Pads[0].Orientation);
    }

    [Fact]
    public void Legacy_nets_resolve_by_code()
    {
        var board = Board.Parse(LegacyBoard);
        var gnd = board.Nets.Find("GND");

        Assert.NotNull(gnd);
        Assert.Equal(1, gnd.Code);
        Assert.Same(gnd, board.Segments[0].Net);
        Assert.Same(gnd, board.Vias[0].Net);
        Assert.Same(gnd, board.Footprints[0].Pads[0].Net);
        Assert.Null(board.Footprints[0].Pads[1].Net);
    }

    [Fact]
    public void Pad_details_are_read()
    {
        var pads = Board.Parse(LegacyBoard).Footprints[0].Pads;

        Assert.Equal(PadType.Smd, pads[0].Type);
        Assert.Equal(PadShape.RoundRect, pads[0].Shape);
        Assert.Equal(0.2, pads[0].RoundRectRatio);
        Assert.Equal(Mm(0.8, 0.9), pads[0].Size);
        Assert.Null(pads[0].Drill);

        Assert.Equal(PadType.ThroughHole, pads[1].Type);
        var drill = pads[1].Drill!.Value;
        Assert.True(drill.IsOval);
        Assert.Equal(Mm(1, 1.2), drill.Size);
        Assert.Equal(Mm(0.1, 0), drill.Offset);
    }

    [Fact]
    public void Layers_and_wildcards()
    {
        var board = Board.Parse(LegacyBoard);

        Assert.Equal(["F.Cu", "In1.Cu", "B.Cu"], board.Layers.Copper.Select(l => l.Name));
        Assert.Equal(["F.Cu", "In1.Cu", "B.Cu"], board.Layers.Expand("*.Cu"));
        Assert.Equal(["F.Cu", "B.Cu"], board.Layers.Expand("F&B.Cu"));
        Assert.Equal(["In1.Cu"], board.Layers.Expand("*.In.Cu"));
        Assert.Equal(["F.Cu", "In1.Cu", "B.Cu"], board.Layers.CopperSpan("B.Cu", "F.Cu").Select(l => l.Name));
    }

    [Fact]
    public void Bounds_come_from_edge_cuts()
    {
        var bounds = Board.Parse(LegacyBoard).ComputeBounds();
        Assert.Equal(new Box2L(Units.MmToNm(-0.05), Units.MmToNm(-0.05), Units.MmToNm(30.05), Units.MmToNm(40.05)), bounds);
    }

    [Fact]
    public void Unknown_items_are_kept_and_file_is_unchanged()
    {
        var board = Board.Parse(LegacyBoard);
        Assert.Equal("dimension", Assert.Single(board.OtherItems).Head);
        Assert.Equal(LegacyBoard, board.Document.ToString());
    }

    [Fact]
    public void Affine_transform_applies_scale_then_rotation()
    {
        var board = Board.Parse(AffineBoard);
        var fp = Assert.Single(board.Footprints);

        Assert.Equal(2, fp.ScaleX);
        Assert.Equal(Mm(10, 18), fp.Pads[0].BoardPosition);
        Assert.Equal(Mm(10, 16), fp.Shapes[0].ToBoard.ApplyRounded(fp.Shapes[0].End));
    }

    [Fact]
    public void Name_only_nets_are_shared_between_items()
    {
        var board = Board.Parse(AffineBoard);
        var net = board.Footprints[0].Pads[0].Net;

        Assert.NotNull(net);
        Assert.Null(net.Code);
        Assert.Equal("GND", net.Name);
        Assert.Same(net, board.Segments[0].Net);
    }
}
