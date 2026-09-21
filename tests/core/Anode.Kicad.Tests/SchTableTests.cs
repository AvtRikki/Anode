using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Tables on a sheet: cells of text in a grid, and what the table says should be drawn between them. A cell knows
/// its own corner, its size and the space kept clear inside it, which is what puts its words in the right place.
/// </summary>
public class SchTableTests
{
    private static string WithTable =>
        Path.Combine(TestData.KiCadDir, "demos", "jetson-agx-thor-baseboard", "power.kicad_sch");

    [Fact]
    public void A_tables_cells_are_read_with_their_boxes()
    {
        Assert.SkipUnless(File.Exists(WithTable), TestData.SkipReason);

        var table = Assert.Single(Schematic.Load(WithTable).Tables);

        Assert.Equal(4, table.ColumnCount);
        Assert.Equal(44, table.Cells.Count);
        Assert.Equal("Sequence", table.Cells[0].Shown);

        // Every cell is a box of its own, and the first row runs across the top.
        Assert.All(table.Cells, cell => Assert.True(cell.Size.X > 0 && cell.Size.Y > 0));
        Assert.Equal(4, table.Cells.Count(c => c.Position.Y == table.Cells[0].Position.Y));
    }

    [Fact]
    public void A_cell_keeps_its_words_clear_of_its_sides()
    {
        Assert.SkipUnless(File.Exists(WithTable), TestData.SkipReason);

        var cell = Schematic.Load(WithTable).Tables[0].Cells[0];
        var margins = cell.Margins;

        Assert.True(margins.Left > 0 && margins.Top > 0, "a cell with no margins would have its text on the line");
        Assert.True(margins.Left < cell.Size.X / 2, "a margin wider than the cell would leave nowhere to write");
        Assert.Equal(("left", "top"), cell.Alignment);
    }

    [Fact]
    public void What_is_drawn_between_the_cells_is_the_tables_to_say()
    {
        Assert.SkipUnless(File.Exists(WithTable), TestData.SkipReason);

        var table = Schematic.Load(WithTable).Tables[0];

        Assert.True(table.HasBorder);
        Assert.True(table.HasHeaderSeparator);
        Assert.True(table.SeparatesRows);
        Assert.True(table.SeparatesColumns);
    }

    [Fact]
    public void A_table_that_says_nothing_about_its_lines_draws_none_of_them()
    {
        var sheet = Schematic.Parse(
            "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + "\t(table (column_count 1) (cells (table_cell \"only\" (at 10 10 0) (size 20 5)\n"
            + "\t\t(effects (font (size 1.27 1.27)) (justify left top)) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\"))))\n"
            + "\t(embedded_fonts no))\n");

        var table = Assert.Single(sheet.Tables);

        Assert.False(table.HasBorder);
        Assert.False(table.SeparatesRows);
        Assert.False(table.SeparatesColumns);
        Assert.Equal(default, table.Cells[0].Margins);
    }

    [Fact]
    public void A_sheet_with_a_table_is_kept_byte_for_byte_through_a_round_trip()
    {
        Assert.SkipUnless(File.Exists(WithTable), TestData.SkipReason);

        Assert.Equal(File.ReadAllBytes(WithTable), Schematic.Load(WithTable).Document.ToBytes());
    }
}
