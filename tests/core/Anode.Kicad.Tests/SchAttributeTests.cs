using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// The words a part carries about what it is left out of, and writing them back. KiCad keeps them in two minds — a
/// part <em>is</em> in the bill and on the board, but <em>is excluded</em> from simulation — so reading one the
/// wrong way round quietly reverses what the designer asked for.
/// </summary>
public class SchAttributeTests
{
    [Fact]
    public void A_part_that_says_nothing_is_in_everything()
    {
        var symbol = Placed(string.Empty);

        Assert.True(symbol.InBom);
        Assert.True(symbol.OnBoard);
        Assert.False(symbol.IsDnp);
        Assert.False(symbol.ExcludedFromSim);
    }

    [Theory]
    [InlineData("(in_bom no)", false, true, false)]
    [InlineData("(on_board no)", true, false, false)]
    [InlineData("(exclude_from_sim yes)", true, true, true)]
    public void Each_word_is_read_the_way_KiCad_writes_it(string written, bool inBom, bool onBoard, bool outOfSim)
    {
        var symbol = Placed(written);

        Assert.Equal(inBom, symbol.InBom);
        Assert.Equal(onBoard, symbol.OnBoard);
        Assert.Equal(outOfSim, symbol.ExcludedFromSim);
    }

    [Fact]
    public void Writing_a_word_that_is_already_there_changes_it_in_place()
    {
        var sheet = Sheet("(in_bom yes)");
        var symbol = sheet.Symbols.Single();

        SchWrites.SetFlag(symbol, "in_bom", false);

        Assert.False(symbol.InBom);
        Assert.Contains("(in_bom no)", Written(sheet), StringComparison.Ordinal);
        Assert.DoesNotContain("(in_bom yes)", Written(sheet), StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_that_was_not_there_is_put_where_KiCad_would_have_put_it()
    {
        var symbol = Placed("(unit 1)");

        SchWrites.SetFlag(symbol, "dnp", true);
        SchWrites.SetFlag(symbol, "exclude_from_sim", true);

        // The part's own words, not the sheet's: the sheet carries a uuid of its own before any of these.
        string text = symbol.Node.ToString()!;

        // KiCad's order: unit, body_style, exclude_from_sim, in_bom, on_board, in_pos_files, dnp, then the uuid.
        Assert.True(text.IndexOf("(exclude_from_sim", StringComparison.Ordinal) < text.IndexOf("(dnp", StringComparison.Ordinal));
        Assert.True(text.IndexOf("(dnp", StringComparison.Ordinal) < text.IndexOf("(uuid", StringComparison.Ordinal));
        Assert.True(text.IndexOf("(unit", StringComparison.Ordinal) < text.IndexOf("(exclude_from_sim", StringComparison.Ordinal));
    }

    [Fact]
    public void Writing_a_word_and_undoing_gives_the_file_back()
    {
        var sheet = Sheet("(in_bom yes)");
        var symbol = sheet.Symbols.Single();
        byte[] original = sheet.Document.ToBytes();

        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand("Bill", [symbol], () => SchWrites.SetFlag(symbol, "in_bom", false)));
        Assert.False(symbol.InBom);

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.True(symbol.InBom);
    }

    [Fact]
    public void A_bare_word_of_an_older_file_means_yes()
    {
        // Older files write "(dnp)" on its own where a newer one writes "(dnp yes)".
        Assert.True(Placed("(dnp)").IsDnp);
    }

    private static string Written(Schematic sheet) =>
        System.Text.Encoding.UTF8.GetString(sheet.Document.ToBytes());

    private static SymbolInstance Placed(string words) => Sheet(words).Symbols.Single();

    private static Schematic Sheet(string words) => Schematic.Parse(
        "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + $"\t(symbol (lib_id \"Device:R\") (at 50.8 44.45 0) {words}"
        + " (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")\n"
        + "\t\t(property \"Reference\" \"R1\" (at 50.8 44.45 0)))\n"
        + "\t(embedded_fonts no))\n");
}
