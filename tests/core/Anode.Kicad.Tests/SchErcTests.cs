using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The electrical rules, over KiCad's own tables: which pins may meet, and which nets nobody drives. The table
/// itself is checked cell by cell against KiCad's; these say what the checks make of a real design.
/// </summary>
public class SchErcTests
{
    [Theory]
    [InlineData("output", "output", "Error")]
    [InlineData("power_out", "power_out", "Error")]
    [InlineData("output", "power_out", "Error")]
    [InlineData("output", "tri_state", "Warning")]
    [InlineData("unspecified", "passive", "Warning")]
    [InlineData("no_connect", "passive", "Error")]
    [InlineData("output", "input", null)]
    [InlineData("passive", "passive", null)]
    [InlineData("power_out", "power_in", null)]
    [InlineData("nonsense", "passive", null)]
    public void The_matrix_says_which_pins_may_meet(string first, string second, string? expected)
    {
        Assert.Equal(expected, SchErc.Conflict(first, second)?.ToString());
    }

    [Fact]
    public void The_matrix_reads_the_same_both_ways_round()
    {
        string[] types =
        [
            "input", "output", "bidirectional", "tri_state", "passive", "free",
            "unspecified", "power_in", "power_out", "open_collector", "open_emitter", "no_connect",
        ];

        // KiCad's own table is symmetric; a check that depended on which pin was looked at first would report a
        // conflict on one sheet and not on another drawn the other way round.
        foreach (string a in types)
        {
            foreach (string b in types)
            {
                Assert.Equal(SchErc.Conflict(a, b), SchErc.Conflict(b, a));
            }
        }
    }

    [Fact]
    public void A_demo_designs_findings_are_the_two_kinds_and_nothing_else()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var findings = SchErc.Check(SchDesignNets.Build(root));

        // Two power flags on one net may not meet, and a supply pin with nothing driving it is the missing flag.
        Assert.Contains(findings, f => f.Kind == ErcKind.PinConflict && f.Net.Name == "GND");
        Assert.Contains(findings, f => f.Kind == ErcKind.PowerNotDriven);

        // Quiet enough to read: a board of this size is not drowned in findings.
        Assert.True(findings.Count < 10, $"{findings.Count} findings on a demo the size of pic_programmer");
    }

    [Fact]
    public void A_design_whose_sheets_are_wired_properly_says_nothing()
    {
        string root = TestData.FullPath("qa/data/eeschema/netlists/bus_connection/bus_connection.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        Assert.Empty(SchErc.Check(SchDesignNets.Build(root)));
    }

    [Fact]
    public void A_no_connect_mark_answers_for_the_net_it_stands_on()
    {
        string root = TestData.FullPath("qa/data/eeschema/netlists/test_hier_no_connect/test_hier_no_connect.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        // Its nets are pins left alone on purpose; the marks say so, so nothing is reported.
        Assert.DoesNotContain(SchErc.Check(SchDesignNets.Build(root)), f => f.Kind is ErcKind.NotDriven or ErcKind.PowerNotDriven);
    }

    [Fact]
    public void A_sheets_pins_and_the_labels_inside_it_must_answer_each_other()
    {
        string folder = Directory.CreateTempSubdirectory("anode-erc-sheets-").FullName;
        try
        {
            const string rootUuid = "11111111-0000-4000-8000-000000000001";
            File.WriteAllText(Path.Combine(folder, "design.kicad_sch"), $$"""
                (kicad_sch (version 20250114) (generator "eeschema") (uuid "{{rootUuid}}") (paper "A4")
                	(sheet (at 101.6 50.8) (size 20.32 20.32) (uuid "22222222-0000-4000-8000-000000000001")
                		(property "Sheetname" "block" (at 101.6 50 0))
                		(property "Sheetfile" "block.kicad_sch" (at 101.6 71.9 0))
                		(pin "IN" input (at 101.6 54.61 180) (uuid "33333333-0000-4000-8000-000000000001"))
                		(pin "SPARE" input (at 101.6 58.42 180) (uuid "33333333-0000-4000-8000-000000000002")))
                	(embedded_fonts no))

                """);

            // The child answers IN, does not answer SPARE, and offers OUT that the symbol above has no pin for.
            File.WriteAllText(Path.Combine(folder, "block.kicad_sch"), """
                (kicad_sch (version 20250114) (generator "eeschema") (uuid "77777777-0000-4000-8000-000000000001") (paper "A4")
                	(hierarchical_label "IN" (at 50.8 50.8 0) (effects (font (size 1.27 1.27))) (uuid "44444444-0000-4000-8000-000000000001"))
                	(hierarchical_label "OUT" (at 50.8 60.96 0) (effects (font (size 1.27 1.27))) (uuid "44444444-0000-4000-8000-000000000002"))
                	(embedded_fonts no))

                """);

            string root = Path.Combine(folder, "design.kicad_sch");
            var findings = SchErc.CheckSheets(SchHierarchy.Walk(root, cancellationToken: TestContext.Current.CancellationToken), Anode.Kicad.Schematic.Load);

            Assert.Equal(
                [(ErcKind.SheetPinWithoutLabel, "SPARE"), (ErcKind.LabelWithoutSheetPin, "OUT")],
                findings.Select(f => (f.Kind, f.Name)));
            Assert.All(findings, f => Assert.Equal("block", f.Place.Name));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_design_whose_sheets_answer_each_other_says_nothing()
    {
        string root = TestData.FullPath("qa/data/eeschema/netlists/test_hier_no_connect/test_hier_no_connect.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        Assert.Empty(SchErc.CheckSheets(SchHierarchy.Walk(root, cancellationToken: TestContext.Current.CancellationToken), Anode.Kicad.Schematic.Load));
    }
}
