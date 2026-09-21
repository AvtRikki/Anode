using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Areas the design rules are told about: a region drawn round part of a sheet so that what is inside it is treated
/// together. The shape lives in a closed outline inside the area rather than on the area itself.
/// </summary>
public class SchRuleAreaTests
{
    private static string WithAreas =>
        Path.Combine(TestData.KiCadDir, "demos", "royalblue54L_feather", "sch", "Debugger.kicad_sch");

    [Fact]
    public void A_sheets_rule_areas_are_read_with_their_outlines()
    {
        Assert.SkipUnless(File.Exists(WithAreas), TestData.SkipReason);

        var sheet = Schematic.Load(WithAreas);

        Assert.Equal(2, sheet.RuleAreas.Count);
        Assert.All(sheet.RuleAreas, area =>
        {
            Assert.NotNull(area.Outline);
            Assert.Equal(SchShapeKind.Polyline, area.Outline!.Kind);
            Assert.True(area.Outline.Points.Length >= 3, "an area needs a shape to be an area");
        });
    }

    [Fact]
    public void An_area_is_one_item_of_the_sheet_and_not_its_outline()
    {
        Assert.SkipUnless(File.Exists(WithAreas), TestData.SkipReason);

        var sheet = Schematic.Load(WithAreas);

        // The outline belongs to the area, so the sheet's own graphics do not carry it: deleting the area would
        // otherwise leave an empty area behind and take a shape nobody could see.
        Assert.DoesNotContain(sheet.Graphics, g => sheet.RuleAreas.Any(a => ReferenceEquals(a.Outline!.Node, g.Node)));
        Assert.All(sheet.RuleAreas, area => Assert.Contains(area, sheet.Items));
    }

    [Fact]
    public void An_area_with_nothing_drawn_in_it_is_read_and_left_alone()
    {
        var sheet = Schematic.Parse(
            "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + "\t(rule_area)\n"
            + "\t(embedded_fonts no))\n");

        var area = Assert.Single(sheet.RuleAreas);
        Assert.Null(area.Outline);
    }

    [Fact]
    public void A_sheet_with_areas_is_kept_byte_for_byte_through_a_round_trip()
    {
        Assert.SkipUnless(File.Exists(WithAreas), TestData.SkipReason);

        byte[] original = File.ReadAllBytes(WithAreas);

        Assert.Equal(original, Schematic.Load(WithAreas).Document.ToBytes());
    }
}
