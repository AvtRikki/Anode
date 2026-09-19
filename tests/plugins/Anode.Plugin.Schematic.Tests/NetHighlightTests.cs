using Anode.Kicad;
using Anode.Render;
using Anode.Sdk;
using Anode.Tests;

// Inside this namespace "Schematic" names the plugin's own namespace segment, not the sheet.
using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// Lighting a net on a real sheet: every wire of it and the junction dots on those wires come up, the parts it
/// touches stay dim, the status bar names it, and an edit that takes its anchor away puts it out.
/// </summary>
public class NetHighlightTests
{
    [Fact]
    public void A_lit_net_is_its_wires_and_dots_and_nothing_else()
    {
        Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason);

        using var document = Open(out var sheet, out var scene);

        // The busiest net with a junction on it: the case where a missing dot or a missing wire would show.
        var net = document.Nets
            .Where(n => n.Items.OfType<SchWire>().Count() > 2)
            .OrderByDescending(n => n.Items.OfType<SchWire>().Count())
            .First(n => sheet.Junctions.Any(j => n.Items.OfType<SchWire>().Any(w => Touches(w, j.Position))));
        var wire = net.Items.OfType<SchWire>().First();

        // The status bar reads through the plugin's own catalog, which a test has to register itself.
        using var strings = Tr.Register(JsonTextCatalog.FromAssembly(typeof(SchematicDocument).Assembly));

        document.HighlightNet(wire);

        var lit = Assert.IsAssignableFrom<IReadOnlySet<int>>(document.LitNet);
        foreach (var member in net.Items.OfType<SchWire>())
        {
            Assert.Subset(lit.ToHashSet(), scene.OwnersOf(member).ToHashSet());
        }

        Assert.Contains(sheet.Junctions, j => scene.OwnersOf(j).Any(lit.Contains));
        Assert.DoesNotContain(sheet.Symbols, s => scene.OwnersOf(s).Any(lit.Contains));
        Assert.Contains(document.StatusFields, f => f.Text.Contains(net.Name, StringComparison.Ordinal));

        document.HighlightNet(null);
        Assert.Null(document.LitNet);
    }

    [Fact]
    public void Deleting_what_the_net_was_lit_from_puts_it_out()
    {
        Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason);

        using var document = Open(out var sheet, out _);
        var wire = sheet.Wires[0];
        document.HighlightNet(wire);
        Assert.NotNull(document.LitNet);

        document.Editor.SetSelection([wire]);
        document.Editor.DeleteSelection();

        Assert.Null(document.LitNet);
    }

    private static SchematicDocument Open(out KicadSchematic sheet, out SchematicScene scene)
    {
        sheet = KicadSchematic.Load(TestData.AnySchematic()!);
        scene = SchematicSceneBuilder.Build(sheet);
        string data = Directory.CreateTempSubdirectory("anode-net-").FullName;
        return new SchematicDocument(sheet, scene, TestData.AnySchematic()!, new SymbolLibraryList(data));
    }

    private static bool Touches(SchWire wire, Anode.Geometry.Vector2L point) =>
        wire.Points.Zip(wire.Points.Skip(1)).Any(s =>
            (s.First.X - point.X) * (s.Second.Y - point.Y) == (s.Second.X - point.X) * (s.First.Y - point.Y)
            && point.X >= Math.Min(s.First.X, s.Second.X) && point.X <= Math.Max(s.First.X, s.Second.X)
            && point.Y >= Math.Min(s.First.Y, s.Second.Y) && point.Y <= Math.Max(s.First.Y, s.Second.Y));
}
