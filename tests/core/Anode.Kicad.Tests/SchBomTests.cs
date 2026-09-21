using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The bill of materials: one line per part that is the same thing, grouped as KiCad groups it by default, with the
/// designator each place of a reused sheet gives its parts. A power symbol is not a part; nor is one the file marks
/// as not for the bill.
/// </summary>
public class SchBomTests
{
    private static string Root => Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");

    [Fact]
    public void Every_part_of_every_place_is_on_it_once()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        var lines = SchBom.Build(Root);
        var references = lines.SelectMany(l => l.References).ToList();

        Assert.NotEmpty(lines);
        Assert.Equal(references.Count, references.Distinct(StringComparer.Ordinal).Count());
        Assert.All(references, r => Assert.False(r.StartsWith('#'), $"{r} is a power symbol, not a part"));

        // The amplifier sheet stands twice, so its parts are there under both designators.
        Assert.Contains("R201", references);
        Assert.Contains("R301", references);

        // Quantity is how many designators the line carries, and a line's own designators count up.
        Assert.All(lines, l => Assert.Equal(l.References.Count, l.Quantity));
        Assert.All(lines, l => Assert.Equal(l.References, l.References.OrderBy(r => r.Length).ThenBy(r => r, StringComparer.Ordinal)));
    }

    /// <summary>
    /// KiCad keeps a part off the bill by saying it is not on it — <c>(in_bom no)</c>, the positive way round. The
    /// board's files spell the same idea as <c>exclude_from_bom</c>, a word the schematic format does not have, so
    /// reading for that one instead would quietly put every excluded part back on the bill.
    /// </summary>
    [Fact]
    public void The_parts_a_design_keeps_off_the_bill_are_off_it()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        string sheet = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipUnless(File.Exists(sheet), TestData.SkipReason);

        var design = Anode.Kicad.Schematic.Load(sheet);
        var excluded = design.Symbols.Where(s => !s.InBom && s.Reference is { Length: > 0 }).ToList();
        Assert.NotEmpty(excluded);

        var references = SchBom.Build(sheet).SelectMany(l => l.References).ToHashSet(StringComparer.Ordinal);
        foreach (var part in excluded)
        {
            Assert.DoesNotContain(part.Reference!, references);
        }
    }

    [Fact]
    public void Parts_of_one_value_share_a_line()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        var lines = SchBom.Build(Root);
        var shared = lines.Where(l => l.Quantity > 1).ToList();

        Assert.NotEmpty(shared);
        Assert.All(shared, l => Assert.All(l.References, r => Assert.NotEqual(string.Empty, r)));

        // One line per value, datasheet, footprint and do-not-place: nothing is listed twice under the same four.
        Assert.Equal(
            lines.Count,
            lines.Select(l => (l.Value, l.Datasheet, l.Footprint, l.Dnp)).Distinct().Count());
    }

    [Fact]
    public void The_csv_is_the_columns_KiCad_writes()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        string[] rows = SchBom.Write(Root).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("\"Reference\",\"Value\",\"Datasheet\",\"Footprint\",\"Qty\",\"DNP\"", rows[0]);
        Assert.Equal(SchBom.Build(Root).Count, rows.Length - 1);

        // Six columns on every row: the commas between designators sit inside a quoted field and are not columns.
        Assert.All(rows, r => Assert.Equal(5, r.Count(c => c == ',') - Commas(r)));
    }

    [Fact]
    public void A_part_kept_off_the_bill_is_not_on_it()
    {
        string folder = Directory.CreateTempSubdirectory("anode-bom-").FullName;
        try
        {
            string path = Path.Combine(folder, "sheet.kicad_sch");
            File.WriteAllText(path, """
                (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
                	(lib_symbols
                		(symbol "Device:R" (property "Reference" "R" (at 0 0 0))
                			(symbol "R_1_1" (pin passive line (at 0 3.81 270) (length 1.27) (name "~") (number "1")))))
                	(symbol (lib_id "Device:R") (at 50.8 50.8 0) (unit 1) (uuid "0a1b2c3d-0000-4000-8000-000000000001")
                		(property "Reference" "R1" (at 50.8 50.8 0))
                		(property "Value" "10k" (at 50.8 50.8 0)))
                	(symbol (lib_id "Device:R") (at 63.5 50.8 0) (unit 1) (in_bom no) (uuid "0a1b2c3d-0000-4000-8000-000000000002")
                		(property "Reference" "R2" (at 63.5 50.8 0))
                		(property "Value" "10k" (at 63.5 50.8 0)))
                	(symbol (lib_id "Device:R") (at 76.2 50.8 0) (unit 1) (dnp yes) (uuid "0a1b2c3d-0000-4000-8000-000000000003")
                		(property "Reference" "R3" (at 76.2 50.8 0))
                		(property "Value" "10k" (at 76.2 50.8 0)))
                	(embedded_fonts no))

                """);

            var lines = SchBom.Build(path);

            // R2 is kept off the bill — KiCad says so as "(in_bom no)" — and R3 is on it, but on a line of its own
            // because it is not to be placed.
            Assert.Equal([["R1"], ["R3"]], lines.Select(l => l.References));
            Assert.Equal([false, true], lines.Select(l => l.Dnp));
            Assert.EndsWith("\"DNP\"\n", SchBom.Write(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Commas inside a quoted field are the designators of one line, not column separators.</summary>
    private static int Commas(string row)
    {
        bool quoted = false;
        int inside = 0;
        foreach (char c in row)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && quoted)
            {
                inside++;
            }
        }

        return inside;
    }
}
