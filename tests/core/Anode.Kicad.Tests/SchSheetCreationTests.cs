using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Making a child sheet: the rectangle that stands for it, the file it reads, and the pins on its edge. What is
/// written has to be a design the rest of the program agrees with — so it is read back, walked, checked and wired.
/// </summary>
public class SchSheetCreationTests
{
    private const long Mm = 1_000_000;

    [Fact]
    public void A_new_sheet_is_read_back_as_the_sheet_it_was_written_as()
    {
        var sheet = SchNodes.Sheet(
            "power",
            "power.kicad_sch",
            new Vector2L(101_600_000, 50_800_000),
            new Vector2L(20 * Mm, 30 * Mm),
            [("design", "/aaaa", "2")]);

        var read = Schematic.Parse($"(kicad_sch (version 20250114) (paper \"A4\")\n\t{sheet.Node}\n)\n").Sheets.Single();

        Assert.Equal("power", read.SheetName);
        Assert.Equal("power.kicad_sch", read.SheetFile);
        Assert.Equal(new Vector2L(101_600_000, 50_800_000), read.Position);
        Assert.Equal(new Vector2L(20 * Mm, 30 * Mm), read.Size);
        Assert.Empty(read.Pins);

        // The name stands above the top-left corner and the file below the bottom-left one, as KiCad places them.
        var name = read.Fields.Single(f => f.Name == "Sheetname");
        var file = read.Fields.Single(f => f.Name == "Sheetfile");
        Assert.Equal(read.Position.X, name.Position.X);
        Assert.True(name.Position.Y < read.Position.Y, "the name is above the sheet");
        Assert.True(file.Position.Y > read.Position.Y + read.Size.Y, "the file is below the sheet");
    }

    [Theory]
    [InlineData(SheetSide.Left, 180)]
    [InlineData(SheetSide.Right, 0)]
    [InlineData(SheetSide.Top, 90)]
    [InlineData(SheetSide.Bottom, 270)]
    public void A_pin_carries_its_edge_as_the_angle_KiCad_writes(SheetSide side, double angle)
    {
        var pin = SchNodes.SheetPin("IN", "input", new Vector2L(10 * Mm, 20 * Mm), side);

        Assert.Equal("IN", pin.Name);
        Assert.Equal("input", pin.Shape);
        Assert.Equal(angle, pin.Angle);
    }

    [Fact]
    public void A_point_near_a_sheet_lands_on_the_edge_it_is_nearest()
    {
        var sheet = Sheet(new Vector2L(100 * Mm, 100 * Mm), new Vector2L(40 * Mm, 20 * Mm));

        // Just inside the left border, a third of the way down: the left edge, at that height.
        var left = SchSheets.OnEdge(sheet, new Vector2L(103 * Mm, 107 * Mm), out var leftSide);
        Assert.Equal(SheetSide.Left, leftSide);
        Assert.Equal(new Vector2L(100 * Mm, 107 * Mm), left);

        var right = SchSheets.OnEdge(sheet, new Vector2L(137 * Mm, 112 * Mm), out var rightSide);
        Assert.Equal(SheetSide.Right, rightSide);
        Assert.Equal(new Vector2L(140 * Mm, 112 * Mm), right);

        var top = SchSheets.OnEdge(sheet, new Vector2L(120 * Mm, 101 * Mm), out var topSide);
        Assert.Equal(SheetSide.Top, topSide);
        Assert.Equal(new Vector2L(120 * Mm, 100 * Mm), top);

        var bottom = SchSheets.OnEdge(sheet, new Vector2L(120 * Mm, 119 * Mm), out var bottomSide);
        Assert.Equal(SheetSide.Bottom, bottomSide);
        Assert.Equal(new Vector2L(120 * Mm, 120 * Mm), bottom);

        // A point beyond a corner is still on the edge: it is held between the corners, not carried past them.
        var beyond = SchSheets.OnEdge(sheet, new Vector2L(90 * Mm, 60 * Mm), out _);
        Assert.Equal(new Vector2L(100 * Mm, 100 * Mm), beyond);
    }

