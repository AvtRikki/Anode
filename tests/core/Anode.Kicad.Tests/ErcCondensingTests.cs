using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// How many lines a quarrel is worth. Six pins that may not meet make fifteen pairs, and fifteen lines saying the
/// same thing is a list nobody reads — so KiCad condenses them, and the rule for which pin speaks and which partner
/// it is shown against is its own (<c>ERC_TESTER::TestPinToPin</c>).
/// </summary>
public class ErcCondensingTests
{
    private const long Mm = 1_000_000;

    [Fact]
    public void A_net_where_many_pins_quarrel_is_not_a_line_for_every_pair()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var nets = SchDesignNets.Build(root);
        var conflicts = SchErc.Check(nets).Where(f => f.Kind == ErcKind.PinConflict).ToList();

        int pairs = 0;
        foreach (var net in nets)
        {
            var pins = net.Pins.GroupBy(p => (p.Reference, p.Pin.Number)).Select(g => g.First()).ToList();
            for (int i = 0; i < pins.Count; i++)
            {
                for (int j = i + 1; j < pins.Count; j++)
                {
                    if (SchErc.Conflict(pins[i].Pin.Pin.ElectricalType, pins[j].Pin.Pin.ElectricalType) is not null)
                    {
                        pairs++;
                    }
                }
            }
        }

        Assert.NotEmpty(conflicts);
        Assert.True(conflicts.Count < pairs, $"{conflicts.Count} lines for {pairs} quarrelling pairs is no condensing at all");

        // No pin speaks twice: each one swallows every pair it takes part in.
        var spoken = conflicts.Select(f => f.Pins[0].ToString()).ToList();
        Assert.Equal(spoken.Count, spoken.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_pin_that_speaks_is_the_one_whose_type_says_most()
    {
        // Three pins on one wire: two outputs and one unspecified, which quarrels with everything.
        var net = Net(
            ("U1", "1", "output", 100 * Mm),
            ("U2", "1", "output", 120 * Mm),
            ("U3", "1", "unspecified", 160 * Mm));

        var conflicts = SchErc.Check([net]).Where(f => f.Kind == ErcKind.PinConflict).ToList();

        // The unspecified pin carries more weight than an output, so it is the one the findings are written against
        // — and it takes all three of its pairs with it, leaving the two outputs to be reported once between them.
        Assert.Equal(2, conflicts.Count);
        Assert.Equal("U3-1", conflicts[0].Pins[0].ToString());
        Assert.Equal("U1-1", conflicts[1].Pins[0].ToString());
    }

    [Fact]
    public void The_partner_shown_is_the_nearest_one()
    {
        var net = Net(
            ("U1", "1", "unspecified", 100 * Mm),
            ("U2", "1", "passive", 300 * Mm),
            ("U3", "1", "passive", 110 * Mm));

        var conflict = Assert.Single(
            SchErc.Check([net]),
            f => f.Kind == ErcKind.PinConflict && f.Pins[0].ToString() == "U1-1");

        // U3 stands ten millimetres away, U2 two hundred: the near one is the one worth showing.
        Assert.Equal("U3-1", conflict.Pins[1].ToString());
    }

    [Fact]
    public void Pins_of_one_part_drawn_on_top_of_each_other_are_not_a_quarrel()
    {
        // A chip's two ground pins, shown as one: same part, same place, same name and type.
        var stacked = Net(
            ("U1", "7", "power_out", 100 * Mm),
            ("U1", "8", "power_out", 100 * Mm));

        Assert.DoesNotContain(SchErc.Check([stacked]), f => f.Kind == ErcKind.PinConflict);

        // Move one of them and they are two pins again, which may not meet.
        var apart = Net(
            ("U1", "7", "power_out", 100 * Mm),
            ("U1", "8", "power_out", 120 * Mm));

        Assert.Single(SchErc.Check([apart]), f => f.Kind == ErcKind.PinConflict);
    }

    /// <summary>One net whose pins stand in a row, each as described.</summary>
    private static DesignNet Net(params (string Reference, string Number, string Type, long X)[] pins)
    {
        string folder = Directory.CreateTempSubdirectory("anode-erc-condense-").FullName;
        try
        {
            string path = Path.Combine(folder, "sheet.kicad_sch");
            File.WriteAllText(path, Sheet(pins));

            var design = SchDesignNets.Build(path);
            return design.OrderByDescending(n => n.Pins.Count).First();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A sheet with one wire and the given pins standing on it, each part drawn from its own definition.</summary>
    private static string Sheet((string Reference, string Number, string Type, long X)[] pins)
    {
        var text = new System.Text.StringBuilder();
        text.Append("(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n");
        text.Append("\t(lib_symbols\n");
        foreach (var group in pins.GroupBy(p => p.Reference))
        {
            text.Append($"\t\t(symbol \"Test:{group.Key}\" (property \"Reference\" \"{group.Key}\" (at 0 0 0))\n");
            text.Append($"\t\t\t(symbol \"{group.Key}_1_1\"\n");
            foreach (var pin in group)
            {
                // The pin's own offset inside the part puts it where the wire runs. Every pin of a part is named
                // alike, so two of them at one point are stacked the way a chip's several ground pins are.
                text.Append($"\t\t\t\t(pin {pin.Type} line (at {Mms(pin.X - group.First().X)} 0 180) (length 0)")
                    .Append($" (name \"{pin.Reference}\") (number \"{pin.Number}\"))\n");
            }

            text.Append("\t\t\t))\n");
        }

        text.Append("\t)\n");

        long left = pins.Min(p => p.X), right = pins.Max(p => p.X);
        text.Append($"\t(wire (pts (xy {Mms(left)} 50) (xy {Mms(right)} 50)) (stroke (width 0) (type default))")
            .Append(" (uuid \"1a1b2c3d-0000-4000-8000-000000000001\"))\n");

        int index = 0;
        foreach (var group in pins.GroupBy(p => p.Reference))
        {
            text.Append($"\t(symbol (lib_id \"Test:{group.Key}\") (at {Mms(group.First().X)} 50 0) (unit 1)")
                .Append($" (uuid \"0a1b2c3d-0000-4000-8000-00000000000{index++}\")\n")
                .Append($"\t\t(property \"Reference\" \"{group.Key}\" (at 0 0 0))\n")
                .Append($"\t\t(property \"Value\" \"part\" (at 0 0 0)))\n");
        }

        text.Append("\t(embedded_fonts no))\n");
        return text.ToString();
    }

    private static string Mms(long nm) => (nm / (double)Mm).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
