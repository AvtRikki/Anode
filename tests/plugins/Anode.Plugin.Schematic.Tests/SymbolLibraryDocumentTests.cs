using Anode.Kicad;
using Anode.Render;
using Anode.Tests;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// A symbol library as a document, on KiCad's own 4xxx library: a symbol is drawn in its own coordinates, its units
/// and body styles are switched between, a derived symbol shows its parent's body, the inspector speaks the library's
/// terms, and saving what nobody changed gives the file back byte for byte.
/// </summary>
public class SymbolLibraryDocumentTests
{
    private static string File4xxx => Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "libs", "4xxx.kicad_sym");

    [Fact]
    public void The_first_symbol_comes_up_and_any_other_can_be_shown()
    {
        Assert.SkipUnless(File.Exists(File4xxx), TestData.SkipReason);
        using var document = Open();

        Assert.Equal(document.Library.Symbols[0].Name, document.Current?.Name);

        document.Show("4001");
        Assert.Equal("4001", document.Current?.Name);
        Assert.Equal((1, 1), (document.Unit, document.BodyStyle));
        Assert.NotNull(document.Editor);
        Assert.False(document.Editor!.IsReadOnly);
    }

    /// <summary>
    /// The library's Y runs up, the scene's down: a pin the library writes above the origin is drawn above it.
    /// </summary>
    [Fact]
    public void A_symbol_is_drawn_the_right_way_up()
    {
        var library = SymbolLibrary.Parse("""
            (kicad_symbol_lib (version 20241209) (generator "anode")
            	(symbol "Up" (property "Reference" "U" (at 0 5.08 0))
            		(symbol "Up_1_1" (pin input line (at 0 7.62 270) (length 2.54) (name "A") (number "1")))))
            """);
        var symbol = library.Symbols.Single();

        var scene = SchematicSceneBuilder.BuildSymbol(symbol, symbol, 1, 1);
        var pin = symbol.Pins.Single();

        Assert.True(scene.BoundsOf(pin).MaxY < 0, "a pin above the origin in the library came out below it");
        Assert.True(scene.BoundsOf(symbol.Fields.Single()).MaxY < 0);
    }

    /// <summary>
    /// A pin is drawn with its graphic style, as KiCad's painter draws it: a bubble is a ring of lines at the body and
    /// the line starting beyond it, a clock two strokes inside the body, and so on — counted in strokes here.
    /// </summary>
    [Theory]
    [InlineData("line", "input", 1)]
    [InlineData("inverted", "input", 25)]
    [InlineData("clock", "input", 3)]
    [InlineData("inverted_clock", "input", 27)]
    [InlineData("input_low", "input", 3)]
    [InlineData("clock_low", "input", 5)]
    [InlineData("edge_clock_high", "input", 5)]
    [InlineData("output_low", "output", 2)]
    [InlineData("non_logic", "input", 3)]
    [InlineData("line", "no_connect", 3)]
    public void A_pin_is_drawn_with_its_graphic_style(string style, string type, int strokes)
    {
        var library = SymbolLibrary.Parse($$"""
            (kicad_symbol_lib (version 20241209) (generator "anode")
            	(symbol "P" (pin_numbers (hide yes)) (pin_names (hide yes)) (property "Reference" "U" (at 0 5.08 0))
            		(symbol "P_1_1" (pin {{type}} {{style}} (at -7.62 0 0) (length 5.08) (name "A") (number "1")))))
            """);
        var symbol = library.Symbols.Single();

        var scene = SchematicSceneBuilder.BuildSymbol(symbol, symbol, 1, 1);
        var owner = scene.OwnersOf(symbol.Pins.Single()).Single();

        Assert.Equal(strokes, scene.Find(LayerStyle.Sch.Pin)!.Lines.Count(l => l.Owner == owner));
    }

    /// <summary>The bubble of an inverted pin sits against the body, and the pin's line starts beyond it.</summary>
    [Fact]
    public void An_inverted_pins_bubble_sits_against_the_body()
    {
        var library = SymbolLibrary.Parse("""
            (kicad_symbol_lib (version 20241209) (generator "anode")
            	(symbol "P" (property "Reference" "U" (at 0 5.08 0))
            		(symbol "P_1_1" (pin output inverted (at 7.62 0 180) (length 5.08) (name "Y") (number "4")))))
            """);
        var symbol = library.Symbols.Single();
        var scene = SchematicSceneBuilder.BuildSymbol(symbol, symbol, 1, 1);
        var owner = scene.OwnersOf(symbol.Pins.Single()).Single();
        var lines = scene.Find(LayerStyle.Sch.Pin)!.Lines.Where(l => l.Owner == owner).ToList();

        // The body end is at x = 2.54; the ring spans 2.54 to 3.81, and the straight line runs 3.81 to 7.62.
        Assert.InRange(lines.Min(l => Math.Min(l.A.X, l.B.X)), 2.53f, 2.55f);
        Assert.Contains(lines, l => Math.Abs(Math.Min(l.A.X, l.B.X) - 3.81f) < 0.01f && Math.Abs(Math.Max(l.A.X, l.B.X) - 7.62f) < 0.01f);
    }

    /// <summary>4001 is four gates and a power unit, each drawn two ways: switching shows each one's own pins.</summary>
    [Fact]
    public void Units_and_body_styles_are_switched_between()
    {
        Assert.SkipUnless(File.Exists(File4xxx), TestData.SkipReason);
        using var document = Open();
        document.Show("4001");

        var body = document.Body!;
        Assert.Equal(5, body.UnitCount);
        Assert.True(body.HasAlternateBody);

        var first = PinsDrawn(document);
        document.ShowUnit(2);
        Assert.Equal(2, document.Unit);
        Assert.NotEqual(first, PinsDrawn(document));

        document.ShowBodyStyle(2);
        Assert.Equal(2, document.BodyStyle);
        Assert.NotEmpty(PinsDrawn(document));

        // Out of range asks for nothing.
        document.ShowUnit(9);
        Assert.Equal(2, document.Unit);
    }

    /// <summary>A derived symbol carries no body of its own: it is drawn with its parent's, and its own fields.</summary>
    [Fact]
    public void A_derived_symbol_is_drawn_with_its_parents_body()
    {
        Assert.SkipUnless(File.Exists(File4xxx), TestData.SkipReason);
        using var document = Open();

        document.Show("14528");

        Assert.Equal("4538", document.Body?.Name);
        Assert.NotEmpty(PinsDrawn(document));
        Assert.Contains(document.Overview!.Blocks.SelectMany(b => b.Rows), r => r.Value == "4538");
        Assert.Contains(document.Editor!.Scene.TopLevelItems, i => i is SchField f && f.Value == "14528");
    }

    [Fact]
    public void The_inspector_speaks_the_librarys_terms()
    {
        Assert.SkipUnless(File.Exists(File4xxx), TestData.SkipReason);
        using var document = Open();
        document.Show("4001");

        var overview = document.Overview!;
        Assert.Equal("4001", overview.Title);
        var pins = overview.Blocks.Single(b => b.IsConnections);
        Assert.Equal(document.Body!.PinsOf(1, 1).Count(), pins.Rows.Count);

        var pin = document.Body.PinsOf(1, 1).First();
        document.Editor!.SetSelection([pin]);
        var selected = document.Selection!;
        Assert.Contains(selected.Blocks.Single().Rows, r => r.Value == pin.Number);
        Assert.Contains(selected.Blocks.Single().Rows, r => r.Value == pin.ElectricalType);
    }

    /// <summary>Nothing changed, nothing different: the library is written back exactly as it was read.</summary>
    [Fact]
    public async Task Saving_an_unchanged_library_gives_it_back_byte_for_byte()
    {
        Assert.SkipUnless(File.Exists(File4xxx), TestData.SkipReason);
        string folder = Directory.CreateTempSubdirectory("anode-symlib-").FullName;
        try
        {
            using var document = Open();
            string copy = Path.Combine(folder, "copy.kicad_sym");

            Assert.True(await document.SaveAsync(copy));

            Assert.Equal(File.ReadAllBytes(File4xxx), File.ReadAllBytes(copy));
            Assert.Equal(copy, document.FilePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A new symbol is added, brought up, and is one step to undo; the library is changed until saved.</summary>
    [Fact]
    public async Task A_new_symbol_is_brought_up_and_is_one_step_to_undo()
    {
        string folder = Directory.CreateTempSubdirectory("anode-symlib-").FullName;
        try
        {
            string path = Path.Combine(folder, "mine.kicad_sym");
            File.WriteAllText(path, SymbolLibrary.EmptyText);
            using var document = new SymbolLibraryDocument(path, SymbolLibrary.Load(path));
            Assert.Null(document.Current);

            Assert.Null(document.AddSymbol("OPA1612"));
            Assert.Equal("OPA1612", document.Current?.Name);
            Assert.True(document.IsDirty);
            Assert.NotNull(document.Editor);

            // A name taken is refused, and nothing is added.
            Assert.NotNull(document.AddSymbol("OPA1612"));
            Assert.Single(document.Library.Symbols);

            document.Undo();
            Assert.Empty(document.Library.Symbols);
            Assert.Null(document.Current);
            Assert.False(document.IsDirty);

            document.Redo();
            Assert.Equal("OPA1612", document.Current?.Name);

            Assert.True(await document.SaveAsync());
            Assert.False(document.IsDirty);
            Assert.NotNull(SymbolLibrary.Load(path).Find("OPA1612"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static SymbolLibraryDocument Open() => new(File4xxx, SymbolLibrary.Load(File4xxx));

    private static List<string> PinsDrawn(SymbolLibraryDocument document) =>
        [.. document.Editor!.Scene.TopLevelItems.OfType<SchPin>().Select(p => p.Number).Order(StringComparer.Ordinal)];
}
