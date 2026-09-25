using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The fields table: which parts are lines of it, how they group, what a line says for a field, and what writing a
/// value into a line does to the file.
/// </summary>
public class SchFieldsTableTests
{
    private const string Path0 = "/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10";

    [Theory]
    [InlineData(new[] { "R1", "R2", "R3", "R4", "R7" }, "R1-R4, R7")]
    [InlineData(new[] { "R1", "R2", "R5" }, "R1, R2, R5")]
    [InlineData(new[] { "C1", "R1", "R2", "R3" }, "C1, R1-R3")]
    [InlineData(new[] { "U?", "U?" }, "U?, U?")]
    public void Designators_are_shortened_as_KiCad_shortens_them(string[] references, string shown) =>
        Assert.Equal(shown, SchFieldsTable.Shorthand(references));

    /// <summary>
    /// Grouped as KiCad's default preset groups: the same value, and both placed or both not. The footprint does
    /// not split a line — it shows mixed instead.
    /// </summary>
    [Fact]
    public void Parts_of_one_value_are_one_line_and_a_placed_one_apart_from_one_that_is_not()
    {
        var rows = Rows(Sheet(), group: true);

        var tenK = rows.Single(r => r.References.Contains("R1"));
        Assert.Equal(["R1", "R2"], tenK.References);
        Assert.Null(tenK.Value("Footprint"));
        Assert.Equal("10k", tenK.Value("Value"));

        Assert.Equal(["R3"], rows.Single(r => r.Dnp).References);
    }

    [Fact]
    public void Ungrouped_every_part_is_a_line_but_the_units_of_one_stay_together()
    {
        var rows = Rows(Sheet(), group: false);

        Assert.Equal(["R1", "R2", "R3", "U1"], rows.Select(r => r.Shorthand));
        var gate = rows.Single(r => r.Shorthand == "U1");
        Assert.Equal(2, gate.Symbols.Count);
        Assert.Equal(1, gate.Quantity);
    }

    [Fact]
    public void Power_symbols_and_parts_kept_off_the_bill_are_not_lines_unless_asked()
    {
        var sheet = Sheet();

        Assert.DoesNotContain(SchFieldsTable.Parts(sheet, Path0), s => s.Reference == "#PWR01");
        Assert.DoesNotContain(SchFieldsTable.Parts(sheet, Path0), s => s.Reference == "TP1");
        Assert.Contains(SchFieldsTable.Parts(sheet, Path0, includeExcluded: true), s => s.Reference == "TP1");
        Assert.DoesNotContain(SchFieldsTable.Parts(sheet, Path0, includeExcluded: true), s => s.Reference == "#PWR01");
    }

