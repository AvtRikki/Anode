using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The second way a part can be drawn — KiCad's De Morgan alternative. Only the drawing changes: the part is the
/// same part, with the same designator, and its pins are on the same nets.
/// </summary>
public class BodyStyleTests
{
    private static string Sockets => Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_sockets.kicad_sch");

    [Fact]
    public void A_definition_says_whether_it_carries_a_second_body()
    {
        Assert.SkipUnless(File.Exists(Sockets), TestData.SkipReason);

        var sheet = Schematic.Load(Sockets);
        var definitions = sheet.LibrarySymbols;

        // The microcontroller in this demo is drawn two ways; the passives are drawn one.
        var twoWays = definitions.Single(d => d.Value.Name.EndsWith("PIC16F54", StringComparison.Ordinal)).Value;
        Assert.True(twoWays.HasAlternateBody);
        Assert.NotEmpty(twoWays.PinsOf(1, 2));

        var oneWay = definitions.First(d => d.Value.Name.EndsWith(":C", StringComparison.Ordinal)).Value;
        Assert.False(oneWay.HasAlternateBody);
        Assert.Empty(oneWay.PinsOf(1, 2));
    }

    [Fact]
    public void The_two_bodies_are_drawn_from_different_parts()
    {
        Assert.SkipUnless(File.Exists(Sockets), TestData.SkipReason);

        var definition = Schematic.Load(Sockets).LibrarySymbols
            .Single(d => d.Value.Name.EndsWith("PIC16F54", StringComparison.Ordinal)).Value;

        var ordinary = definition.PinsOf(1, 1).ToList();
        var alternate = definition.PinsOf(1, 2).ToList();

        Assert.NotEmpty(ordinary);
        Assert.NotEmpty(alternate);

        // Each way is drawn from bodies of its own — here the pins, since this part's outline is common to both.
        Assert.Contains(ordinary, p => !alternate.Contains(p));
        Assert.Contains(alternate, p => !ordinary.Contains(p));

        // The outline of this part lives in "PIC16F54_0_1": common to every section, but still the ordinary style's
        // own. Unit 0 means all sections, not both ways of drawing — so the alternative here is its pins alone.
        Assert.NotEmpty(definition.GraphicsOf(1, 1));
        Assert.Empty(definition.GraphicsOf(1, 2));
    }

    [Fact]
    public void Switching_a_placement_changes_which_body_it_is_drawn_from()
    {
        var sheet = Sheet("(body_style 1)");
        var symbol = sheet.Symbols.Single();

        Assert.Equal(1, symbol.BodyStyle);

        SchWrites.SetBodyStyle(symbol, 2);

        Assert.Equal(2, symbol.BodyStyle);
        Assert.Contains("(body_style 2)", Written(sheet), StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_file_keeps_its_own_word_for_it()
    {
        var sheet = Sheet("(convert 1)");

        SchWrites.SetBodyStyle(sheet.Symbols.Single(), 2);

        string written = Written(sheet);

        // A file that says "convert" goes on saying it; saying both would make it contradict itself.
        Assert.Contains("(convert 2)", written, StringComparison.Ordinal);
        Assert.DoesNotContain("body_style", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_says_nothing_is_left_alone_until_there_is_something_to_say()
    {
        var sheet = Sheet(null);
        byte[] original = sheet.Document.ToBytes();
        var symbol = sheet.Symbols.Single();

        // It is already drawn the ordinary way, so saying so writes nothing.
        SchWrites.SetBodyStyle(symbol, 1);
        Assert.Equal(original, sheet.Document.ToBytes());

        SchWrites.SetBodyStyle(symbol, 2);
        Assert.Equal(2, symbol.BodyStyle);
        Assert.Contains("(body_style 2)", Written(sheet), StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_and_undoing_gives_the_file_back()
    {
        var sheet = Sheet("(body_style 1)");
        byte[] original = sheet.Document.ToBytes();
        var symbol = sheet.Symbols.Single();

        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand("Body style", [symbol], () => SchWrites.SetBodyStyle(symbol, 2)));
        Assert.Equal(2, symbol.BodyStyle);

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Equal(1, symbol.BodyStyle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void There_are_only_two_ways_to_draw_a_part(int style)
    {
        var symbol = Sheet("(body_style 1)").Symbols.Single();

        Assert.Throws<ArgumentOutOfRangeException>(() => SchWrites.SetBodyStyle(symbol, style));
    }

    private static string Written(Schematic sheet) => System.Text.Encoding.UTF8.GetString(sheet.Document.ToBytes());

    private static Schematic Sheet(string? style) => Schematic.Parse(
        "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "\t(symbol (lib_id \"Logic:74LS00\") (at 50.8 44.45 0) (unit 1)"
        + (style is null ? string.Empty : " " + style)
        + " (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")\n"
        + "\t\t(property \"Reference\" \"U1\" (at 50.8 44.45 0)))\n"
        + "\t(embedded_fonts no))\n");
}
