using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// A sheet's copy of a symbol against its library's, as KiCad's <c>lib_symbol_mismatch</c> weighs them: what the
/// part means — its pins, its drawing, its fields' words, its units — and not how either file happens to be spelled.
/// A false "differs" on every part of an older sheet would teach the reader to ignore the check, so real projects
/// written by one KiCad and read against libraries written by another must come out clean.
/// </summary>
public class SymbolDriftTests
{
    private static string Kicad(params string[] parts) => Path.Combine([TestData.KiCadDir, .. parts]);

    /// <summary>
    /// KiCad's own hierarchy test: the sheets were written by KiCad 7, the library they drew from by an earlier
    /// symbol editor. Every part is what the library says but one, and that one really differs: the second of
    /// TEST_STACKED's stacked pins is called "PIN" on the sheet and nothing in the library.
    /// </summary>
    [Fact]
    public void Sheets_and_the_library_they_came_from_do_not_differ_across_versions()
    {
        string folder = Kicad("qa", "data", "eeschema", "netlists", "test_hier_no_connect");
        Assert.SkipUnless(Directory.Exists(folder), TestData.SkipReason);
        var library = SymbolLibrary.Load(Path.Combine(folder, "TEST_LIB.kicad_sym"));

        int compared = 0;
        foreach (string file in Directory.GetFiles(folder, "*.kicad_sch"))
        {
            foreach (var (libId, copy) in Schematic.Load(file).LibrarySymbols)
            {
                if (library.FindByLibId(libId, "TEST_LIB") is not { } symbol)
                {
                    continue;
                }

                var drift = SymbolDrift.Between(copy, library.Flatten(symbol));
                if (libId == "TEST_LIB:TEST_STACKED")
                {
                    Assert.Equal((0, 0, 1, 0), (drift!.PinsAdded, drift.PinsRemoved, drift.PinsChanged, drift.ShapesChanged));
                    Assert.Empty(drift.Fields);
                }
                else
                {
                    Assert.True(drift is null, $"{Path.GetFileName(file)}: {libId} {drift}");
                }

                compared++;
            }
        }

        Assert.True(compared >= 3);
    }

