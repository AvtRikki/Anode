using Anode.Geometry;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Unfolding a net from a bus: the nets a bus offers, and where the entry and the label go for the one chosen.
/// </summary>
public class SchBusUnfoldTests
{
    [Fact]
    public void A_vector_bus_offers_its_range()
    {
        var sheet = Sheet("D[0..3]");

        Assert.Equal(["D0", "D1", "D2", "D3"], SchBusUnfold.Members(sheet, sheet.Wires.Single()));
    }

    [Fact]
    public void An_alias_offers_what_it_is_declared_to_carry()
    {
        var sheet = Sheet("DPHY", alias: "(bus_alias \"DPHY\" (members \"CLK_P\" \"CLK_N\" \"D0_P\"))");

        Assert.Equal(["CLK_P", "CLK_N", "D0_P"], SchBusUnfold.Members(sheet, sheet.Wires.Single()));
    }

    [Fact]
    public void A_bus_with_no_name_on_it_offers_nothing()
    {
        var sheet = Sheet(null);

        Assert.Empty(SchBusUnfold.Members(sheet, sheet.Wires.Single()));
    }

    /// <summary>
    /// The entry stands on the bus where it passes nearest the pointer, 100 mil each way, leaning toward the pointer;
    /// the label names the net at its far end, where the wire starts. Off a bus that runs down, the pointer can only be
    /// to one side of it, so the entry leans down, as KiCad's does before the pointer has moved.
    /// </summary>
    [Theory]
    [InlineData(52.0, 60.0, 2.54, 2.54)]
    [InlineData(49.0, 60.0, -2.54, 2.54)]
    public void The_entry_leans_toward_the_pointer(double x, double y, double sx, double sy)
    {
        var sheet = Sheet("D[0..3]");
        var bus = sheet.Wires.Single();
        var pointer = Mm(x, y);

        var unfolding = SchBusUnfold.Unfold(bus, "D2", pointer, pointer)!;

        // The bus runs down x = 50.8, so the entry stands at the pointer's height, on the bus.
        Assert.Equal(Mm(50.8, y), unfolding.Entry.Position);
        Assert.Equal(Mm(sx, sy), unfolding.Entry.Size);
        Assert.Equal(unfolding.Entry.EndPoint, unfolding.WireEnd);
        Assert.Equal("D2", unfolding.Label.Shown);
        Assert.Equal(unfolding.WireEnd, unfolding.Label.Position);
    }

    /// <summary>On KiCad's own bus test designs, every labelled bus offers members.</summary>
    [Fact]
    public void On_KiCads_bus_designs_labelled_buses_offer_their_members()
    {
        string file = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "netlists", "bus_connection", "a.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Schematic.Load(file);
        var labelled = sheet.Wires.Where(w => w.IsBus && SchBuses.At(sheet, w.Points[0]) is { Names.Count: > 0 }).ToList();

        Assert.NotEmpty(labelled);
        Assert.All(labelled, bus => Assert.NotEmpty(SchBusUnfold.Members(sheet, bus)));
    }

    private static Schematic Sheet(string? name, string alias = "") => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + (alias.Length > 0 ? "\t" + alias + "\n" : string.Empty)
        + "\t(bus (pts (xy 50.8 40.64) (xy 50.8 81.28)) (stroke (width 0) (type default)) (uuid \"1a1b2c3d-0000-4000-8000-000000000001\"))\n"
        + (name is null ? string.Empty
            : $"\t(label \"{name}\" (at 50.8 45.72 90) (effects (font (size 1.27 1.27))) (uuid \"1a1b2c3d-0000-4000-8000-000000000002\"))\n")
        + "\t(embedded_fonts no))\n");

    private static Vector2L Mm(double x, double y) => new((long)Math.Round(x * 1_000_000), (long)Math.Round(y * 1_000_000));
}
