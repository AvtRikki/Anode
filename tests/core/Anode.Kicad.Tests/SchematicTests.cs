using Anode.Kicad;
using Anode.Tests;

namespace Anode.Kicad.Tests;

public class SchematicTests
{
    [Theory]
    [MemberData(nameof(Sheets))]
    public void Sheets_load_and_write_back_byte_for_byte(string relative)
    {
        Assert.SkipWhen(relative.Length == 0, TestData.SkipReason);

        string path = TestData.FullPath(relative);
        var schematic = Schematic.Load(path);

        Assert.Equal("kicad_sch", schematic.Root.Head);
        Assert.True(schematic.Version > 0);

        // The typed model is a view: nothing it reads may change the bytes.
        Assert.Equal(File.ReadAllBytes(path), schematic.Document.ToBytes());
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void Every_placed_symbol_finds_its_definition(string relative)
    {
        Assert.SkipWhen(relative.Length == 0, TestData.SkipReason);

        var schematic = Schematic.Load(TestData.FullPath(relative));
        foreach (var symbol in schematic.Symbols)
        {
            Assert.False(string.IsNullOrEmpty(symbol.LibId));

            // Sheets carry the definitions they use; a missing one would draw as nothing.
            if (schematic.LibrarySymbols.Count > 0)
            {
                Assert.True(symbol.Definition is not null, $"{symbol.Reference ?? symbol.LibId}: no definition for {symbol.LibId}");
            }
        }
    }

    [Fact]
    public void A_demo_sheet_reads_its_items()
    {
        string path = TestData.FullPath(Path.Combine("demos", "pic_programmer", "pic_programmer.kicad_sch"));
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var schematic = Schematic.Load(path);

        Assert.Equal("A4", schematic.Paper);
        Assert.False(schematic.IsPortrait);
        Assert.Equal("JDM - COM84 PIC Programmer with 13V DC/DC converter", schematic.TitleBlock.Title);
        Assert.NotEmpty(schematic.Symbols);
        Assert.NotEmpty(schematic.Wires);
        Assert.NotEmpty(schematic.Junctions);
        Assert.NotEmpty(schematic.Labels);
        Assert.Contains(schematic.Sheets, s => s.SheetName == "pic_sockets");

        // A symbol places its definition: same library id, pins and a body to draw.
        var symbol = schematic.Symbols.First(s => s.Reference == "U2");
        var definition = Assert.IsType<LibSymbol>(symbol.Definition);
        Assert.Equal(symbol.LibId, definition.Name);
        Assert.NotEmpty(definition.PinsOf(symbol.Unit, symbol.BodyStyle));
        Assert.All(definition.PinsOf(symbol.Unit, symbol.BodyStyle), pin => Assert.True(pin.Length > 0));
    }

    [Fact]
    public void Placement_turns_library_coordinates_into_sheet_coordinates()
    {
        string path = TestData.FullPath(Path.Combine("demos", "pic_programmer", "pic_programmer.kicad_sch"));
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var schematic = Schematic.Load(path);
        var symbol = schematic.Symbols.First(s => s.Reference == "U2");

        // The library frame has Y up and the sheet has Y down, so a placement always mirrors.
        Assert.True(symbol.ToSheet.IsMirrored);

        // The symbol origin lands exactly on the placement point.
        var origin = symbol.ToSheet.Apply(new Geometry.Vector2D(0, 0)).Round();
        Assert.Equal(symbol.Position, origin);
    }

    public static TheoryData<string> Sheets() => TestData.Files(".kicad_sch");
}
