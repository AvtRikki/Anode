using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The nets of a whole design rather than of one sheet: a sheet's pin meets the hierarchical label of that name
/// inside it, a global label or a power symbol joins its name wherever it appears, and a sheet placed twice is two
/// places whose parts have two designators.
/// </summary>
public class SchDesignNetsTests
{
    private const string Header = """
        	(version 20260206)
        	(generator "anode")
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
        """;

    /// <summary>A resistor upright at a place, with a designator for each place its sheet stands in.</summary>
    private static string Resistor(string uuid, double x, double y, params (string Path, string Reference)[] places) => $$"""
        	(symbol
        		(lib_id "Device:R")
        		(at {{x}} {{y}} 0)
        		(unit 1)
        		(uuid "{{uuid}}")
        		(property "Reference" "{{places[0].Reference}}"
        			(at {{x}} {{y}} 0)
        		)
        		(instances
        			(project "design"
        {{string.Join("\n", places.Select(p => $"\t\t\t\t(path \"{p.Path}\"\n\t\t\t\t\t(reference \"{p.Reference}\")\n\t\t\t\t\t(unit 1)\n\t\t\t\t)"))}}
        			)
        		)
        	)
        """;

    private static string Wire(double x1, double y1, double x2, double y2, string uuid) => $"""
        	(wire
        		(pts
        			(xy {x1} {y1}) (xy {x2} {y2})
        		)
        		(uuid "{uuid}")
        	)
        """;

    private static string Label(string kind, string text, double x, double y, double angle, string uuid) => $"""
        	({kind} "{text}"
        		(at {x} {y} {angle})
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "{uuid}")
        	)
        """;

    /// <summary>A root placing one child sheet twice, each child holding a resistor on a hierarchical label.</summary>
    private static string Design(string folder)
    {
        const string rootUuid = "11111111-0000-4000-8000-000000000001";
        const string firstPlace = "22222222-0000-4000-8000-000000000001";
        const string secondPlace = "22222222-0000-4000-8000-000000000002";

        string Sheet(string uuid, string name, double y) => $$"""
            	(sheet
            		(at 101.6 {{y}})
            		(size 20.32 20.32)
            		(uuid "{{uuid}}")
            		(property "Sheetname" "{{name}}"
            			(at 101.6 {{y - 0.76}} 0)
            		)
            		(property "Sheetfile" "block.kicad_sch"
            			(at 101.6 {{y + 21.08}} 0)
            		)
            		(pin "IN" input
            			(at 101.6 {{y + 3.81}} 180)
            			(uuid "33333333-0000-4000-8000-00000000000{{(uuid == firstPlace ? 1 : 2)}}")
            		)
            	)
            """;

        File.WriteAllText(Path.Combine(folder, "design.kicad_sch"), $$"""
            (kicad_sch
            {{Header}}
            	(uuid "{{rootUuid}}")
            {{Resistor("44444444-0000-4000-8000-000000000001", 50.8, 50.8, ("/" + rootUuid, "R1"))}}
            {{Wire(50.8, 54.61, 101.6, 54.61, "55555555-0000-4000-8000-000000000001")}}
            {{Wire(101.6, 54.61, 101.6, 92.71, "55555555-0000-4000-8000-000000000002")}}
            {{Label("label", "TOP", 71.12, 54.61, 0, "66666666-0000-4000-8000-000000000001")}}
            {{Sheet(firstPlace, "first", 50.8)}}
            {{Sheet(secondPlace, "second", 88.9)}}
            	(embedded_fonts no)
            )
            """);

        // The child: its hierarchical label reaches the sheet pin above, and a power symbol joins the design.
        File.WriteAllText(Path.Combine(folder, "block.kicad_sch"), $$"""
            (kicad_sch
            {{Header}}
            	(uuid "77777777-0000-4000-8000-000000000001")
            {{Resistor("88888888-0000-4000-8000-000000000001", 50.8, 50.8,
                ($"/{rootUuid}/{firstPlace}", "R2"), ($"/{rootUuid}/{secondPlace}", "R3"))}}
            {{Wire(50.8, 54.61, 71.12, 54.61, "99999999-0000-4000-8000-000000000001")}}
            {{Label("hierarchical_label", "IN", 71.12, 54.61, 0, "aaaaaaaa-0000-4000-8000-000000000001")}}
            	(embedded_fonts no)
            )
            """);

        return Path.Combine(folder, "design.kicad_sch");
    }

    [Fact]
    public void A_sheets_pin_and_the_label_inside_it_are_one_net()
    {
        string folder = Directory.CreateTempSubdirectory("anode-design-nets-").FullName;
        try
        {
            var nets = SchDesignNets.Build(Design(folder));

            // The root's wire, and the wire inside each of the two places, are one net named where it was labelled.
            var net = Assert.Single(nets, n => n.Name == "/TOP");
            Assert.Equal(3, net.Parts.Count);
            Assert.Equal(["/", "/first/", "/second/"], net.Parts.Select(p => p.Place.Trail).Order(StringComparer.Ordinal));

            // A part on a sheet placed twice answers to a designator per place.
            Assert.Equal(["R1-2", "R2-2", "R3-2"], net.Pins.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Sheets_that_share_nothing_keep_their_nets_apart()
    {
        string folder = Directory.CreateTempSubdirectory("anode-design-nets-").FullName;
        try
        {
            var nets = SchDesignNets.Build(Design(folder));

            // R2's lower pin is wired to nothing: one net per place, not one shared between them.
            var loose = nets.Where(n => !n.IsNamed && n.Pins.Any(p => p.Place.Depth > 0)).ToList();
            Assert.Equal(2, loose.Count);
            Assert.All(loose, n => Assert.Single(n.Parts));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_demo_designs_power_reaches_every_sheet_of_it()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var nets = SchDesignNets.Build(root);
        var power = Assert.Single(nets, n => n.Name == "+12V");

        // The amplifier sheet stands twice, and the supply of the root reaches both places.
        Assert.Equal(["/", "/ampli_ht_horizontal/", "/ampli_ht_vertical/"], power.Parts.Select(p => p.Place.Trail).Distinct().Order(StringComparer.Ordinal));
        Assert.True(power.Pins.Count > 20, $"+12V reaches {power.Pins.Count} pins");

        // Every net of the design is reachable by name, and no two carry the same one.
        Assert.Equal(nets.Count, nets.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A child sheet that will not parse leaves the rest of the design readable: the nets of the sheets that do read
    /// are still worked out, and the netlist is still written. The design is partial, and its diagnostics say so.
    /// </summary>
    [Fact]
    public void A_child_that_will_not_parse_does_not_stop_the_design()
    {
        string folder = Directory.CreateTempSubdirectory("anode-design-nets-").FullName;
        try
        {
            string root = Design(folder);
            File.WriteAllText(Path.Combine(folder, "block.kicad_sch"), "(kicad_sch (version 20250114) (generator \"eeschema\"");

            var nets = SchDesignNets.Build(root);
            Assert.NotEmpty(nets);
            Assert.Contains(nets, n => n.Name == "/TOP");
            Assert.All(nets, n => Assert.All(n.Parts, p => Assert.Equal("/", p.Place.Trail)));

            string netlist = SchNetlist.Write(root);
            Assert.StartsWith("(export (version \"E\")", netlist, StringComparison.Ordinal);
            Assert.Contains("R1", netlist, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
