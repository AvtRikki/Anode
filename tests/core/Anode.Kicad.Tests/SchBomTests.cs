using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// The bill of materials, laid out by the preset the project names — KiCad's own default when it names none — for
/// every part in every place of the design, and exported as KiCad's CSV export writes it.
/// </summary>
public class SchBomTests
{
    private const string Path0 = "/6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10";

    private static string Root => Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");

    [Fact]
    public void Every_part_of_every_place_is_on_it_once()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        var table = SchBom.Build(Root);
        var references = table.Rows.SelectMany(r => r.References).ToList();

        Assert.NotEmpty(table.Rows);
        Assert.Equal(references.Count, references.Distinct(StringComparer.Ordinal).Count());
        Assert.All(references, r => Assert.False(r.StartsWith('#'), $"{r} is a power symbol, not a part"));

        // The amplifier sheet stands twice, so its parts are there under both designators.
        Assert.Contains("R201", references);
        Assert.Contains("R301", references);
        Assert.All(table.Rows, r => Assert.Equal(r.References.Count, r.Quantity));
    }

    /// <summary>
    /// The demo's project names its preset as KiCad 8.0 first wrote it — the count as a column called "Quantity",
    /// which no KiCad since reads as the count. Current KiCad shows it as a field no part has: an empty column.
    /// </summary>
    [Fact]
    public void The_project_names_the_preset_and_it_is_read_as_KiCad_reads_it()
    {
        Assert.SkipUnless(File.Exists(Root), TestData.SkipReason);

        var (preset, _) = BomPreset.Read(SchBom.ProjectOf(Root));

        Assert.Equal(["Reference", "Value", "Datasheet", "Footprint", "Quantity"], preset.Fields.Where(f => f.Show).Select(f => f.Name));
        Assert.Equal(["Value"], preset.Fields.Where(f => f.GroupBy).Select(f => f.Name));
        Assert.False(preset.IncludeExcludedFromBom);

        string header = SchBom.Write(Root).Split('\n')[0];
        Assert.Equal("\"Reference\",\"Value\",\"Datasheet\",\"Footprint\",\"Qty\"", header);
    }

    /// <summary>
    /// A project that names no preset gets KiCad's Default Editing: grouped by value, footprint and the flags, and
    /// with the parts kept off the bill still listed, marked as such.
    /// </summary>
    [Fact]
    public void Without_a_preset_the_bill_is_KiCads_default_editing()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.DefaultEditing);

        Assert.Equal(["R1", "R2", "R3", "TP1", "U1"], table.Rows.Select(r => r.References[0]));
        Assert.Equal(["R2", "R4"], table.Rows.Single(r => r.References[0] == "R2").References);
        Assert.Equal("Excluded from BOM", BomTable.Text(table.Rows.Single(r => r.References[0] == "TP1"), BomPreset.ExcludeFromBom, false));
        Assert.Equal("DNP", BomTable.Text(table.Rows.Single(r => r.References[0] == "R3"), BomPreset.Dnp, false));
    }

    /// <summary>
    /// Grouped by value alone, parts of one value on two footprints are one line; on screen the footprint is mixed,
    /// and the export names both, as KiCad lists mixed values.
    /// </summary>
    [Fact]
    public void Grouped_by_value_one_line_names_both_footprints()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue);
        var tenK = table.Rows.Single(r => r.References.Contains("R1"));

        Assert.Equal(["R1", "R2", "R4"], tenK.References);
        Assert.Null(BomTable.Text(tenK, "Footprint", forExport: false));
        Assert.Equal("R_0603,R_0805", BomTable.Text(tenK, "Footprint", forExport: true));
        Assert.DoesNotContain(table.Rows, r => r.References.Contains("TP1"));
    }

    [Fact]
    public void The_units_of_one_part_are_one_line_even_with_grouping_off()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue with { GroupSymbols = false });

        var gate = table.Rows.Single(r => r.References.Contains("U1"));
        Assert.Equal(2, gate.Parts.Count);
        Assert.Equal(1, gate.Quantity);
        Assert.Equal(["R1", "R2", "R3", "R4", "U1"], table.Rows.Select(r => r.References[0]));
    }

    /// <summary>A preset that groups by nothing groups nothing — KiCad's groupMatch finds no column to match on.</summary>
    [Fact]
    public void A_preset_that_groups_by_nothing_groups_nothing()
    {
        var preset = BomPreset.GroupedByValue with { Fields = [.. BomPreset.GroupedByValue.Fields.Select(f => f with { GroupBy = false })] };

        Assert.Equal(5, BomTable.Build(Parts(Sheet()), preset).Rows.Count);
    }

    [Fact]
    public void Parts_not_placed_are_left_out_when_the_preset_says_so()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue with { ExcludeDnp = true });

        Assert.DoesNotContain(table.Rows, r => r.References.Contains("R3"));
    }

    [Fact]
    public void Lines_sort_by_the_presets_column_and_are_numbered_after()
    {
        var preset = BomPreset.GroupedByValue with { SortField = "Value", SortAscending = false };
        var table = BomTable.Build(Parts(Sheet()), preset);

        Assert.Equal("74HC00", BomTable.Text(table.Rows[0], "Value", false));
        Assert.Equal([1, 2, 3], table.Rows.Select(r => r.ItemNumber));
        Assert.Equal("1", BomTable.Text(table.Rows[0], BomPreset.ItemNumber, false));
    }

    /// <summary>
    /// KiCad's CSV export: the shown columns under their labels, every value quoted, the designators of a line one by
    /// one with commas inside their quotes — no ranges, the CSV format preset has none.
    /// </summary>
    [Fact]
    public void The_csv_is_KiCads_export()
    {
        var csv = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue).Csv();
        string[] rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("\"Reference\",\"Value\",\"Datasheet\",\"Footprint\",\"Qty\",\"DNP\"", rows[0]);
        Assert.Equal("\"R1,R2,R4\",\"10k\",\"\",\"R_0603,R_0805\",\"3\",\"\"", rows[1]);
        Assert.Contains("\"R3\",\"10k\",\"\",\"R_0603\",\"1\",\"DNP\"", rows);
    }

    [Theory]
    [InlineData(new[] { "R1", "R2", "R3", "R4", "R7" }, "R1-R4, R7")]
    [InlineData(new[] { "R1", "R2", "R5" }, "R1, R2, R5")]
    [InlineData(new[] { "C1", "R1", "R2", "R3" }, "C1, R1-R3")]
    [InlineData(new[] { "U?", "U?" }, "U?, U?")]
    public void Designators_are_shortened_on_screen_as_KiCad_shortens_them(string[] references, string shown) =>
        Assert.Equal(shown, BomTable.Shorthand(references));

    [Fact]
    public void A_preset_written_as_KiCad_writes_one_is_read_back()
    {
        string folder = Directory.CreateTempSubdirectory("anode-bom-").FullName;
        try
        {
            string project = Path.Combine(folder, "p.kicad_pro");
            File.WriteAllText(project, """
                { "schematic": { "bom_settings": {
                    "name": "Mine", "sort_field": "Value", "sort_asc": false, "filter_string": "R",
                    "group_symbols": false, "exclude_dnp": true, "include_excluded_from_bom": true,
                    "fields_ordered": [ { "name": "Reference", "label": "Refs", "show": true, "group_by": false },
                                        { "name": "MPN", "label": "Part number", "show": true, "group_by": true } ] },
                  "bom_presets": [ { "name": "Other", "sort_field": "Reference", "sort_asc": true, "filter_string": "",
                    "group_symbols": true, "exclude_dnp": false,
                    "fields_ordered": [ { "name": "Reference", "label": "Reference", "show": true, "group_by": false } ] } ] } }
                """);

            var (current, saved) = BomPreset.Read(project);

            Assert.Equal(("Mine", "Value", false, "R", false, true, true), (current.Name, current.SortField, current.SortAscending,
                current.Filter, current.GroupSymbols, current.ExcludeDnp, current.IncludeExcludedFromBom));
            Assert.Equal(new BomField("MPN", "Part number", true, true), current.Fields[1]);
            Assert.Equal("Other", Assert.Single(saved).Name);
            Assert.False(saved[0].IncludeExcludedFromBom);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_project_that_names_no_preset_gets_the_default()
    {
        Assert.Same(BomPreset.DefaultEditing, BomPreset.Read(null).Current);
    }

    [Fact]
    public void Every_other_field_a_part_carries_is_a_hidden_column()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue);

        var mpn = Assert.Single(table.Columns, c => c.Name == "MPN");
        Assert.False(mpn.Show);
    }

    [Fact]
    public void The_filter_looks_at_designators()
    {
        var table = BomTable.Build(Parts(Sheet()), BomPreset.GroupedByValue, filter: "u");

        Assert.Equal("U1", Assert.Single(table.Rows).References[0]);
    }

    private static IReadOnlyList<BomPart> Parts(Schematic sheet) =>
        [.. sheet.Symbols.Select(s => new BomPart(s, s.ReferenceAt(Path0) ?? s.Reference ?? "?", "sheet.kicad_sch", Path0))];

    /// <summary>
    /// R1, R2 and R4 of 10k on two footprints, R3 of 10k not placed, a two-unit U1, a power symbol, and a test
    /// point kept off the bill. R1 carries an MPN.
    /// </summary>
    private static Schematic Sheet() => Schematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "\t(lib_symbols\n"
        + "\t\t(symbol \"power:GND\" (power) (property \"Reference\" \"#PWR\" (at 0 0 0)) (symbol \"GND_1_1\"))\n"
        + "\t\t(symbol \"Logic:Gate\" (property \"Reference\" \"U\" (at 0 0 0)) (symbol \"Gate_1_1\") (symbol \"Gate_2_1\")))\n"
        + Part(1, "Device:R", "R1", "10k", "R_0805", 1, extra: "(property \"MPN\" \"A\" (at 0 0 0) (hide yes))")
        + Part(2, "Device:R", "R2", "10k", "R_0603", 1)
        + Part(3, "Device:R", "R3", "10k", "R_0603", 1, flags: "(dnp yes)")
        + Part(4, "Device:R", "R4", "10k", "R_0603", 1)
        + Part(5, "Logic:Gate", "U1", "74HC00", "SOIC-14", 1)
        + Part(6, "Logic:Gate", "U1", "74HC00", "SOIC-14", 2)
        + Part(7, "power:GND", "#PWR01", "GND", string.Empty, 1)
        + Part(8, "Device:TP", "TP1", "TestPoint", string.Empty, 1, flags: "(in_bom no)")
        + "\t(embedded_fonts no))\n");

    private static string Part(int n, string lib, string reference, string value, string footprint, int unit, string flags = "", string extra = "") =>
        $"\t(symbol (lib_id \"{lib}\") (at {20 * n} 50 0) (unit {unit}) {flags} (uuid \"0a1b2c3d-0000-4000-8000-00000000000{n}\")\n"
        + $"\t\t(property \"Reference\" \"{reference}\" (at {20 * n} 45 90))\n"
        + $"\t\t(property \"Value\" \"{value}\" (at {20 * n} 55 0))\n"
        + $"\t\t(property \"Footprint\" \"{footprint}\" (at {20 * n} 50 0) (hide yes))\n"
        + $"\t\t{extra}\n"
        + $"\t\t(instances (project \"t\" (path \"{Path0}\" (reference \"{reference}\") (unit {unit})))))\n";
}
