using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Find and replace on a sheet, by KiCad's rules: what is looked in, how words are compared, what may be written,
/// and the order the places come in.
/// </summary>
public class SchFindTests
{
    [Theory]
    [InlineData(SchFindMode.Plain, "10k", false, true)]
    [InlineData(SchFindMode.Plain, "10k", true, false)]
    [InlineData(SchFindMode.Plain, "0K", false, true)]
    [InlineData(SchFindMode.WholeWord, "0K", false, false)]
    [InlineData(SchFindMode.WholeWord, "10K", false, true)]
    [InlineData(SchFindMode.Wildcard, "1?K", false, true)]
    [InlineData(SchFindMode.Wildcard, "1*", false, true)]
    [InlineData(SchFindMode.Wildcard, "0*", false, false)]
    [InlineData(SchFindMode.Regex, "^1\\dK$", false, true)]
    [InlineData(SchFindMode.Regex, "^1\\dk$", true, false)]
    public void Words_are_compared_as_KiCad_compares_them(SchFindMode mode, string text, bool matchCase, bool found)
    {
        var matcher = new SchTextMatcher(new SchFindOptions(text) { Mode = mode, MatchCase = matchCase });

        Assert.Equal(found, matcher.Matches("10K"));
    }

    /// <summary>A wildcard is the whole text against the pattern, and a broken expression finds nothing rather than failing.</summary>
    [Fact]
    public void A_broken_expression_finds_nothing()
    {
        var matcher = new SchTextMatcher(new SchFindOptions("(") { Mode = SchFindMode.Regex });

        Assert.False(matcher.Matches("("));
        Assert.False(matcher.Replace("(", "x", out _));
    }

    [Theory]
    [InlineData(SchFindMode.Plain, "net", "NET_A net_b", "BUS_A BUS_b")]
    [InlineData(SchFindMode.WholeWord, "a", "a ab a_b a", "X ab a_b X")]
    [InlineData(SchFindMode.Regex, "(\\w+)_(\\w+)", "SDA_1", "1_SDA")]
    public void Every_occurrence_is_replaced(SchFindMode mode, string text, string before, string after)
    {
        var matcher = new SchTextMatcher(new SchFindOptions(text) { Mode = mode });
        string with = mode switch { SchFindMode.Regex => "\\2_\\1", SchFindMode.WholeWord => "X", _ => "BUS" };

        Assert.True(matcher.Replace(before, with, out string result));
        Assert.Equal(after, result);
    }

    /// <summary>KiCad's regular expressions write the whole match as <c>&amp;</c>; a dollar is only a dollar.</summary>
    [Fact]
    public void A_replacement_is_read_as_KiCad_reads_it()
    {
        var matcher = new SchTextMatcher(new SchFindOptions("R\\d") { Mode = SchFindMode.Regex });

        Assert.True(matcher.Replace("R1", "[&]$", out string result));
        Assert.Equal("[R1]$", result);
    }

    [Fact]
    public void Hidden_fields_are_looked_in_only_when_asked()
    {
        var sheet = Sheet();

        Assert.DoesNotContain(SchFind.All(sheet, new SchFindOptions("Resistor_SMD")), h => h.Name == "Footprint");
        Assert.Contains(SchFind.All(sheet, new SchFindOptions("Resistor_SMD") { HiddenFields = true }), h => h.Name == "Footprint");
    }

    [Fact]
    public void Pins_are_looked_in_only_when_asked()
    {
        var sheet = Sheet();

        Assert.Empty(SchFind.All(sheet, new SchFindOptions("CLKIN")));
        var hit = Assert.Single(SchFind.All(sheet, new SchFindOptions("CLKIN") { Pins = true }));
        Assert.Equal(SchFindPlace.Pin, hit.Place);
        Assert.IsType<SymbolInstance>(hit.Item);
    }

    /// <summary>Left to right, then top to bottom, as KiCad walks a sheet.</summary>
    [Fact]
    public void Places_come_left_to_right_then_top_to_bottom()
    {
        var hits = SchFind.All(Sheet(), new SchFindOptions("SDA"));

        Assert.Equal(3, hits.Count);
        Assert.Equal(hits.OrderBy(h => h.Position.X).ThenBy(h => h.Position.Y).Select(h => h.Position), hits.Select(h => h.Position));
    }

