using Anode.Geometry;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Connectivity against a real, correctly drawn sheet. Every hand-written fixture in these tests is an upright part
/// at no rotation, and all of them agreed with a wrong answer: pins were read at the far end of their line rather
/// than at the point a wire meets. Only a real design caught it, so a real design guards it.
/// </summary>
public class SchConnectivityFixtureTests
{
    [Fact]
    public void Most_pins_of_a_real_sheet_meet_the_wires_drawn_to_them()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        var pins = SchConnectivity.PinsOf(sheet);
        Assert.NotEmpty(pins);

        var wireEnds = new HashSet<Vector2L>();
        foreach (var wire in sheet.Wires.Where(w => !w.IsBus))
        {
            foreach (var point in wire.Points)
            {
                wireEnds.Add(point);
            }
        }

        int met = pins.Count(p => wireEnds.Contains(p.At));

        // A drawn schematic wires most of what it places. Reading the wrong end of the pin left one in ten here.
        Assert.True(
            met > pins.Count / 2,
            $"only {met} of {pins.Count} pins land where a wire ends; the pin's connection point is its own point");
    }

    [Fact]
    public void A_real_sheet_has_far_fewer_nets_than_pins()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        var pins = SchConnectivity.PinsOf(sheet);
        var nets = SchConnectivity.Build(sheet);

        // One net per pin means nothing joined at all, which is what a broken reading looks like.
        Assert.True(
            nets.Count < pins.Count,
            $"{nets.Count} nets for {pins.Count} pins: nothing is being joined");
    }

    [Fact]
    public void A_correctly_drawn_sheet_gives_a_check_nothing_to_complain_about()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        var nets = SchConnectivity.Build(sheet);

        // The condition the loose-pin check reports on: one pin, no name, nobody marked it.
        var loose = nets.Where(n => !n.IsNamed && !n.IsNoConnect && n.Pins.Count == 1).ToList();

        // A finished design should give it nothing to say. When the pins were read at the wrong end this stood at
        // 138, and every one of them was noise — which is how a check teaches its reader to stop looking.
        Assert.True(
            loose.Count == 0,
            $"{loose.Count} pins would be reported on a drawn sheet, starting with "
            + string.Join(", ", loose.Take(5).Select(n => n.Pins[0].ToString())));
    }
}
