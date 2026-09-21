using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The numbers a design has used. A designator has to be unique across the whole design, not across one sheet —
/// otherwise two sheets each hand out R1 and the netlist describes two different parts by the same name.
/// </summary>
public class DesignNumbersTests
{
    [Fact]
    public void A_number_used_on_one_sheet_is_not_free_on_another()
    {
        string folder = Directory.CreateTempSubdirectory("anode-numbers-").FullName;
        try
        {
            string root = Design(folder, ["R1", "R2"], ["R3", "C1"]);
            var numbers = DesignNumbers.Of(root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(numbers.IsTaken("R", 3), "R3 stands on the child sheet");
            Assert.Equal(4, numbers.FirstFree("R"));
            Assert.Equal(2, numbers.FirstFree("C"));

            // What one sheet knows on its own is exactly the trap this exists to avoid.
            Assert.Equal(3, DesignNumbers.Of(Schematic.Load(root)).FirstFree("R"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_first_free_number_fills_a_gap_rather_than_stepping_past_it()
    {
        string folder = Directory.CreateTempSubdirectory("anode-numbers-").FullName;
        try
        {
            string root = Design(folder, ["R1", "R7"], ["R8"]);
            var numbers = DesignNumbers.Of(root, cancellationToken: TestContext.Current.CancellationToken);

            // KiCad counts up from one and takes the first nobody has.
            Assert.Equal(2, numbers.FirstFree("R"));

            // And handing one out takes it, so the next part laid down gets the one after.
            Assert.Equal(2, numbers.TakeFirstFree("R"));
            Assert.Equal(3, numbers.TakeFirstFree("R"));
            Assert.Equal(9, numbers.TakeFirstFree("R", 8));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Annotating_two_sheets_of_one_design_gives_no_number_twice()
    {
        string folder = Directory.CreateTempSubdirectory("anode-numbers-").FullName;
        try
        {
            string root = Design(folder, ["R1", "R?"], ["R?", "R?"]);
            var numbers = DesignNumbers.Of(root, cancellationToken: TestContext.Current.CancellationToken);

            var parent = Schematic.Load(root);
            var child = Schematic.Load(Path.Combine(folder, "block.kicad_sch"));

            // The same set carries from one sheet to the next, which is what keeps them apart.
            var here = SchAnnotation.Annotate(parent, parent.Symbols, null, numbers);
            var there = SchAnnotation.Annotate(child, child.Symbols, null, numbers);

            Assert.Equal(["R2"], here.Select(g => g.Reference));
            Assert.Equal(["R3", "R4"], there.Select(g => g.Reference));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_sheet_placed_twice_has_its_numbers_counted_once_per_place()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var numbers = DesignNumbers.Of(root, cancellationToken: TestContext.Current.CancellationToken);

        // The amplifier sheet stands twice and its parts are numbered per place: both are spoken for.
        Assert.True(numbers.IsTaken("R", 201), "R201 is the first place of the amplifier sheet");
        Assert.True(numbers.IsTaken("R", 301), "R301 is the second");

        // Nothing the design already carries is offered again.
        foreach (var line in SchBom.Build(root))
        {
            foreach (string reference in line.References)
            {
                string prefix = SchAnnotation.PrefixOf(reference);
                Assert.NotEqual(reference, prefix + numbers.FirstFree(prefix));
            }
        }
    }

    /// <summary>A root sheet with one child, each carrying the designators given.</summary>
    private static string Design(string folder, string[] onRoot, string[] onChild)
    {
        const string rootUuid = "11111111-0000-4000-8000-000000000001";
        string root = Path.Combine(folder, "design.kicad_sch");

        File.WriteAllText(root, $"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "{rootUuid}") (paper "A4")
            {Symbols(onRoot)}	(sheet (at 101.6 50.8) (size 20.32 20.32) (uuid "22222222-0000-4000-8000-000000000001")
            		(property "Sheetname" "block" (at 101.6 50 0))
            		(property "Sheetfile" "block.kicad_sch" (at 101.6 71.9 0)))
            	(embedded_fonts no))

            """);

        File.WriteAllText(Path.Combine(folder, "block.kicad_sch"), $"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "77777777-0000-4000-8000-000000000001") (paper "A4")
            {Symbols(onChild)}	(embedded_fonts no))

            """);

        return root;
    }

    private static string Symbols(string[] references) =>
        string.Concat(references.Select((reference, i) =>
            $"\t(symbol (lib_id \"Device:R\") (at 50.8 {44.45 + (i * 2.54)} 0) (unit 1)"
            + $" (uuid \"0a1b2c3d-0000-4000-8000-00000000000{i}\")\n"
            + $"\t\t(property \"Reference\" \"{reference}\" (at 50.8 44.45 0))\n"
            + $"\t\t(property \"Value\" \"10k\" (at 50.8 44.45 0)))\n"));
}