    /// <summary>The CM5 demo and the library beside it, both KiCad 9: its parts are the library's.</summary>
    [Fact]
    public void A_demo_and_its_own_library_do_not_differ()
    {
        string sheet = Kicad("demos", "cm5_minima", "CM5.kicad_sch");
        Assert.SkipUnless(File.Exists(sheet), TestData.SkipReason);
        var library = SymbolLibrary.Load(Kicad("demos", "cm5_minima", "CM5IO.kicad_sym"));

        var copies = Schematic.Load(sheet).LibrarySymbols.Where(p => p.Key.StartsWith("CM5IO:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(copies);
        Assert.All(copies, p => Assert.Null(SymbolDrift.Between(p.Value, library.Flatten(library.FindByLibId(p.Key)!))));
    }

    private const string Part = """
        (kicad_symbol_lib (version 20241209) (generator "anode")
        	(symbol "P" (pin_names (offset 0.508)) (in_bom yes) (on_board yes)
        		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Value" "P" (at 0 -5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Footprint" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
        		(symbol "P_0_1"
        			(rectangle (start -2.54 2.54) (end 2.54 -2.54) (stroke (width 0) (type default)) (fill (type background))))
        		(symbol "P_1_1"
        			(pin input line (at -5.08 0 0) (length 2.54) (name "A" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
        			(pin output line (at 5.08 0 180) (length 2.54) (name "Y" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27))))))))
        """;

    private static LibSymbol Symbol(string text) => SymbolLibrary.Parse(text).Symbols.Single();

    /// <summary>
    /// What older files spell otherwise is the same part: a bare <c>hide</c>, trailing zeros, a colour of all zeros,
    /// a <c>(hide no)</c>, lists in another order, a field standing somewhere else.
    /// </summary>
    [Fact]
    public void Spelling_is_not_a_difference()
    {
        string older = Part
            .Replace("(effects (font (size 1.27 1.27)) (hide yes))", "(effects (font (size 1.270 1.270)) hide)", StringComparison.Ordinal)
            .Replace("(stroke (width 0) (type default))", "(stroke (width 0.000) (type default) (color 0 0 0 0))", StringComparison.Ordinal)
            .Replace("(length 2.54) (name \"A\"", "(length 2.54) (hide no) (name \"A\"", StringComparison.Ordinal)
            .Replace("(fill (type background))))", "(fill (type background)) ))", StringComparison.Ordinal)
            .Replace("(at 0 5.08 0)", "(at 1.27 7.62 0)", StringComparison.Ordinal);

        Assert.NotEqual(Part, older);
        Assert.Null(SymbolDrift.Between(Symbol(older), Symbol(Part)));
    }

    [Fact]
    public void A_pin_moved_added_or_taken_away_is_counted()
    {
        var library = Symbol(Part);

        var moved = Symbol(Part.Replace("(at -5.08 0 0)", "(at -7.62 0 0)", StringComparison.Ordinal));
        var shifted = SymbolDrift.Between(moved, library)!;
        Assert.Equal((0, 0, 1, 0), (shifted.PinsAdded, shifted.PinsRemoved, shifted.PinsChanged, shifted.ShapesChanged));
        Assert.Empty(shifted.Fields);

        // The sheet's copy lacks pin 2: the library added it.
        var older = Symbol(Part.Replace("(pin output line (at 5.08 0 180)", "(pin_gone output line (at 5.08 0 180)", StringComparison.Ordinal));
        var drift = SymbolDrift.Between(older, library)!;
        Assert.Equal((1, 0), (drift.PinsAdded, drift.PinsRemoved));

        // And the other way round.
        Assert.Equal((0, 1), (SymbolDrift.Between(library, older)!.PinsAdded, SymbolDrift.Between(library, older)!.PinsRemoved));
    }

    [Fact]
    public void Drawing_fields_units_and_power_are_each_told()
    {
        var library = Symbol(Part);

        Assert.Equal(1, SymbolDrift.Between(Symbol(Part.Replace("(fill (type background))", "(fill (type none))", StringComparison.Ordinal)), library)!.ShapesChanged);
        Assert.Equal(["Footprint"], SymbolDrift.Between(Symbol(Part.Replace("\"Footprint\" \"\"", "\"Footprint\" \"SOT-23\"", StringComparison.Ordinal)), library)!.Fields);

        // Every placement has its own reference and value: the copy's are not the part.
        Assert.Null(SymbolDrift.Between(Symbol(Part.Replace("\"Value\" \"P\"", "\"Value\" \"~\"", StringComparison.Ordinal).Replace("\"U\"", "\"U2\"", StringComparison.Ordinal)), library));
        Assert.True(SymbolDrift.Between(Symbol(Part.Replace("\"P_1_1\"", "\"P_2_1\"", StringComparison.Ordinal)), library)!.UnitsChanged);
        Assert.True(SymbolDrift.Between(Symbol(Part.Replace("(in_bom yes)", "(power) (in_bom yes)", StringComparison.Ordinal)), library)!.PowerChanged);
    }

    /// <summary>
    /// A derived symbol comes onto a sheet flattened, as KiCad's <c>Flatten</c> makes it: 14528 is 4538 with its own
    /// value and description, so it has 4538's pins and bodies under its own name, and extends nothing.
    /// </summary>
    [Fact]
    public void A_derived_symbol_is_flattened_onto_its_parents_body()
    {
        string file = Kicad("qa", "data", "eeschema", "libs", "4xxx.kicad_sym");
        Assert.SkipUnless(File.Exists(file), TestData.SkipReason);
        var library = SymbolLibrary.Load(file);
        var derived = library.Find("14528")!;
        var parent = library.Find("4538")!;

        var flat = library.Flatten(derived);

        Assert.Null(flat.Extends);
        Assert.Equal("14528", flat.Name);
        Assert.Equal(parent.Pins.Count, flat.Pins.Count);
        Assert.Equal(parent.UnitCount, flat.UnitCount);
        Assert.All(flat.Node.Lists().Where(l => l.Head == "symbol"), body => Assert.StartsWith("14528_", body.AtomAt(1)!.Value, StringComparison.Ordinal));
        Assert.Equal("14528", flat.Value);
        if (derived.Description is { } description)
        {
            Assert.Equal(description, flat.Description);
        }

        // The library itself is left alone, and a symbol that extends nothing is answered as it is.
        Assert.Equal("4538", derived.Extends);
        Assert.Same(parent, library.Flatten(parent));
    }

    /// <summary>A field the derived symbol leaves empty keeps the parent's; any other field it has replaces the parent's.</summary>
    [Fact]
    public void Flattening_keeps_the_parents_words_where_the_child_says_nothing()
    {
        var library = SymbolLibrary.Parse("""
            (kicad_symbol_lib (version 20241209) (generator "anode")
            	(symbol "Base"
            		(property "Reference" "U" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "Base" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Datasheet" "base.pdf" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "ki_keywords" "logic" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Supplier" "A" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(symbol "Base_1_1" (pin input line (at 0 0 0) (length 2.54) (name "A") (number "1"))))
            	(symbol "Child" (extends "Base")
            		(property "Reference" "U" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "Child" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Datasheet" "" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "ki_keywords" "" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "Supplier" "" (at 0 0 0) (effects (font (size 1.27 1.27))))
            		(property "MPN" "C-1" (at 0 0 0) (effects (font (size 1.27 1.27))))))
            """);

        var flat = library.Flatten(library.Find("Child")!);

        string? Field(string name) => flat.Node.Lists().FirstOrDefault(l => l.Head == "property" && l.AtomAt(1)!.Value == name)?.AtomAt(2)!.Value;
        Assert.Equal("Child", Field("Value"));
        Assert.Equal("base.pdf", Field("Datasheet"));
        Assert.Equal("logic", Field("ki_keywords"));
        Assert.Equal(string.Empty, Field("Supplier"));
        Assert.Equal("C-1", Field("MPN"));
        Assert.Equal("Child_1_1", flat.Node.Lists().Single(l => l.Head == "symbol").AtomAt(1)!.Value);
        Assert.Single(flat.Pins);
    }
}