    [Fact]
    public void A_pin_is_added_where_KiCad_keeps_one_and_the_sheet_knows_it()
    {
        var sheet = Sheet(new Vector2L(100 * Mm, 100 * Mm), new Vector2L(40 * Mm, 20 * Mm), [("design", "/aaaa", "2")]);

        SchSheets.AddPin(sheet, "IN", "input", new Vector2L(100 * Mm, 105 * Mm), SheetSide.Left);
        SchSheets.AddPin(sheet, "OUT", "output", new Vector2L(140 * Mm, 105 * Mm), SheetSide.Right);

        Assert.Equal(["IN", "OUT"], sheet.Pins.Select(p => p.Name));

        // The pins stand after the fields and before the instances, which is the order KiCad writes a sheet in.
        var heads = sheet.Node.Lists().Select(l => l.Head).ToList();
        Assert.Equal(
            ["property", "property", "pin", "pin", "instances"],
            heads.Where(h => h is "property" or "pin" or "instances"));
    }

    /// <summary>
    /// The whole gesture, as the editor performs it: a sheet is placed on the root, its file is written beside it
    /// with the labels that answer its pins, and the design that comes out is one the rest of the program reads —
    /// walked as a hierarchy, quiet under the sheet rules, and wired through the pin into the sheet below.
    /// </summary>
    [Fact]
    public void A_sheet_written_beside_its_parent_is_a_design_the_rest_of_the_program_reads()
    {
        string folder = Directory.CreateTempSubdirectory("anode-new-sheet-").FullName;
        try
        {
            string rootFile = Path.Combine(folder, "design.kicad_sch");
            const string rootUuid = "11111111-0000-4000-8000-000000000001";
            File.WriteAllText(rootFile, $"""
                (kicad_sch (version 20250114) (generator "eeschema") (uuid "{rootUuid}") (paper "A4")
                	(embedded_fonts no))

                """);

            var root = Schematic.Load(rootFile);

            // The child is written first, because a sheet that reads a file which is not there is a broken design.
            var child = SchSheets.NewSheet(root);
            var label = SchNodes.Label(SchLabelKind.Hierarchical, "IN", new Vector2L(60 * Mm, 60 * Mm));
            child.Attach(label, child.Root.Count);
            child.Save(Path.Combine(folder, "block.kicad_sch"));

            var placed = SchNodes.Sheet(
                "block",
                "block.kicad_sch",
                new Vector2L(100 * Mm, 100 * Mm),
                new Vector2L(40 * Mm, 20 * Mm),
                [("design", "/" + rootUuid, "2")]);

            SchSheets.AddPin(placed, "IN", "input", new Vector2L(100 * Mm, 105 * Mm), SheetSide.Left);
            root.Attach(placed, root.Root.Count);
            root.Save(rootFile);

            // Walked as a hierarchy: the root and the sheet under it, the child knowing where it stands.
            var places = SchHierarchy.Walk(rootFile, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal([rootFile, Path.Combine(folder, "block.kicad_sch")], places.Select(p => p.File));
            Assert.Equal("block", places[1].Name);
            Assert.Equal($"/{rootUuid}/{placed.Uuid}", places[1].Path);

            // The pin and the label answer each other, so the sheet rules have nothing to say.
            Assert.Empty(SchErc.CheckSheets(places, Schematic.Load));

            // And the net is one net through both sheets: the pin above, the label below.
            var net = Assert.Single(SchDesignNets.Build(rootFile), n => n.Name.EndsWith("IN", StringComparison.Ordinal));
            Assert.Contains(net.Parts, part => part.Place.File == rootFile);
            Assert.Contains(net.Parts, part => part.Place.Name == "block");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static SchSheet Sheet(Vector2L at, Vector2L size, (string Project, string Path, string Page)[]? places = null) =>
        SchNodes.Sheet("block", "block.kicad_sch", at, size, places);
}
