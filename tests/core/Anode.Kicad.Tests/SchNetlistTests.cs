using Anode.Sexpr;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The netlist, checked against KiCad's own. Its QA cases each ship the schematic and the netlist KiCad exported
/// from it, so what we write can be compared with what KiCad writes for the same design: the same nets, each
/// reaching the same pins.
/// </summary>
public class SchNetlistTests
{
    private static string Case(string name) =>
        TestData.FullPath($"qa/data/eeschema/netlists/{name}/{name}");

    /// <summary>Every net of a netlist as name → the pins it reaches, sorted, so two netlists can be compared.</summary>
    private static Dictionary<string, List<string>> Nets(string text) =>
        SDocument.Parse(text).Root.Lists().First(l => l.Head == "nets").Lists().Where(l => l.Head == "net")
            .ToDictionary(
                n => n.Find("name")!.AtomAt(1)!.Value,
                n => n.Lists().Where(l => l.Head == "node")
                    .Select(x => $"{x.Find("ref")!.AtomAt(1)!.Value}-{x.Find("pin")!.AtomAt(1)!.Value}")
                    .Order(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

    [Theory]
    [InlineData("issue14657")]
    [InlineData("test_hier_no_connect")]
    public void A_netlist_says_what_KiCads_own_says(string name)
    {
        Assert.SkipUnless(File.Exists(Case(name) + ".net"), TestData.SkipReason);

        var ours = Nets(SchNetlist.Write(Case(name) + ".kicad_sch"));
        var kicad = Nets(File.ReadAllText(Case(name) + ".net"));

        Assert.Equal(kicad.Keys.Order(StringComparer.Ordinal), ours.Keys.Order(StringComparer.Ordinal));
        foreach (var (net, pins) in kicad)
        {
            Assert.Equal(pins, ours[net]);
        }
    }

    /// <summary>
    /// A design whose sheets are named through bus aliases: the same nets reaching the same pins, but KiCad calls
    /// them after the bus they came in on ("/S0.BOOT.SDA") where we call them after the sheet that named them.
    /// </summary>
    [Fact]
    public void Bus_alias_names_still_group_the_same_pins()
    {
        Assert.SkipUnless(File.Exists(Case("hierarchy_aliases") + ".net"), TestData.SkipReason);

        var ours = Nets(SchNetlist.Write(Case("hierarchy_aliases") + ".kicad_sch"));
        var kicad = Nets(File.ReadAllText(Case("hierarchy_aliases") + ".net"));

        Assert.Equal(
            kicad.Values.Select(v => string.Join(" ", v)).Order(StringComparer.Ordinal),
            ours.Values.Select(v => string.Join(" ", v)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_file_carries_the_design_its_parts_and_their_definitions()
    {
        Assert.SkipUnless(File.Exists(Case("test_hier_no_connect") + ".kicad_sch"), TestData.SkipReason);

        string text = SchNetlist.Write(Case("test_hier_no_connect") + ".kicad_sch", tool: "Anode 0.1", when: new DateTime(2026, 9, 19, 13, 45, 0));
        var root = SDocument.Parse(text).Root;

        Assert.Equal("export", root.Head);
        Assert.Equal("E", root.Find("version")!.AtomAt(1)!.Value);

        var design = root.Find("design")!;
        Assert.Equal("Anode 0.1", design.Find("tool")!.AtomAt(1)!.Value);
        Assert.Contains("2026", design.Find("date")!.AtomAt(1)!.Value, StringComparison.Ordinal);

        // A sheet of the design per place, the root first, named as KiCad names them.
        var sheets = design.Lists().Where(l => l.Head == "sheet").ToList();
        Assert.Equal(4, sheets.Count);
        Assert.Equal("/", sheets[0].Find("name")!.AtomAt(1)!.Value);
        Assert.Equal("/", sheets[0].Find("tstamps")!.AtomAt(1)!.Value);
        Assert.All(sheets.Skip(1), s => Assert.StartsWith("/", s.Find("tstamps")!.AtomAt(1)!.Value, StringComparison.Ordinal));

        // Every part of every place, with where it stands and what it was drawn from.
        var comps = root.Find("components")!.Lists().Where(l => l.Head == "comp").ToList();
        Assert.NotEmpty(comps);
        Assert.All(comps, c =>
        {
            Assert.NotNull(c.Find("libsource"));
            Assert.NotNull(c.Find("sheetpath"));
            Assert.False(c.Find("ref")!.AtomAt(1)!.Value.StartsWith('#'), "a power symbol is not a part of the netlist");
        });

        // The definitions, with their pins.
        var parts = root.Find("libparts")!.Lists().Where(l => l.Head == "libpart").ToList();
        Assert.NotEmpty(parts);
        Assert.Contains(parts, p => p.Find("pins") is { } pins && pins.Lists().Any(x => x.Head == "pin"));
    }
}
