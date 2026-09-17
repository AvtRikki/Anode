using System.Text;
using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Putting a part on a sheet. Two things have to happen at once — the instance that says where it sits, and a copy
/// of the definition so the sheet can still be drawn by someone without the library — and the copy is renamed as it
/// crosses over, because a library calls the symbol "R" where a sheet calls the same definition "Device:R".
/// </summary>
public class SchSymbolsTests
{
    private const string Sheet = """
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

    private const string Library = """
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "anode")
        	(symbol "R"
        		(property "Reference" "R"
        			(at 2.032 0 90)
        		)
        		(property "Value" "R"
        			(at 0 0 90)
        		)
        		(symbol "R_1_1"
        			(pin passive line
        				(at 0 3.81 270)
        				(length 1.27)
        				(name "~")
        				(number "1")
        			)
        			(pin passive line
        				(at 0 -3.81 90)
        				(length 1.27)
        				(name "~")
        				(number "2")
        			)
        		)
        	)
        )
        """;

    private static readonly Vector2L At = new(50_800_000, 44_450_000);

    private static LibSymbol Definition() => SymbolLibrary.Parse(Library).Find("R")!;

    private static string Written(Schematic sheet) => Encoding.UTF8.GetString(sheet.Document.ToBytes());

    private static SymbolInstance Place(Schematic sheet, string reference = "R1") =>
        SchSymbols.Place(sheet, "Device:R", Definition(), At, reference, "project", SchSymbols.PathOf(sheet));

    [Fact]
    public void A_placed_symbol_says_what_it_is_and_where_it_stands()
    {
        var sheet = Schematic.Parse(Sheet);
        var symbol = Place(sheet);

        Assert.Equal("Device:R", symbol.LibId);
        Assert.Equal(At, symbol.Position);
        Assert.Equal(1, symbol.Unit);
        Assert.False(string.IsNullOrEmpty(symbol.Uuid));

        // It arrives carrying its own designator and the value the library gave it.
        Assert.Equal("R1", symbol.Reference);
        Assert.Equal("R", symbol.Value);
    }

    [Fact]
    public void A_placed_symbol_carries_a_pin_for_every_pin_of_its_unit()
    {
        var sheet = Schematic.Parse(Sheet);
        sheet.Attach(Place(sheet), int.MaxValue);

        string text = Written(sheet);

        // One entry per pin of the definition, each with a uuid of its own.
        Assert.Contains("(pin \"1\"", text, StringComparison.Ordinal);
        Assert.Contains("(pin \"2\"", text, StringComparison.Ordinal);
        Assert.Equal(2, Schematic.Parse(text).Symbols.Single().Node.Lists().Count(l => l.Head == "pin"));
    }

    [Fact]
    public void A_placed_symbol_carries_the_instance_block_annotation_writes_into()
    {
        var sheet = Schematic.Parse(Sheet);
        sheet.Attach(Place(sheet, "R7"), int.MaxValue);

        string text = Written(sheet);

        // The path is the sheet's own uuid, which is what KiCad writes for a design of one sheet.
        Assert.Contains("(instances", text, StringComparison.Ordinal);
        Assert.Contains("(project \"project\"", text, StringComparison.Ordinal);
        Assert.Contains("(path \"/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\"", text, StringComparison.Ordinal);
        Assert.Contains("(reference \"R7\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_definition_is_copied_in_under_the_name_the_sheet_calls_it()
    {
        var sheet = Schematic.Parse(Sheet);

        Assert.True(SchSymbols.Ensure(sheet, "Device:R", Definition()));

        // A library names it "R"; a sheet names the same definition "Device:R".
        Assert.True(sheet.LibrarySymbols.ContainsKey("Device:R"));
        Assert.False(sheet.LibrarySymbols.ContainsKey("R"));
    }

    [Fact]
    public void Copying_a_definition_that_is_already_there_changes_nothing()
    {
        var sheet = Schematic.Parse(Sheet);
        Assert.True(SchSymbols.Ensure(sheet, "Device:R", Definition()));

        byte[] once = sheet.Document.ToBytes();

        Assert.False(SchSymbols.Ensure(sheet, "Device:R", Definition()));
        Assert.Equal(once, sheet.Document.ToBytes());
    }

    [Fact]
    public void Placing_a_part_and_undoing_it_gives_the_file_back()
    {
        var sheet = Schematic.Parse(Sheet);
        byte[] original = sheet.Document.ToBytes();

        var symbol = Place(sheet);
        var history = new UndoStack();
        history.Execute(new AddNodesCommand(sheet, [symbol]));

        Assert.Single(sheet.Symbols);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        history.Undo();

        Assert.Empty(sheet.Symbols);
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void What_was_written_reads_back_as_the_same_tree()
    {
        var sheet = Schematic.Parse(Sheet);
        SchSymbols.Ensure(sheet, "Device:R", Definition());
        var symbol = Place(sheet);
        sheet.Attach(symbol, int.MaxValue);

        string text = Encoding.UTF8.GetString(sheet.Document.ToBytes());
        var again = Schematic.Parse(text);

        Assert.Equal("Device:R", Assert.Single(again.Symbols).LibId);
        Assert.Equal(text, Encoding.UTF8.GetString(again.Document.ToBytes()));
    }
}
