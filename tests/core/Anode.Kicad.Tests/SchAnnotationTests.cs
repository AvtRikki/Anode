using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Giving parts their numbers. The rule that matters most is the one about restraint: a part that already carries a
/// number keeps it, because renumbering a design behind the designer's back would be worse than not numbering at all.
/// </summary>
public class SchAnnotationTests
{
    private static Schematic Sheet(params string[] references) => Schematic.Parse(
        "(kicad_sch\n\t(version 20260206)\n\t(generator \"anode\")\n"
        + "\t(uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\")\n\t(paper \"A4\")\n\t(lib_symbols)\n"
        + string.Concat(references.Select((r, i) =>
            $"\t(symbol\n\t\t(lib_id \"Device:R\")\n\t\t(at 50.8 {44.45 + (i * 2.54)} 0)\n\t\t(unit 1)\n"
            + $"\t\t(uuid \"0a1b2c3d-0000-4000-8000-00000000000{i}\")\n"
            + $"\t\t(property \"Reference\" \"{r}\"\n\t\t\t(at 50.8 44.45 0)\n\t\t)\n"
            + "\t\t(instances\n\t\t\t(project \"p\"\n\t\t\t\t(path \"/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\"\n"
            + $"\t\t\t\t\t(reference \"{r}\")\n\t\t\t\t\t(unit 1)\n\t\t\t\t)\n\t\t\t)\n\t\t)\n\t)\n"))
        + "\t(sheet_instances\n\t\t(path \"/\"\n\t\t\t(page \"1\")\n\t\t)\n\t)\n\t(embedded_fonts no)\n)");

    [Theory]
    [InlineData("R?", true)]
    [InlineData("", true)]
    [InlineData("R1", false)]
    [InlineData("U12", false)]
    public void A_designator_says_whether_it_is_waiting_for_a_number(string reference, bool waiting) =>
        Assert.Equal(waiting, SchAnnotation.IsUnannotated(reference));

    [Theory]
    [InlineData("R?", "R")]
    [InlineData("R12", "R")]
    [InlineData("U", "U")]
    [InlineData("", "U")]
    public void The_prefix_is_the_letters_without_the_number(string reference, string prefix) =>
        Assert.Equal(prefix, SchAnnotation.PrefixOf(reference));

    [Fact]
    public void The_next_number_is_the_first_one_nobody_has()
    {
        var sheet = Sheet("R1", "R7", "C1");

        // KiCad fills the gap rather than stepping past it: R2, not R8.
        Assert.Equal(2, SchAnnotation.NextNumber(sheet, "R"));
        Assert.Equal(2, SchAnnotation.NextNumber(sheet, "C"));
        Assert.Equal(1, SchAnnotation.NextNumber(sheet, "U"));
    }

    [Fact]
    public void Only_the_parts_waiting_for_a_number_are_given_one()
    {
        var sheet = Sheet("R1", "R?", "R?", "C?");

        var given = SchAnnotation.Annotate(sheet, sheet.Symbols);

        Assert.Equal(["R2", "R3", "C1"], given.Select(g => g.Reference));

        // The one that already had a number is not in the list at all.
        Assert.DoesNotContain(given, g => g.Symbol.Reference == "R1");
    }

    [Fact]
    public void What_is_given_is_written_in_both_places()
    {
        var sheet = Sheet("R?");
        var symbol = sheet.Symbols.Single();

        SchWrites.SetReference(symbol, "R4");

        Assert.Equal("R4", symbol.Reference);

        // The instance block is what the rest of the project reads; leaving it behind makes the file contradict itself.
        string written = System.Text.Encoding.UTF8.GetString(sheet.Document.ToBytes());
        Assert.Contains("(reference \"R4\")", written, StringComparison.Ordinal);
        Assert.DoesNotContain("R?", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotating_and_undoing_gives_the_file_back()
    {
        var sheet = Sheet("R?", "R?");
        byte[] original = sheet.Document.ToBytes();

        var given = SchAnnotation.Annotate(sheet, sheet.Symbols);
        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand("Annotate", [.. given.Select(g => g.Symbol)], () =>
        {
            foreach (var (symbol, reference) in given)
            {
                SchWrites.SetReference(symbol, reference);
            }
        }));

        Assert.Equal(["R1", "R2"], sheet.Symbols.Select(s => s.Reference));

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void Renumbering_gives_the_sheet_its_numbers_again_from_one()
    {
        var sheet = Sheet("R7", "R3", "R?");

        var given = SchAnnotation.Renumber(sheet.Symbols, null, DesignNumbers.Of(sheet));

        // Parts are numbered left to right, and the numbers start from one again rather than past what was there.
        Assert.Equal(["R1", "R2", "R3"], given.Select(g => g.Reference));
        Assert.Equal(["R7", "R3", "R?"], given.Select(g => g.Symbol.Reference));
    }

    [Fact]
    public void Renumbering_runs_left_to_right()
    {
        // Three parts standing out of order across the sheet: the rightmost carries the lowest number to begin with.
        var sheet = Schematic.Parse(
            "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + Placed("R1", 150) + Placed("R2", 50) + Placed("R3", 100)
            + "\t(embedded_fonts no))\n");

        var given = SchAnnotation.Renumber(sheet.Symbols, null, DesignNumbers.Of(sheet));

        // KiCad numbers within a prefix by position, left to right (SORT_BY_X_POSITION, its default).
        Assert.Equal([("R2", "R1"), ("R3", "R2"), ("R1", "R3")], given.Select(g => (g.Symbol.Reference, g.Reference)));

        static string Placed(string reference, int x) =>
            $"\t(symbol (lib_id \"Device:R\") (at {x} 50 0) (unit 1)"
            + $" (uuid \"0a1b2c3d-0000-4000-8000-0000000000{x}\")\n"
            + $"\t\t(property \"Reference\" \"{reference}\" (at {x} 50 0)))\n";
    }

    [Fact]
    public void Renumbering_one_sheet_leaves_the_rest_of_the_design_its_numbers()
    {
        var sheet = Sheet("R7", "R3");

        // Another sheet of the design already holds R1, so this one starts at R2.
        var taken = DesignNumbers.Of(sheet);
        taken.Take("R", 1);

        Assert.Equal(["R2", "R3"], SchAnnotation.Renumber(sheet.Symbols, null, taken).Select(g => g.Reference));
    }

    [Fact]
    public void The_sections_of_one_part_go_on_sharing_a_designator()
    {
        // Two placements of one part, as a two-gate chip is drawn, and a third part beside them.
        var sheet = Sheet("U5", "U5", "U2");

        var given = SchAnnotation.Renumber(sheet.Symbols, null, DesignNumbers.Of(sheet));

        var byPart = given
            .GroupBy(g => g.Symbol.Reference ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Reference).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        Assert.Equal(["U1"], byPart["U5"]);
        Assert.Equal(["U2"], byPart["U2"]);
    }

    [Fact]
    public void The_sheet_can_say_what_is_still_waiting()
    {
        var sheet = Sheet("R1", "R?", "C?");

        Assert.Equal(2, SchAnnotation.Unannotated(sheet).Count);
    }
}