    /// <summary>A part of several units is found by the name it is shown with: the designator and its gate's letter.</summary>
    [Fact]
    public void One_gate_of_a_package_is_found_by_its_letter()
    {
        var sheet = Sheet();

        var hit = Assert.Single(SchFind.All(sheet, new SchFindOptions("U1B") { Mode = SchFindMode.WholeWord }));
        Assert.Equal(SchFindPlace.Reference, hit.Place);
        Assert.Empty(SchFind.All(sheet, new SchFindOptions("U1A") { Mode = SchFindMode.WholeWord }));
    }

    [Fact]
    public void Units_are_lettered_as_KiCad_letters_them()
    {
        Assert.Equal("A", SchFind.UnitLetters(1));
        Assert.Equal("Z", SchFind.UnitLetters(26));
        Assert.Equal("AA", SchFind.UnitLetters(27));
    }

    [Fact]
    public void A_value_is_replaced_where_it_was_found_and_nowhere_else()
    {
        var sheet = Sheet();
        var options = new SchFindOptions("10k");

        var hit = Assert.Single(SchFind.All(sheet, options));
        Assert.True(SchFind.Replace(hit, options, "4k7"));

        var resistor = sheet.Symbols.Single(s => s.Reference == "R1");
        Assert.Equal("4k7", resistor.Value);
        Assert.Contains("(property \"Value\" \"4k7\"", sheet.Document.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Designators are replaced only when asked. A replace of "R" meant for values would otherwise rename every
    /// resistor on the sheet; when it is asked for, both places KiCad keeps the designator are written.
    /// </summary>
    [Fact]
    public void Designators_are_replaced_only_when_asked()
    {
        var sheet = Sheet();
        var hit = SchFind.All(sheet, new SchFindOptions("R1") { Mode = SchFindMode.WholeWord }).Single(h => h.Place == SchFindPlace.Reference);

        Assert.False(SchFind.Replace(hit, new SchFindOptions("R1") { Mode = SchFindMode.WholeWord }, "R9"));
        Assert.Equal("R1", ((SymbolInstance)hit.Item).Reference);

        var asked = new SchFindOptions("R1") { Mode = SchFindMode.WholeWord, ReplaceReferences = true };
        Assert.True(SchFind.Replace(hit, asked, "R9", "/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10"));

        var resistor = (SymbolInstance)hit.Item;
        Assert.Equal("R9", resistor.Reference);
        Assert.Equal("R9", resistor.ReferenceAt("/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10"));
    }

    [Fact]
    public void A_sheets_file_name_and_a_pin_are_not_written()
    {
        var sheet = Sheet();
        var options = new SchFindOptions("amp") { Pins = true };

        var file = SchFind.All(sheet, options).Single(h => h.Name == "Sheetfile");
        Assert.False(SchFind.Replace(file, options, "filter"));
        Assert.Equal("amp.kicad_sch", sheet.Sheets.Single().SheetFile);

        var pin = SchFind.All(sheet, new SchFindOptions("CLKIN") { Pins = true }).Single();
        Assert.False(SchFind.Replace(pin, new SchFindOptions("CLKIN") { Pins = true }, "X"));
    }

    [Fact]
    public void A_held_item_is_found_but_not_written()
    {
        var sheet = Sheet();
        var options = new SchFindOptions("keep");
        var hit = Assert.Single(SchFind.All(sheet, options));

        Assert.False(SchFind.Replace(hit, options, "drop"));
    }

    [Fact]
    public void Within_a_selection_only_the_selection_is_looked_in()
    {
        var sheet = Sheet();
        var label = sheet.Labels.First(l => l.Shown == "SDA");

        var hit = Assert.Single(SchFind.All(sheet, new SchFindOptions("SDA"), within: [label]));
        Assert.Same(label, hit.Item);
    }

    /// <summary>
    /// A label's slash is the hierarchy's, so KiCad writes it <c>{slash}</c> — but older files carry it bare, and the
    /// demo has one of each. Both are found by the name they are shown with, and both are renamed.
    /// </summary>
    [Fact]
    public void Both_spellings_of_a_slashed_label_are_found_and_renamed()
    {
        string file = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);

        var sheet = Schematic.Load(file);
        var options = new SchFindOptions("VPP/MCLR") { Mode = SchFindMode.WholeWord };

        var hits = SchFind.All(sheet, options).Where(h => h.Place == SchFindPlace.Label).ToList();
        Assert.Equal(2, hits.Count);

        foreach (var hit in hits)
        {
            Assert.True(SchFind.Replace(hit, options, "VPP-MCLR/2"));
        }

        Assert.Equal(2, sheet.Labels.Count(l => l.Text == "VPP-MCLR{slash}2"));
        Assert.DoesNotContain(SchFind.All(sheet, options), h => h.Place == SchFindPlace.Label);
    }

    /// <summary>
    /// A resistor, a two-gate part placed as its second gate, three labels, a note that is held, and a child sheet.
    /// </summary>
    private static Schematic Sheet() => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "\t(lib_symbols\n"
        + "\t\t(symbol \"Device:R\" (property \"Reference\" \"R\" (at 0 0 0))\n"
        + "\t\t\t(symbol \"R_1_1\"\n"
        + "\t\t\t\t(pin passive line (at 0 3.81 270) (length 1.27) (name \"~\") (number \"1\"))\n"
        + "\t\t\t\t(pin passive line (at 0 -3.81 90) (length 1.27) (name \"~\") (number \"2\"))))\n"
        + "\t\t(symbol \"Logic:Gate\" (property \"Reference\" \"U\" (at 0 0 0))\n"
        + "\t\t\t(symbol \"Gate_1_1\" (pin input line (at -5.08 0 0) (length 2.54) (name \"IN\") (number \"1\")))\n"
        + "\t\t\t(symbol \"Gate_2_1\" (pin input line (at -5.08 0 0) (length 2.54) (name \"CLKIN\") (number \"3\")))))\n"
        + "\t(symbol (lib_id \"Device:R\") (at 50.8 50.8 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")\n"
        + "\t\t(property \"Reference\" \"R1\" (at 53 50 0))\n"
        + "\t\t(property \"Value\" \"10k\" (at 53 52 0))\n"
        + "\t\t(property \"Footprint\" \"Resistor_SMD:R_0603\" (at 53 54 0) (hide yes))\n"
        + "\t\t(instances (project \"t\" (path \"/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\" (reference \"R1\") (unit 1)))))\n"
        + "\t(symbol (lib_id \"Logic:Gate\") (at 80 50.8 0) (unit 2) (uuid \"0a1b2c3d-0000-4000-8000-000000000002\")\n"
        + "\t\t(property \"Reference\" \"U1\" (at 80 46 0))\n"
        + "\t\t(property \"Value\" \"74HC00\" (at 80 56 0))\n"
        + "\t\t(instances (project \"t\" (path \"/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\" (reference \"U1\") (unit 2)))))\n"
        + "\t(label \"SDA\" (at 30 40 0) (effects (font (size 1.27 1.27))) (uuid \"1a1b2c3d-0000-4000-8000-000000000001\"))\n"
        + "\t(label \"SDA\" (at 20 60 0) (effects (font (size 1.27 1.27))) (uuid \"1a1b2c3d-0000-4000-8000-000000000002\"))\n"
        + "\t(global_label \"SDA\" (shape input) (at 30 20 0) (effects (font (size 1.27 1.27))) (uuid \"1a1b2c3d-0000-4000-8000-000000000003\"))\n"
        + "\t(text \"keep this\" (locked yes) (at 100 100 0) (effects (font (size 1.27 1.27))) (uuid \"2a1b2c3d-0000-4000-8000-000000000001\"))\n"
        + "\t(sheet (at 120 30) (size 20 20) (uuid \"3a1b2c3d-0000-4000-8000-000000000001\")\n"
        + "\t\t(property \"Sheetname\" \"Amplifier\" (at 120 29 0))\n"
        + "\t\t(property \"Sheetfile\" \"amp.kicad_sch\" (at 120 51 0)))\n"
        + "\t(embedded_fonts no))\n");
}
