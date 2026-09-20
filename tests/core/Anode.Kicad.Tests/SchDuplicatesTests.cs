using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Designators used twice, across a whole design. Run over the 151 KiCad projects on hand it reports exactly one:
/// the QA design built to have two packages under one name. Checked per file on the Reference property instead, it
/// reported 30 of 727 sheets, almost all of them wrong.
/// </summary>
public class SchDuplicatesTests
{
    private const string Sheet = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        {0}
        )
        """;

    private static Schematic Parts(params (string Reference, int Unit)[] parts) => Schematic.Parse(Sheet.Replace(
        "{0}",
        string.Concat(parts.Select((p, i) =>
            $"\t(symbol\n\t\t(lib_id \"Device:R\")\n\t\t(at {50.8 + i * 2.54} 44.45 0)\n\t\t(unit {p.Unit})\n"
            + $"\t\t(uuid \"0a1b2c3d-0000-4000-8000-00000000000{i}\")\n"
            + $"\t\t(property \"Reference\" \"{p.Reference}\"\n\t\t\t(at 50.8 44.45 0)\n\t\t)\n\t)\n")),
        StringComparison.Ordinal));

    private static IReadOnlyList<(string Reference, IReadOnlyList<DesignatorUse> Uses)> Find(Schematic sheet) =>
        SchDuplicates.Find([new SheetInstance("sheet", "/" + sheet.Uuid, "sheet", 0)], _ => sheet);

    [Fact]
    public void Two_parts_under_one_name_clash()
    {
        var found = Find(Parts(("R1", 1), ("R1", 1), ("R2", 1)));

        var (reference, uses) = Assert.Single(found);
        Assert.Equal("R1", reference);
        Assert.Equal(2, uses.Count);
    }

    [Fact]
    public void The_sections_of_one_package_share_a_name()
    {
        Assert.Empty(Find(Parts(("U1", 1), ("U1", 2), ("U1", 3))));
    }

    [Fact]
    public void Power_symbols_and_parts_waiting_for_a_number_are_not_parts_to_clash()
    {
        Assert.Empty(Find(Parts(("#PWR01", 1), ("#PWR01", 1), ("R?", 1), ("R?", 1), ("U", 1), ("U", 1))));
    }

    [Fact]
    public void A_sheet_placed_twice_is_not_a_clash_with_itself()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        Assert.SkipWhen(!File.Exists(root), TestData.SkipReason);

        var places = SchHierarchy.Walk(root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(SchDuplicates.Find(places, Schematic.Load));

        // Read off the property instead, every part of the amplifier would be named the same in both of its places.
        var byProperty = places
            .SelectMany(p => Schematic.Load(p.File).Symbols.Select(s => (s.Reference, s.Unit)))
            .Where(r => r.Reference is { } name && !name.StartsWith('#'))
            .GroupBy(r => r)
            .Count(g => g.Count() > 1);
        Assert.True(byProperty > 0);
    }

    [Fact]
    public void Two_packages_under_one_name_are_found_in_a_real_design()
    {
        string root = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "netlists",
            "test_multiunit_reannotate_5", "test_multiunit_reannotate_5.kicad_sch");
        Assert.SkipWhen(!File.Exists(root), TestData.SkipReason);

        var (reference, uses) = Assert.Single(SchDuplicates.Find(SchHierarchy.Walk(root, cancellationToken: TestContext.Current.CancellationToken), Schematic.Load));

        // Two packages of three sections each, both called U2.
        Assert.Equal("U2", reference);
        Assert.Equal([1, 1, 2, 2, 3, 3], uses.Select(u => u.Unit).Order());
    }
}
