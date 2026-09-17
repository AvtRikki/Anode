using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Reading a .kicad_sym library. A library is where a schematic gets its parts, so the first thing asked of it is
/// the same thing asked of every other file here: read it, write it, and get back exactly what was there.
/// </summary>
public class SymbolLibraryTests
{
    private const string Small = """
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "kicad_symbol_editor")
        	(symbol "R"
        		(property "Reference" "R"
        			(at 2.032 0 90)
        		)
        		(property "Value" "R"
        			(at 0 0 90)
        		)
        		(symbol "R_0_1"
        			(rectangle
        				(start -1.016 -2.54)
        				(end 1.016 2.54)
        			)
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
        	(symbol "C"
        		(property "Reference" "C"
        			(at 0.635 2.54 0)
        		)
        	)
        )
        """;

    [Fact]
    public void A_library_lists_its_symbols_in_order()
    {
        var library = SymbolLibrary.Parse(Small);

        Assert.Equal(20250324, library.Version);
        Assert.Equal("kicad_symbol_editor", library.Generator);
        Assert.Equal(["R", "C"], library.Symbols.Select(s => s.Name));
        Assert.True(library.IsSupportedVersion);
        Assert.False(library.IsNewerThanKnown);
    }

    [Fact]
    public void A_symbol_is_found_by_name_and_by_lib_id()
    {
        var library = SymbolLibrary.Parse(Small);

        Assert.Same(library.Symbols[0], library.Find("R"));
        Assert.Null(library.Find("NotHere"));

        // The nickname belongs to the table, not the file, so a lib_id may or may not carry one.
        Assert.Same(library.Symbols[0], library.FindByLibId("R"));
        Assert.Same(library.Symbols[0], library.FindByLibId("Device:R"));

        // Asking the wrong library for a qualified name answers nothing rather than the wrong symbol.
        Assert.Null(library.FindByLibId("Device:R", nickname: "Connector"));
        Assert.Same(library.Symbols[0], library.FindByLibId("Device:R", nickname: "Device"));
    }

    [Fact]
    public void A_symbols_body_and_pins_are_read_the_way_a_sheet_reads_them()
    {
        var library = SymbolLibrary.Parse(Small);
        var resistor = library.Find("R");

        Assert.NotNull(resistor);
        Assert.Equal(["1", "2"], resistor!.PinsOf(1, 1).Select(p => p.Number));
        Assert.Single(resistor.GraphicsOf(0, 1));
    }

    [Fact]
    public void Something_that_is_not_a_library_is_refused()
    {
        Assert.Throws<KiCadFormatException>(() => SymbolLibrary.Parse("(kicad_sch (version 20250324))"));
    }

    [Fact]
    public void A_real_library_reads_and_writes_back_byte_for_byte()
    {
        string? path = TestData.AnySymbolLibrary();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var library = SymbolLibrary.Load(path!);

        Assert.NotEmpty(library.Symbols);
        Assert.All(library.Symbols, symbol => Assert.False(string.IsNullOrEmpty(symbol.Name)));

        // Every symbol is reachable by the name the file gave it.
        Assert.All(library.Symbols, symbol => Assert.NotNull(library.Find(symbol.Name)));

        Assert.Equal(File.ReadAllBytes(path!), library.Document.ToBytes());
    }
}
