using System.Text;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Editing the title block: in place when it is there, created where KiCad writes it when it is not, fields in
/// KiCad's order, nothing left behind when emptied — and an undo that gives the file back byte for byte either way.
/// </summary>
public class TitleBlockTests
{
    private const string Bare = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    private static string Text(Schematic sheet) => Encoding.UTF8.GetString(sheet.Document.ToBytes());

    private static void Edit(UndoStack history, Schematic sheet, string field, string value) =>
        history.Execute(new RootChildCommand(sheet.Root, "title_block", "Title block", () => TitleBlockWrites.Set(sheet.Root, field, value)));

    [Fact]
    public void A_sheet_without_one_gets_a_title_block_where_KiCad_writes_it()
    {
        var sheet = Schematic.Parse(Bare);
        byte[] original = sheet.Document.ToBytes();
        var history = new UndoStack();

        Edit(history, sheet, "company", "Anode");

        Assert.Equal("Anode", sheet.TitleBlock.Company);
        Assert.Contains("\t(paper \"A4\")\n\t(title_block\n\t\t(company \"Anode\")\n\t)\n\t(lib_symbols)", Text(sheet), StringComparison.Ordinal);

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Null(sheet.TitleBlock.Company);
    }

    [Fact]
    public void Fields_go_in_KiCads_order_whichever_is_written_first()
    {
        var sheet = Schematic.Parse(Bare);

        TitleBlockWrites.Set(sheet.Root, "company", "Anode");
        TitleBlockWrites.Set(sheet.Root, "rev", "B");
        TitleBlockWrites.Set(sheet.Root, "title", "Power");

        string text = Text(sheet);
        int title = text.IndexOf("(title ", StringComparison.Ordinal);
        int rev = text.IndexOf("(rev ", StringComparison.Ordinal);
        int company = text.IndexOf("(company ", StringComparison.Ordinal);
        Assert.True(title < rev && rev < company, text);

        // And what was written reads back as the same tree.
        Assert.Equal(text, Text(Schematic.Parse(text)));
    }

    [Fact]
    public void Emptying_every_field_leaves_no_block_behind()
    {
        var sheet = Schematic.Parse(Bare);
        TitleBlockWrites.Set(sheet.Root, "title", "Power");
        TitleBlockWrites.Set(sheet.Root, "date", "2026-09-18");

        TitleBlockWrites.Set(sheet.Root, "title", string.Empty);
        Assert.Contains("(date ", Text(sheet), StringComparison.Ordinal);

        TitleBlockWrites.Set(sheet.Root, "date", string.Empty);
        Assert.DoesNotContain("title_block", Text(sheet), StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_title_block_is_rewritten_in_place_and_undone_exactly()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        byte[] original = sheet.Document.ToBytes();
        var history = new UndoStack();

        Edit(history, sheet, "rev", "3");
        Edit(history, sheet, "company", string.Empty);

        Assert.Equal("3", sheet.TitleBlock.Revision);
        Assert.Null(sheet.TitleBlock.Company);

        history.Undo();
        history.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());

        history.Redo();
        Assert.Equal("3", sheet.TitleBlock.Revision);
    }
}
