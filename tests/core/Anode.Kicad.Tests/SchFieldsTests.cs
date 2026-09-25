using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Writing one field of many parts, as a line of the bill does: every part gets the value, a part without the field
/// gets one as KiCad adds it, and what the bill works out is never written.
/// </summary>
public class SchFieldsTests
{
    [Fact]
    public void A_value_goes_to_every_part()
    {
        var sheet = Sheet();

        Assert.True(SchFields.Write(sheet.Symbols, "Footprint", "Resistor_SMD:R_0402"));

        Assert.All(sheet.Symbols, s => Assert.Equal("Resistor_SMD:R_0402", s.Footprint));
        Assert.False(SchFields.Write(sheet.Symbols, "Footprint", "Resistor_SMD:R_0402"));
    }

    /// <summary>
    /// A part without the field gets one, written as this file writes a hidden field — a copy of one the part
    /// already hides — standing at the part and turned as its designator. An empty value adds nothing.
    /// </summary>
    [Fact]
    public void A_part_without_the_field_is_given_one_hidden_and_at_the_part()
    {
        var part = Sheet().Symbols[0];

        Assert.False(SchFields.Write([part], "MPN", string.Empty));
        Assert.DoesNotContain(part.Fields, f => f.Name == "MPN");

        Assert.True(SchFields.Write([part], "MPN", "RC0603FR-0710KL"));

        var field = part.Fields.Single(f => f.Name == "MPN");
        Assert.Equal("RC0603FR-0710KL", field.Value);
        Assert.True(field.IsHidden);
        Assert.Equal(part.Position, field.Position);
        Assert.Equal(90, field.Angle);
    }

    [Fact]
    public void A_field_is_found_without_regard_to_case()
    {
        var part = Sheet().Symbols[1];

        Assert.True(SchFields.Write([part], "mpn", "B2"));

        Assert.Equal("B2", Assert.Single(part.Fields, f => f.Name == "MPN").Value);
    }

    [Theory]
    [InlineData("Reference")]
    [InlineData("${QUANTITY}")]
    [InlineData("${DNP}")]
    public void What_the_bill_works_out_is_not_written(string field)
    {
        var sheet = Sheet();
        byte[] before = sheet.Document.ToBytes();

        Assert.False(SchFields.Write(sheet.Symbols, field, "R9"));
        Assert.Equal(before, sheet.Document.ToBytes());
    }

    /// <summary>On a real sheet written by KiCad 9, a field added from the bill reads back as KiCad writes one.</summary>
    [Fact]
    public void On_a_real_sheet_an_added_field_is_written_as_KiCad_writes_one()
    {
        string file = Path.Combine(TestData.KiCadDir, "demos", "cm5_minima", "CM5.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Schematic.Load(file);
        var part = sheet.Symbols.First(s => s.Fields.Any(f => f.IsHidden));
        var model = part.Node.Lists().First(l => l.Head == "property" && new SchField(l).IsHidden);

        Assert.True(SchFields.Write([part], "Anode test", "x"));

        var added = part.Node.Lists().Single(l => l.Head == "property" && new SchField(l).Name == "Anode test");
        Assert.Equal(model.Lists().Select(l => l.Head), added.Lists().Select(l => l.Head));
        Assert.Contains(Schematic.Parse(sheet.Document.ToString()).Symbols, s => s.Fields.Any(f => f.Name == "Anode test" && f.IsHidden));
    }

    private static Schematic Sheet() => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "\t(symbol (lib_id \"Device:R\") (at 20 50 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")\n"
        + "\t\t(property \"Reference\" \"R1\" (at 20 45 90)) (property \"Value\" \"10k\" (at 20 55 0))"
        + " (property \"Footprint\" \"R_0603\" (at 20 50 0) (hide yes)))\n"
        + "\t(symbol (lib_id \"Device:R\") (at 40 50 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000002\")\n"
        + "\t\t(property \"Reference\" \"R2\" (at 40 45 90)) (property \"Value\" \"10k\" (at 40 55 0))"
        + " (property \"Footprint\" \"R_0805\" (at 40 50 0) (hide yes)) (property \"MPN\" \"A\" (at 40 50 0) (hide yes)))\n"
        + "\t(embedded_fonts no))\n");
}