    [Fact]
    public void Every_field_any_part_carries_is_a_column_after_KiCads_four()
    {
        var columns = SchFieldsTable.Columns(SchFieldsTable.Parts(Sheet(), Path0));

        Assert.Equal(["Reference", "Value", "Datasheet", "Footprint"], columns.Take(4));
        // Spelt as it is first met; the other spelling is the same column, as KiCad finds a field without case.
        Assert.Equal("mpn", Assert.Single(columns, c => string.Equals(c, "MPN", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void A_value_written_into_a_line_goes_to_every_part_of_it()
    {
        var sheet = Sheet();
        var line = Rows(sheet, group: true).Single(r => r.References.Contains("R1"));

        Assert.True(SchFieldsTable.Write(line, "Footprint", "Resistor_SMD:R_0402"));

        Assert.Equal("Resistor_SMD:R_0402", line.Value("Footprint"));
        Assert.All(line.Symbols, s => Assert.Equal("Resistor_SMD:R_0402", s.Footprint));
    }

    /// <summary>
    /// A part without the field gets one, written as this file writes a hidden field — a copy of one the part
    /// already hides — standing at the part. An empty value adds nothing.
    /// </summary>
    [Fact]
    public void A_part_without_the_field_is_given_one_hidden_and_at_the_part()
    {
        var sheet = Sheet();
        var line = Rows(sheet, group: false).Single(r => r.Shorthand == "R2");
        var part = line.Symbols.Single();

        Assert.False(SchFieldsTable.Write(line, "MPN", string.Empty));
        Assert.DoesNotContain(part.Fields, f => f.Name == "MPN");

        Assert.True(SchFieldsTable.Write(line, "MPN", "RC0603FR-0710KL"));

        var field = part.Fields.Single(f => f.Name == "MPN");
        Assert.Equal("RC0603FR-0710KL", field.Value);
        Assert.True(field.IsHidden);
        Assert.Equal(part.Position, field.Position);
        Assert.Equal(90, field.Angle);
    }

    [Fact]
    public void A_designator_is_not_written_from_the_table()
    {
        var line = Rows(Sheet(), group: false).Single(r => r.Shorthand == "R1");

        Assert.False(SchFieldsTable.Write(line, "Reference", "R9"));
        Assert.Equal("R1", line.Symbols[0].Reference);
    }

    /// <summary>On a real sheet written by KiCad 9, a field added from the table reads back as KiCad writes one.</summary>
    [Fact]
    public void On_a_real_sheet_an_added_field_is_written_as_KiCad_writes_one()
    {
        string file = Path.Combine(TestData.KiCadDir, "demos", "cm5_minima", "CM5.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Schematic.Load(file);
        var parts = SchFieldsTable.Parts(sheet, null);
        var line = SchFieldsTable.Rows(parts, null, group: false)[0];
        var part = line.Symbols[0];
        var model = part.Node.Lists().First(l => l.Head == "property" && new SchField(l).IsHidden);

        Assert.True(SchFieldsTable.Write(line, "Anode test", "x"));

        var added = part.Node.Lists().Single(l => l.Head == "property" && new SchField(l).Name == "Anode test");
        Assert.Equal(model.Lists().Select(l => l.Head), added.Lists().Select(l => l.Head));
        Assert.Contains(Schematic.Parse(sheet.Document.ToString()).Symbols, s => s.Fields.Any(f => f.Name == "Anode test" && f.IsHidden));
    }

    private static IReadOnlyList<SchFieldsRow> Rows(Schematic sheet, bool group) =>
        SchFieldsTable.Rows(SchFieldsTable.Parts(sheet, Path0), Path0, group);

    /// <summary>
    /// R1 and R2 of 10k on two footprints, R3 of 10k not placed, a two-unit U1, a power symbol, and a test point
    /// kept off the bill. R2 has no MPN; U1 spells it in capitals, R1 not.
    /// </summary>
    private static Schematic Sheet() => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "\t(lib_symbols\n"
        + "\t\t(symbol \"power:GND\" (power) (property \"Reference\" \"#PWR\" (at 0 0 0)) (symbol \"GND_1_1\"))\n"
        + "\t\t(symbol \"Logic:Gate\" (property \"Reference\" \"U\" (at 0 0 0)) (symbol \"Gate_1_1\") (symbol \"Gate_2_1\")))\n"
        + Part(1, "Device:R", "R1", "10k", "R_0805", 1, extra: "(property \"mpn\" \"A\" (at 0 0 0) (hide yes))")
        + Part(2, "Device:R", "R2", "10k", "R_0603", 1)
        + Part(3, "Device:R", "R3", "10k", "R_0603", 1, flags: "(dnp yes)")
        + Part(4, "Logic:Gate", "U1", "74HC00", "SOIC-14", 1, extra: "(property \"MPN\" \"B\" (at 0 0 0) (hide yes))")
        + Part(5, "Logic:Gate", "U1", "74HC00", "SOIC-14", 2, extra: "(property \"MPN\" \"B\" (at 0 0 0) (hide yes))")
        + Part(6, "power:GND", "#PWR01", "GND", string.Empty, 1)
        + Part(7, "Device:TP", "TP1", "TestPoint", string.Empty, 1, flags: "(in_bom no)")
        + "\t(embedded_fonts no))\n");

    private static string Part(int n, string lib, string reference, string value, string footprint, int unit, string flags = "", string extra = "") =>
        $"\t(symbol (lib_id \"{lib}\") (at {20 * n} 50 0) (unit {unit}) {flags} (uuid \"0a1b2c3d-0000-4000-8000-00000000000{n}\")\n"
        + $"\t\t(property \"Reference\" \"{reference}\" (at {20 * n} 45 90))\n"
        + $"\t\t(property \"Value\" \"{value}\" (at {20 * n} 55 0))\n"
        + $"\t\t(property \"Footprint\" \"{footprint}\" (at {20 * n} 50 0) (hide yes))\n"
        + $"\t\t{extra}\n"
        + $"\t\t(instances (project \"t\" (path \"{Path0}\" (reference \"{reference}\") (unit {unit})))))\n";
}
