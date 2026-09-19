using System.Text;
using Anode.Geometry;
using Anode.Kicad.Editing;
using Anode.Tests;

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

    /// <summary>
    /// A part drawn in sections, the way a quad gate is: unit 0 carries what every section shares — the power pin —
    /// and units 1 and 2 carry a gate each. Every multi-section part in the demo designs is shaped this way.
    /// </summary>
    private const string GateLibrary = """
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "anode")
        	(symbol "G"
        		(property "Reference" "U"
        			(at 0 0 90)
        		)
        		(property "Value" "G"
        			(at 0 0 90)
        		)
        		(symbol "G_0_1"
        			(pin power_in line
        				(at 0 7.62 270)
        				(length 1.27)
        				(name "VCC")
        				(number "14")
        			)
        		)
        		(symbol "G_1_1"
        			(pin input line
        				(at -5.08 0 0)
        				(length 1.27)
        				(name "A")
        				(number "1")
        			)
        		)
        		(symbol "G_2_1"
        			(pin input line
        				(at -5.08 0 0)
        				(length 1.27)
        				(name "A")
        				(number "4")
        			)
        		)
        	)
        )
        """;

    private static readonly Vector2L At = new(50_800_000, 44_450_000);

    private static LibSymbol Definition() => SymbolLibrary.Parse(Library).Find("R")!;

    private static LibSymbol Gate() => SymbolLibrary.Parse(GateLibrary).Find("G")!;

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
    public void A_placed_symbol_carries_a_pin_for_every_pin_of_the_part()
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

    [Fact]
    public void A_part_says_how_many_sections_it_is_drawn_in()
    {
        Assert.Equal(2, Gate().UnitCount);

        // A part with one body is one section, not none: the count is what the inspector decides to ask about.
        Assert.Equal(1, Definition().UnitCount);
    }

    [Fact]
    public void A_placed_section_carries_every_pin_of_the_whole_part()
    {
        var sheet = Schematic.Parse(Sheet);
        sheet.Attach(
            SchSymbols.Place(sheet, "Logic:G", Gate(), At, "U1", "project", SchSymbols.PathOf(sheet), unit: 2),
            int.MaxValue);

        // The shared power pin and both gates' inputs, on a placement of section 2 alone — which is what KiCad
        // writes, and what the four sections of the 74LS125 in the demos each carry.
        string text = Written(sheet);
        Assert.Contains("(pin \"14\"", text, StringComparison.Ordinal);
        Assert.Contains("(pin \"1\"", text, StringComparison.Ordinal);
        Assert.Contains("(pin \"4\"", text, StringComparison.Ordinal);
        Assert.Equal(3, Schematic.Parse(text).Symbols.Single().Node.Lists().Count(l => l.Head == "pin"));
    }

    [Fact]
    public void The_section_of_a_placed_part_is_written_in_both_places()
    {
        var sheet = Schematic.Parse(Sheet);
        var symbol = SchSymbols.Place(sheet, "Logic:G", Gate(), At, "U1", "project", SchSymbols.PathOf(sheet));
        sheet.Attach(symbol, int.MaxValue);

        SchWrites.SetUnit(symbol, 2);

        Assert.Equal(2, symbol.Unit);

        // On the symbol and in the instance block, or the file contradicts itself — the designator's rule exactly.
        string text = Written(sheet);
        Assert.Equal(2, text.Split("(unit 2)").Length - 1);
        Assert.DoesNotContain("(unit 1)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_section_and_undoing_it_gives_the_file_back()
    {
        var sheet = Schematic.Parse(Sheet);
        var symbol = SchSymbols.Place(sheet, "Logic:G", Gate(), At, "U1", "project", SchSymbols.PathOf(sheet));
        sheet.Attach(symbol, int.MaxValue);
        byte[] placed = sheet.Document.ToBytes();

        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand("Section", [symbol], () => SchWrites.SetUnit(symbol, 2)));

        Assert.NotEqual(placed, sheet.Document.ToBytes());

        history.Undo();

        Assert.Equal(1, symbol.Unit);
        Assert.Equal(placed, sheet.Document.ToBytes());
    }

    [Fact]
    public void A_section_below_the_first_is_refused()
    {
        var sheet = Schematic.Parse(Sheet);
        var symbol = Place(sheet);

        Assert.Throws<ArgumentOutOfRangeException>(() => SchWrites.SetUnit(symbol, 0));
    }

    [Fact]
    public void The_sections_of_a_real_quad_gate_are_counted_and_placed()
    {
        string path = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipWhen(!File.Exists(path), TestData.SkipReason);

        var sheet = Schematic.Load(path);
        var gate = sheet.LibrarySymbols["pic_programmer:74LS125"];

        // Four gates in the package, drawn as units 1-4 over a body (unit 0) they share. Every fixture I invented
        // had a single section and agreed with the code whatever it did; this one is the part that disagrees.
        Assert.Equal(4, gate.UnitCount);
        Assert.Equal(
            [1, 2, 3, 4],
            sheet.Symbols.Where(s => s.LibId == "pic_programmer:74LS125").Select(s => s.Unit).Order());
    }

    /// <summary>The same resistor after someone added a wiper to it in the library.</summary>
    private const string NewerLibrary = """
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
        			(pin passive line
        				(at 2.54 0 180)
        				(length 1.27)
        				(name "W")
        				(number "3")
        			)
        		)
        	)
        )
        """;

    [Fact]
    public void A_definition_is_updated_in_place_and_what_reads_it_follows()
    {
        var sheet = Schematic.Parse(Sheet);
        SchSymbols.Ensure(sheet, "Device:R", Definition());
        var definition = sheet.LibrarySymbols["Device:R"];

        Assert.Equal(2, definition.Pins.Count);

        Assert.True(SchSymbols.Update(sheet, "Device:R", SymbolLibrary.Parse(NewerLibrary).Find("R")!));

        // The sheet keeps handing out the same wrapper, so the wrapper has to describe the part as it is now.
        Assert.Same(definition, sheet.LibrarySymbols["Device:R"]);
        Assert.Equal(3, definition.Pins.Count);

        // And it is still known by the name the sheet calls it, not the one the library used.
        Assert.Equal("Device:R", definition.Name);
    }

    [Fact]
    public void Updating_a_definition_and_undoing_it_gives_the_file_back()
    {
        var sheet = Schematic.Parse(Sheet);
        SchSymbols.Ensure(sheet, "Device:R", Definition());
        sheet.Attach(Place(sheet), int.MaxValue);
        byte[] original = sheet.Document.ToBytes();
        var definition = sheet.LibrarySymbols["Device:R"];

        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand(
            "Update",
            [definition],
            () => SchSymbols.Update(sheet, "Device:R", SymbolLibrary.Parse(NewerLibrary).Find("R")!)));

        Assert.Equal(3, definition.Pins.Count);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());

        // The wrapper follows the undo as well; a definition read once would still be showing the third pin.
        Assert.Equal(2, definition.Pins.Count);
    }

    [Fact]
    public void A_definition_the_sheet_does_not_carry_is_not_updated()
    {
        var sheet = Schematic.Parse(Sheet);

        Assert.False(SchSymbols.Update(sheet, "Device:R", Definition()));
    }

    [Fact]
    public void A_change_to_a_definition_touches_it_and_every_placement_of_it()
    {
        var sheet = Schematic.Parse(Sheet);
        SchSymbols.Ensure(sheet, "Device:R", Definition());
        sheet.Attach(Place(sheet, "R1"), int.MaxValue);
        sheet.Attach(Place(sheet, "R2"), int.MaxValue);

        // A part of another kind, to be sure the list is about this definition and not about everything placed.
        SchSymbols.Ensure(sheet, "Logic:G", Gate());
        sheet.Attach(
            SchSymbols.Place(sheet, "Logic:G", Gate(), At, "U1", "project", SchSymbols.PathOf(sheet)),
            int.MaxValue);

        var affected = SchSymbols.Affected(sheet, "Device:R");

        // The definition itself leads: without it in the list, undo has no snapshot of the body that was replaced.
        Assert.Same(sheet.LibrarySymbols["Device:R"], affected[0]);
        Assert.Equal(["R1", "R2"], affected.Skip(1).Cast<SymbolInstance>().Select(s => s.Reference));
    }
}
