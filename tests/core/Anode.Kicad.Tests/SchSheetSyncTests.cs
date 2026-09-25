using Anode.Geometry;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Bringing a sheet symbol's pins in step with the hierarchical labels inside it: what agrees, what does not, and
/// the three ways of making it agree that write only the sheet on screen.
/// </summary>
public class SchSheetSyncTests
{
    [Fact]
    public void Pins_and_labels_are_sorted_into_what_agrees_and_what_does_not()
    {
        var (sheet, child) = Pair(
            pins: [("SDA", "bidirectional"), ("CLK", "input"), ("OLD", "input")],
            labels: [("SDA", "bidirectional"), ("CLK", "output"), ("RESET", "input"), ("IRQ", "output")]);

        var match = SchSheetSync.Compare(sheet, child);

        Assert.Equal("SDA", Assert.Single(match.Matched).Label.Shown);
        Assert.Equal("CLK", Assert.Single(match.ShapeDiffers).Pin.Name);
        Assert.Equal(["IRQ", "RESET"], match.LabelsWithoutPin.Select(l => l.Shown));
        Assert.Equal("OLD", Assert.Single(match.PinsWithoutLabel).Name);
        Assert.False(match.IsInStep);
    }

    /// <summary>A label written twice inside the sheet is one signal, and wants one pin.</summary>
    [Fact]
    public void A_label_written_twice_counts_once()
    {
        var (sheet, child) = Pair(pins: [], labels: [("SDA", "input"), ("SDA", "input")]);

        Assert.Single(SchSheetSync.Compare(sheet, child).LabelsWithoutPin);
    }

    /// <summary>Names are compared as shown: the escaped slash inside is the same name as the bare one outside.</summary>
    [Fact]
    public void An_escaped_name_answers_the_same_name_written_plainly()
    {
        var (sheet, child) = Pair(pins: [("VPP/MCLR", "input")], labels: [("VPP{slash}MCLR", "input")]);

        Assert.True(SchSheetSync.Compare(sheet, child).IsInStep);
    }

    /// <summary>
    /// Added pins land on the edge, out of the way of the pins already there: outputs on the right, the rest on
    /// the left, one pitch apart, and then every pin agrees with a label.
    /// </summary>
    [Fact]
    public void Added_pins_go_down_the_edges_clear_of_the_ones_already_there()
    {
        var (sheet, child) = Pair(
            pins: [("SDA", "bidirectional")],
            labels: [("SDA", "bidirectional"), ("RESET", "input"), ("IRQ", "output"), ("CLK", "input")]);
        var missing = SchSheetSync.Compare(sheet, child).LabelsWithoutPin;

        var unplaced = SchSheetSync.AddPins(sheet, missing);

        Assert.Empty(unplaced);
        Assert.True(SchSheetSync.Compare(sheet, child).IsInStep);

        long left = sheet.Position.X, right = sheet.Position.X + sheet.Size.X;
        Assert.Equal(right, sheet.Pins.Single(p => p.Name == "IRQ").Position.X);
        Assert.Equal(left, sheet.Pins.Single(p => p.Name == "RESET").Position.X);

        // No two pins on one edge closer than the pitch, and none in a corner.
        foreach (var edge in sheet.Pins.GroupBy(p => p.Position.X))
        {
            var ys = edge.Select(p => p.Position.Y).Order().ToList();
            Assert.All(ys.Zip(ys.Skip(1)), pair => Assert.True(pair.Second - pair.First >= SchSheetSync.PinPitchNm));
            Assert.All(ys, y => Assert.InRange(y, sheet.Position.Y + 1, sheet.Position.Y + sheet.Size.Y - 1));
        }

        // What a pin written by KiCad says: the label's shape, and the angle of the edge it stands on.
        var irq = sheet.Pins.Single(p => p.Name == "IRQ");
        var reset = sheet.Pins.Single(p => p.Name == "RESET");
        Assert.Equal(("output", 0.0), (irq.Shape, irq.Angle));
        Assert.Equal(("input", 180.0), (reset.Shape, reset.Angle));
    }

    [Fact]
    public void A_sheet_too_small_for_them_all_keeps_the_rest_out()
    {
        // Room for one pin a side: the sheet is two pitches tall, and a corner is no place for a pin.
        var (sheet, child) = Pair(
            pins: [],
            labels: [("A", "input"), ("B", "input"), ("C", "input")],
            height: 2 * SchSheetSync.PinPitchNm);

        var unplaced = SchSheetSync.AddPins(sheet, SchSheetSync.Compare(sheet, child).LabelsWithoutPin);

        Assert.Equal(2, sheet.Pins.Count);
        Assert.Equal("C", Assert.Single(unplaced).Shown);
    }

