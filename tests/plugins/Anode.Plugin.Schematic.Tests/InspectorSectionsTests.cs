using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Sdk;

// Inside this namespace "Schematic" names the plugin's own namespace segment, not the sheet.
using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// What the inspector offers for a part drawn in sections. The rows themselves are the only part of this the user
/// ever sees, and a green core proved nothing about them: the number has to appear on a part that has sections, stay
/// away from one that does not, and write through to the file when it is committed.
/// </summary>
public class InspectorSectionsTests
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

    /// <summary>A gate drawn in two sections over a body they share, and a resistor drawn in one.</summary>
    private const string Library = """
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
        		(symbol "G_1_2"
        			(pin input inverted
        				(at -5.08 0 0)
        				(length 1.27)
        				(name "A")
        				(number "1")
        			)
        		)
        	)
        	(symbol "R"
        		(property "Reference" "R"
        			(at 0 0 90)
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
        		)
        	)
        )
        """;

    private static readonly Vector2L At = new(50_800_000, 44_450_000);

    /// <summary>The row's name as the panel shows it, whatever language is active.</summary>
    private static string UnitRow => Tr.T("sch.property.unit");

    private static string BodyStyleRow => Tr.T("sch.property.bodyStyle");

    /// <summary>
    /// A sheet carrying one placed part. The definition is copied in first: the inspector asks the sheet what the
    /// part is, so a placement whose definition is missing has nothing to count sections from.
    /// </summary>
    private static SymbolInstance Place(string name)
    {
        var sheet = KicadSchematic.Parse(Sheet);
        var definition = SymbolLibrary.Parse(Library).Find(name)!;
        string libId = $"Logic:{name}";

        SchSymbols.Ensure(sheet, libId, definition);
        var symbol = SchSymbols.Place(sheet, libId, definition, At, "U1", "project", SchSymbols.PathOf(sheet));
        sheet.Attach(symbol, int.MaxValue);
        return symbol;
    }

    /// <summary>Every row of the inspector, with edits applied straight through rather than through a history.</summary>
    private static List<InspectorRow> Rows(SymbolInstance symbol) =>
        [.. SchItemProperties.Blocks(symbol, (_, mutate) => mutate()).SelectMany(b => b.Rows)];

    [Fact]
    public void A_part_drawn_in_sections_is_asked_which_one_it_is()
    {
        var row = Assert.Single(Rows(Place("G")), r => r.Name == UnitRow);

        Assert.Equal("1", row.Value);

        // The fill that tells the eye a row can be written is the presence of this.
        Assert.NotNull(row.Commit);
    }

    [Fact]
    public void A_part_with_one_section_is_not_asked_at_all()
    {
        Assert.DoesNotContain(Rows(Place("R")), r => r.Name == UnitRow);
    }

    [Fact]
    public void A_part_drawn_a_second_way_is_asked_which_way_it_is()
    {
        var row = Assert.Single(Rows(Place("G")), r => r.Name == BodyStyleRow);

        // A switch, not a typed value: there are two ways and no more.
        Assert.False(row.Switch);
        Assert.NotNull(row.Commit);
    }

    [Fact]
    public void A_part_drawn_one_way_is_not_asked_at_all()
    {
        Assert.DoesNotContain(Rows(Place("R")), r => r.Name == BodyStyleRow);
    }

    [Fact]
    public void The_way_of_drawing_that_is_committed_reaches_the_file()
    {
        var symbol = Place("G");

        Assert.Single(Rows(symbol), r => r.Name == BodyStyleRow).Commit!("yes");
        Assert.Equal(2, symbol.BodyStyle);
        Assert.True(Assert.Single(Rows(symbol), r => r.Name == BodyStyleRow).Switch);

        Assert.Single(Rows(symbol), r => r.Name == BodyStyleRow).Commit!("no");
        Assert.Equal(1, symbol.BodyStyle);
    }

    [Fact]
    public void The_section_that_is_committed_reaches_the_file()
    {
        var symbol = Place("G");

        Assert.Single(Rows(symbol), r => r.Name == UnitRow).Commit!("2");

        Assert.Equal(2, symbol.Unit);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("two")]
    public void A_section_the_part_does_not_have_is_left_alone(string typed)
    {
        var symbol = Place("G");

        Assert.Single(Rows(symbol), r => r.Name == UnitRow).Commit!(typed);

        // The gate has two sections; anything else is a typing slip, and a slip must not reach the file.
        Assert.Equal(1, symbol.Unit);
    }
}
