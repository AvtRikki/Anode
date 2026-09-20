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
        // 1.27, so its free end sits 2.54 below the middle: 50.8 + 2.54 = 54.61.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Resistor("R2", 71.12, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000101"));

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
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000102")
            + """
        	(label "VCC"
        		(at 71.12 54.61 0)
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

        // The library draws the pins 3.81 from the middle, and that point is where a wire meets them.
        Assert.Equal(["R1-1", "R1-2"], pins.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        Assert.Contains(pins, p => p.At.Y == 46_990_000);
        Assert.Contains(pins, p => p.At.Y == 54_610_000);
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

    /// <summary>A sheet whose library holds a power symbol as well as the resistor.</summary>
    private static Schematic PowerSheet(string body) => Schematic.Parse($$"""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """.Replace("(lib_symbols)", """
        	(lib_symbols
        		(symbol "power:GND"
        			(power global)
        			(property "Reference" "#PWR"
        				(at 0 0 0)
        			)
        			(property "Value" "GND"
        				(at 0 0 0)
        			)
        			(symbol "GND_1_1"
        				(pin power_in line
        					(at 0 0 90)
        					(length 0)
        					(name "GND")
        					(number "1")
        				)
        			)
        		)
        	)
        """, StringComparison.Ordinal).Replace("\t(sheet_instances", body + "\t(sheet_instances", StringComparison.Ordinal));

    private static string Ground(string reference, double x, double y) => $"""
        	(symbol
        		(lib_id "power:GND")
        		(at {x} {y} 0)
        		(unit 1)
        		(uuid "0a1b2c3d-0000-4000-8000-0000000{reference.GetHashCode() & 0xFF:x2}")
        		(property "Reference" "{reference}"
        			(at {x} {y} 0)
        		)
        		(property "Value" "GND"
        			(at {x} {y} 0)
        		)
        	)
        """;

    [Fact]
    public void Two_power_symbols_of_one_name_are_one_net()
    {
        // Nothing joins them on the sheet but the name they carry, which is the whole point of a power symbol.
        var sheet = PowerSheet(Ground("#PWR01", 50.8, 50.8) + Ground("#PWR02", 88.9, 76.2));

        var net = Assert.Single(SchConnectivity.Build(sheet));

        Assert.Equal("GND", net.Name);
        Assert.True(net.IsNamed);
        Assert.Equal(2, net.Pins.Count);
    }

    [Fact]
    public void A_global_label_names_a_net_as_a_local_one_does()
    {
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-00000000010d")
            + """
        	(global_label "VBUS"
        		(shape input)
        		(at 71.12 54.61 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-00000000010e")
        	)
        """);

        var named = Assert.Single(SchConnectivity.Build(sheet), n => n.IsNamed);

        Assert.Equal("VBUS", named.Name);
        Assert.Contains(named.Pins, p => p.ToString() == "R1-2");
    }

    [Fact]
    public void One_name_in_two_places_is_one_net()
    {
        // Two wires that never touch, each carrying the same label: KiCad reads that as one net, and so do we.
        var sheet = Sheet(
            Wire(50.8, 50.8, 71.12, 50.8, "0a1b2c3d-0000-4000-8000-00000000010f")
            + Wire(50.8, 76.2, 71.12, 76.2, "0a1b2c3d-0000-4000-8000-000000000110")
            + """
        	(label "SDA"
        		(at 50.8 50.8 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000111")
        	)
        	(label "SDA"
        		(at 50.8 76.2 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000112")
        	)
        """);

        var net = Assert.Single(SchConnectivity.Build(sheet));

        Assert.Equal("SDA", net.Name);
        Assert.Equal(2, net.Items.OfType<SchWire>().Count());
    }

    [Theory]
    [InlineData("(power)")]
    [InlineData("(power global)")]
    public void A_power_symbol_is_known_by_its_marker_whichever_way_it_is_written(string marker)
    {
        // Both spellings are in the wild; reading only the newer one would miss over half of what is out there.
        var library = SymbolLibrary.Parse(
            "(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n"
            + $"\t(symbol \"GND\"\n\t\t{marker}\n\t\t(property \"Value\" \"GND\"\n\t\t\t(at 0 0 0)\n\t\t)\n\t)\n)");

        var symbol = library.Find("GND");

        Assert.NotNull(symbol);
        Assert.True(symbol!.IsPower);
        Assert.Equal("GND", symbol.Value);
    }

    [Fact]
    public void An_ordinary_part_is_not_power()
    {
        var sheet = Sheet(Resistor("R1", 50.8, 50.8));

        Assert.False(sheet.LibrarySymbols["Device:R"].IsPower);
    }

    private static string BusLabel(string text, double x, double y, string uuid) => $"""
        	(label "{text}"
        		(at {x} {y} 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "{uuid}")
        	)
        """;

    [Fact]
    public void A_bus_declares_the_nets_it_carries()
    {
        var sheet = Sheet(
            """
        	(bus
        		(pts
        			(xy 50.8 88.9) (xy 101.6 88.9)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000120")
        	)
        """
            + BusLabel("DQ[0..3]", 50.8, 88.9, "0a1b2c3d-0000-4000-8000-000000000121"));

        var nets = SchConnectivity.Build(sheet);

        // Four nets exist because the bus says they do, each named and none fused with another.
        Assert.Equal(["DQ0", "DQ1", "DQ2", "DQ3"], nets.Select(n => n.Name));
        Assert.All(nets, n => Assert.True(n.IsNamed));
    }

    [Fact]
    public void A_wire_that_taps_a_bus_joins_the_member_it_names()
    {
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000122")
            + BusLabel("DQ1", 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000123")
            + BusLabel("DQ[0..3]", 50.8, 88.9, "0a1b2c3d-0000-4000-8000-000000000124"));

        var nets = SchConnectivity.Build(sheet);
        var tapped = Assert.Single(nets, n => n.Name == "DQ1");

        // The wire's own label put the part on DQ1; the bus is listed with it as what carries it.
        Assert.Contains(tapped.Pins, p => p.ToString() == "R1-2");
        Assert.Equal(2, tapped.Items.OfType<SchLabel>().Count());

        // The members nobody tapped still exist, and carry nothing.
        Assert.All(nets.Where(n => n.Name != "DQ1" && n.Name.StartsWith("DQ", StringComparison.Ordinal)),
            n => Assert.Empty(n.Pins));
    }

    [Fact]
    public void A_group_bus_carries_what_the_sheet_declared()
    {
        var sheet = Schematic.Parse("""
            (kicad_sch
            	(version 20260206)
            	(generator "anode")
            	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
            	(paper "A4")
            	(lib_symbols)
            	(bus_alias "DPHY"
            		(members "C_N" "C_P"
            		)
            	)
            	(label "DPHY"
            		(at 50.8 88.9 0)
            		(effects
            			(font
            				(size 1.27 1.27)
            			)
            		)
            		(uuid "0a1b2c3d-0000-4000-8000-000000000125")
            	)
            	(sheet_instances
            		(path "/"
            			(page "1")
            		)
            	)
            	(embedded_fonts no)
            )
            """);

        Assert.Equal(["C_N", "C_P"], SchConnectivity.Build(sheet).Select(n => n.Name));
    }

    [Fact]
    public void A_pin_marked_no_connect_says_so()
    {
        // The mark sits on the resistor's lower pin, 3.81 below its middle — where a wire would meet it.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + """
        	(no_connect
        		(at 50.8 54.61)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000130")
        	)
        """);

        var nets = SchConnectivity.Build(sheet);
        var marked = Assert.Single(nets, n => n.Pins.Any(p => p.ToString() == "R1-2"));

        Assert.True(marked.IsNoConnect);

        // The other pin carries no such mark, and a check about loose pins should still speak up about it.
        var loose = Assert.Single(nets, n => n.Pins.Any(p => p.ToString() == "R1-1"));
        Assert.False(loose.IsNoConnect);
    }

    /// <summary>A label at a place, as KiCad writes one.</summary>
    private static string Label(string text, double x, double y, string uuid) => $"""
        	(label "{text}"
        		(at {x} {y} 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "{uuid}")
        	)
        """;

    [Fact]
    public void A_label_part_way_along_a_wire_names_that_wire()
    {
        // The label sits in the middle of the wire, where no end of anything is: KiCad asks for a dot only where
        // two wires meet, never where a label lands on one.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000102")
            + Label("VCC", 60.96, 54.61, "0a1b2c3d-0000-4000-8000-000000000103"));

        var named = Assert.Single(SchConnectivity.Build(sheet), n => n.IsNamed);

        Assert.Equal("VCC", named.Name);
        Assert.Contains(named.Pins, p => p.ToString() == "R1-2");
    }

    [Fact]
    public void A_pin_part_way_along_a_wire_is_on_it()
    {
        // The wire runs past the top pin of R2 rather than ending on it.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Resistor("R2", 60.96, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000104"));

        var net = Assert.Single(SchConnectivity.Build(sheet), n => n.Pins.Count > 1);

        Assert.Equal(["R1-2", "R2-2"], net.Pins.Select(p => p.ToString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_written_with_an_escape_is_the_name_it_stands_for()
    {
        // KiCad may not write a "/" in a label — that is the hierarchy separator — so it writes {slash}. The two
        // spellings are one net, and the escape is not part of the name.
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Resistor("R2", 88.9, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000105")
            + Wire(88.9, 54.61, 109.22, 54.61, "0a1b2c3d-0000-4000-8000-000000000106")
            + Label("VPP{slash}MCLR", 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000107")
            + Label("VPP/MCLR", 109.22, 54.61, "0a1b2c3d-0000-4000-8000-000000000108"));

        var named = Assert.Single(SchConnectivity.Build(sheet), n => n.IsNamed);

        Assert.Equal("VPP/MCLR", named.Name);
        Assert.Equal(2, named.Pins.Count);
        Assert.Equal("VPP/MCLR", sheet.Labels[0].Shown);
    }

    [Fact]
    public void A_pin_of_a_child_sheet_is_a_connection_of_this_one()
    {
        var sheet = Sheet(
            Resistor("R1", 50.8, 50.8)
            + Wire(50.8, 54.61, 71.12, 54.61, "0a1b2c3d-0000-4000-8000-000000000109")
            + """
        	(sheet
        		(at 71.12 50.8)
        		(size 20.32 20.32)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000110")
        		(property "Sheetname" "Child"
        			(at 71.12 50.04 0)
        		)
        		(property "Sheetfile" "child.kicad_sch"
        			(at 71.12 71.88 0)
        		)
        		(pin "IN" input
        			(at 71.12 54.61 180)
        			(uuid "0a1b2c3d-0000-4000-8000-000000000111")
        		)
        	)
        """);

        var net = Assert.Single(SchConnectivity.Build(sheet), n => n.SheetPins.Count > 0);

        Assert.Equal("IN", Assert.Single(net.SheetPins).Name);
        Assert.Equal("R1-2", Assert.Single(net.Pins).ToString());
        Assert.Equal(2, net.Connections);
        Assert.Equal("Net-(R1-2)", net.Name);
    }
}
