using Anode.Kicad;
using Anode.Tests;

namespace Anode.Render.Tests;

/// <summary>
/// A sheet placed twice is drawn with the designators of the appearance on show. The drawing holds text as strokes
/// rather than strings, so the test compares the strokes of the field layer between appearances.
/// </summary>
public class SceneAppearanceTests
{
    [Fact]
    public void The_designators_drawn_are_those_of_the_appearance_on_show()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        string amplifier = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "ampli_ht.kicad_sch");
        Assert.SkipWhen(!File.Exists(root) || !File.Exists(amplifier), TestData.SkipReason);

        var paths = SchHierarchy.Walk(root)
            .Where(i => i.File == Path.GetFullPath(amplifier))
            .Select(i => i.Path)
            .ToList();
        var sheet = Schematic.Load(amplifier);

        // The property holds the designator of whichever appearance KiCad saved from.
        string first = paths.Single(p => sheet.Symbols.All(s => s.ReferenceAt(p) == s.Reference));
        string second = paths.Single(p => p != first);

        Assert.Equal(Fields(null), Fields(first));
        Assert.NotEqual(Fields(first), Fields(second));

        List<LinePrim> Fields(string? path) =>
            [.. SchematicSceneBuilder.Build(sheet, path).Layers.Single(l => l.Name == LayerStyle.Sch.Field).Lines];
    }
}