    [Fact]
    public void Pins_that_name_nothing_inside_are_taken_off()
    {
        var (sheet, child) = Pair(pins: [("SDA", "input"), ("OLD", "input"), ("GONE", "output")], labels: [("SDA", "input")]);

        SchSheetSync.RemovePins(sheet, SchSheetSync.Compare(sheet, child).PinsWithoutLabel);

        Assert.Equal("SDA", Assert.Single(sheet.Pins).Name);
        Assert.True(SchSheetSync.Compare(sheet, child).IsInStep);
    }

    /// <summary>A pin takes the label's name and shape and keeps its place: KiCad's "use the label as a template".</summary>
    [Fact]
    public void A_pin_takes_the_name_and_shape_of_a_label()
    {
        var (sheet, child) = Pair(pins: [("SDA_OLD", "input")], labels: [("SDA", "bidirectional")]);
        var pin = sheet.Pins.Single();
        var at = pin.Position;

        SchSheetSync.Adopt(pin, child.Labels.Single());

        var now = sheet.Pins.Single();
        Assert.Equal("SDA", now.Name);
        Assert.Equal("bidirectional", now.Shape);
        Assert.Equal(at, now.Position);
        Assert.True(SchSheetSync.Compare(sheet, child).IsInStep);
    }

    /// <summary>
    /// On KiCad's own demos the comparison says what the ERC says: a pin without a label, or a label without a pin,
    /// is exactly what the check reports for that sheet.
    /// </summary>
    [Theory]
    [InlineData("complex_hierarchy", "complex_hierarchy.kicad_sch")]
    [InlineData("pic_programmer", "pic_programmer.kicad_sch")]
    public void On_the_demos_it_agrees_with_the_check(string folder, string file)
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", folder, file);
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var cache = new Dictionary<string, Schematic?>(StringComparer.Ordinal);
        Schematic? Open(string path) => cache.TryGetValue(path, out var known) ? known
            : cache[path] = File.Exists(path) ? Schematic.Load(path) : null;

        var places = SchHierarchy.Walk(root, Open, cancellationToken: TestContext.Current.CancellationToken);
        var findings = SchErc.CheckSheets(places, Open);

        int compared = 0;
        foreach (var place in places.Where(p => p.Placement is not null && p.Parent is not null))
        {
            if (Open(place.File) is not { } child)
            {
                continue;
            }

            var match = SchSheetSync.Compare(place.Placement!, child);
            var here = findings.Where(f => ReferenceEquals(f.Place, place)).ToList();
            Assert.Equal(here.Count(f => f.Kind == ErcKind.SheetPinWithoutLabel), match.PinsWithoutLabel.Count);
            Assert.Equal(here.Count(f => f.Kind == ErcKind.LabelWithoutSheetPin), match.LabelsWithoutPin.Count);
            compared++;
        }

        Assert.True(compared > 0, "the demo has no sheets to compare");
    }

    /// <summary>A sheet symbol with the given pins, and the sheet inside it with the given hierarchical labels.</summary>
    private static (SchSheet Sheet, Schematic Child) Pair(
        (string Name, string Shape)[] pins, (string Name, string Shape)[] labels, long height = 20_320_000)
    {
        var at = new Vector2L(40_640_000, 30_480_000);
        int n = 0;
        string pinText = string.Concat(pins.Select(p =>
            $"\t\t(pin \"{p.Name}\" {p.Shape} (at 40.64 {Mm(at.Y + ((++n + 1) * SchSheetSync.PinPitchNm))} 180)"
            + $" (uuid \"4a1b2c3d-0000-4000-8000-00000000000{n}\") (effects (font (size 1.27 1.27)) (justify left)))\n"));

        var parent = Schematic.Parse(
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + $"\t(sheet (at 40.64 30.48) (size 20.32 {Mm(height)}) (uuid \"3a1b2c3d-0000-4000-8000-000000000001\")\n"
            + "\t\t(property \"Sheetname\" \"Child\" (at 40.64 29 0))\n"
            + "\t\t(property \"Sheetfile\" \"child.kicad_sch\" (at 40.64 52 0))\n"
            + pinText
            + "\t)\n"
            + "\t(embedded_fonts no))\n");

        int m = 0;
        var child = Schematic.Parse(
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"7f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + string.Concat(labels.Select(l =>
                $"\t(hierarchical_label \"{l.Name}\" (shape {l.Shape}) (at 100 {50 + (5 * ++m)} 0)"
                + $" (effects (font (size 1.27 1.27))) (uuid \"5a1b2c3d-0000-4000-8000-00000000000{m}\"))\n"))
            + "\t(embedded_fonts no))\n");

        return (parent.Sheets.Single(), child);

        static string Mm(long nm) => (nm / 1_000_000.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
