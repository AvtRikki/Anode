using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// A design as a tree of sheet appearances, on KiCad's own complex_hierarchy demo: one amplifier sheet placed twice,
/// so every part on it has two designators. A symbol's name is only meaningful together with the path it is read at.
/// </summary>
public class SchHierarchyTests
{
    private static string Root => Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");

    private static string Amplifier => Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "ampli_ht.kicad_sch");

    private static bool Missing => !File.Exists(Root) || !File.Exists(Amplifier);

    [Fact]
    public void A_sheet_placed_twice_appears_twice_under_two_paths()
    {
        Assert.SkipWhen(Missing, TestData.SkipReason);

        var instances = SchHierarchy.Walk(Root);

        Assert.Equal(0, instances[0].Depth);
        Assert.Equal(Path.GetFullPath(Root), instances[0].File);

        var amplifiers = instances.Where(i => i.File == Path.GetFullPath(Amplifier)).ToList();
        Assert.Equal(2, amplifiers.Count);
        Assert.Equal(2, amplifiers.Select(a => a.Path).Distinct().Count());
        Assert.All(amplifiers, a => Assert.StartsWith(instances[0].Path + "/", a.Path, StringComparison.Ordinal));
        Assert.Equal(["ampli_ht_horizontal", "ampli_ht_vertical"], amplifiers.Select(a => a.Name).Order());
    }

    [Fact]
    public void One_symbol_carries_a_designator_per_appearance()
    {
        Assert.SkipWhen(Missing, TestData.SkipReason);

        var paths = SchHierarchy.Walk(Root).Where(i => i.File == Path.GetFullPath(Amplifier)).Select(i => i.Path).ToList();
        var sheet = Schematic.Load(Amplifier);

        // Every part on the reused sheet is named differently in each appearance — that is what reuse means.
        foreach (var symbol in sheet.Symbols.Where(s => s.Definition?.IsPower != true))
        {
            var names = paths.Select(p => symbol.ReferenceAt(p)).ToList();
            Assert.Equal(2, names.Distinct().Count());
        }

        // The property holds one of them at most, so reading it alone is wrong for the other appearance.
        var first = sheet.Symbols.First(s => s.Definition?.IsPower != true);
        Assert.Contains(first.Reference, paths.Select(p => first.ReferenceAt(p)));
    }

    [Fact]
    public void Without_a_path_the_property_is_what_is_left()
    {
        Assert.SkipWhen(Missing, TestData.SkipReason);

        var symbol = Schematic.Load(Amplifier).Symbols[0];

        Assert.Equal(symbol.Reference, symbol.ReferenceAt(null));
        Assert.Equal(symbol.Reference, symbol.ReferenceAt("/not/a/path/in/this/design"));
    }

    [Fact]
    public void Renaming_a_part_in_one_appearance_leaves_the_other_alone()
    {
        Assert.SkipWhen(Missing, TestData.SkipReason);

        var paths = SchHierarchy.Walk(Root).Where(i => i.File == Path.GetFullPath(Amplifier)).Select(i => i.Path).ToList();
        var symbol = Schematic.Load(Amplifier).Symbols.First(s => s.Definition?.IsPower != true);
        string other = symbol.ReferenceAt(paths[1])!;

        SchWrites.SetReference(symbol, "R999", paths[0]);

        Assert.Equal("R999", symbol.ReferenceAt(paths[0]));

        // Written into every path, both copies of the part would now be called R999.
        Assert.Equal(other, symbol.ReferenceAt(paths[1]));
    }
}
