namespace Anode.Kicad.Tests;

/// <summary>
/// What is joined to what. The rule that decides it is the same one the dots answer — a T needs a dot, a crossing
/// is not a connection — so these tests are the netlist's half of what SchJunctionsTests says about the drawing.
/// </summary>
public class SchConnectivityTests
{
    /// <summary>A sheet holding one resistor definition, plus whatever is asked for.</summary>
    private static Schematic Sheet(string body) => Schematic.Parse($$"""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols
        		(symbol "Device:R"
        			(property "Reference" "R"
        				(at 0 0 0)
        			)
        			(symbol "R_1_1"
        				(pin passive line
        					(at 0 3.81 270)
        					(length 1.27)
        					(name "~")
        					(number "1")
        				)
        				(pin passive line
        					(at 0 -3.81 90)
        					(length 1.27)
        					(name "~")
        					(number "2")
        				)
        			)
        		)
        	)
        {{body}}
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """);

    /// <summary>A resistor at a place, upright, so its pins sit 2.54 above and below its middle.</summary>
    private static string Resistor(string reference, double x, double y) => $"""
        	(symbol
        		(lib_id "Device:R")
        		(at {x} {y} 0)
        		(unit 1)
        		(uuid "0a1b2c3d-0000-4000-8000-0000000{reference.GetHashCode() & 0xFF:x2}")
        		(property "Reference" "{reference}"
        			(at {x} {y} 0)
        		)
        	)
        """;

    private static string Wire(double x1, double y1, double x2, double y2, string uuid) => $"""
        	(wire
        		(pts
        			(xy {x1} {y1}) (xy {x2} {y2})
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "{uuid}")
        	)
        """;

    [Fact]
    public void A_wire_between_two_pins_makes_one_net()
    {
        // Two resistors 20 mm apart, their lower pins joined by a wire. The pin is drawn 3.81 out with a length of
        // 1.27, so its free end sits 2.54 below the middle: 50.8 + 2.54 = 53.34.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Resistor("R2", 71.12, 50.8)
            + Wire(50.8, 53.34, 71.12, 53.34, "0a1b2c3d-0000-4000-8000-000000000101"));

        var nets = SchConnectivity.Build(sheet);
        var joined = Assert.Single(nets, n => n.Pins.Count == 2);

        Assert.Equal(["R1-2", "R2-2"], joined.Pins.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        Assert.False(joined.IsNamed);
        Assert.StartsWith("Net-(", joined.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_gives_the_net_its_name()
    {
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Wire(50.8, 53.34, 71.12, 53.34, "0a1b2c3d-0000-4000-8000-000000000102")
            + """
        	(label "VCC"
        		(at 71.12 53.34 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000103")
        	)
        """);

        var nets = SchConnectivity.Build(sheet);
        var named = Assert.Single(nets, n => n.IsNamed);

        Assert.Equal("VCC", named.Name);
        Assert.Contains(named.Pins, p => p.ToString() == "R1-2");
    }

    [Fact]
    public void Wires_that_only_cross_are_two_nets()
    {
        // A horizontal and a vertical wire crossing at 60.96, with no dot where they meet.
        var sheet = Sheet(
            Wire(50.8, 60.96, 71.12, 60.96, "0a1b2c3d-0000-4000-8000-000000000104")
            + Wire(60.96, 50.8, 60.96, 71.12, "0a1b2c3d-0000-4000-8000-000000000105"));

        Assert.Equal(2, SchConnectivity.Build(sheet).Count);
    }

    [Fact]
    public void A_dot_where_they_cross_makes_them_one()
    {
        var sheet = Sheet(
            Wire(50.8, 60.96, 71.12, 60.96, "0a1b2c3d-0000-4000-8000-000000000106")
            + Wire(60.96, 50.8, 60.96, 71.12, "0a1b2c3d-0000-4000-8000-000000000107")
            + """
        	(junction
        		(at 60.96 60.96)
        		(diameter 0)
        		(color 0 0 0 0)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000108")
        	)
        """);

        Assert.Single(SchConnectivity.Build(sheet));
    }

    [Fact]
    public void A_pin_of_a_part_is_found_where_the_part_stands()
    {
        var sheet = Sheet(Resistor("R1", 50.8, 50.8));

        var pins = SchConnectivity.PinsOf(sheet);

        // The library draws the pins 3.81 out with a length of 1.27, so their free ends sit 2.54 from the middle.
        Assert.Equal(["R1-1", "R1-2"], pins.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        Assert.Contains(pins, p => p.At.Y == 48_260_000);
        Assert.Contains(pins, p => p.At.Y == 53_340_000);
    }

    [Fact]
    public void A_part_nobody_wired_has_a_net_of_its_own_for_each_pin()
    {
        var sheet = Sheet(Resistor("R1", 50.8, 50.8));

        var nets = SchConnectivity.Build(sheet);

        // Two pins, nothing joining them: two nets, each with one pin on it.
        Assert.Equal(2, nets.Count);
        Assert.All(nets, n => Assert.Single(n.Pins));
    }

    [Fact]
    public void Two_labels_of_one_net_settle_on_one_name()
    {
        var sheet = Sheet(
            Wire(50.8, 60.96, 71.12, 60.96, "0a1b2c3d-0000-4000-8000-000000000109")
            + """
        	(label "VCC"
        		(at 50.8 60.96 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-00000000010a")
        	)
        	(label "+5V"
        		(at 71.12 60.96 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-00000000010b")
        	)
        """);

        var net = Assert.Single(SchConnectivity.Build(sheet));

        // Both names belong to the same net; the first by name is the one it goes by.
        Assert.True(net.IsNamed);
        Assert.Equal("+5V", net.Name);
    }

    [Fact]
    public void A_bus_is_left_out_until_buses_are_understood()
    {
        var sheet = Sheet("""
        	(bus
        		(pts
        			(xy 50.8 60.96) (xy 71.12 60.96)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-00000000010c")
        	)
        """);

        Assert.Empty(SchConnectivity.Build(sheet));
    }
}
