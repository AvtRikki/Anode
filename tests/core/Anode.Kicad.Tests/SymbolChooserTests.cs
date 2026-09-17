namespace Anode.Kicad.Tests;

/// <summary>
/// Choosing a part, without the window it will eventually wear. A modal dialog cannot be driven in a headless test,
/// so everything that decides what the chooser shows and what it would place lives here, where it can be asked.
/// </summary>
public class SymbolChooserTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-chooser-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Library(string file, params (string Name, string Description)[] symbols)
    {
        string body = string.Concat(symbols.Select(s => $"""
        	(symbol "{s.Name}"
        		(property "Description" "{s.Description}"
        			(at 0 0 0)
        		)
        	)

        """));

        File.WriteAllText(Path.Combine(_folder, file), $"(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n{body})");
    }

    private SymbolChooser Chooser(int limit = 200)
    {
        var table = SymLibTable.Parse(
            "(sym_lib_table\n\t(version 7)\n"
            + string.Concat(Directory.EnumerateFiles(_folder, "*.kicad_sym").Order(StringComparer.Ordinal)
                .Select(f => $"\t(lib (name \"{Path.GetFileNameWithoutExtension(f)}\")(type \"KiCad\")(uri \"${{KIPRJMOD}}/{Path.GetFileName(f)}\")(options \"\")(descr \"\"))\n"))
            + ")");

        return new SymbolChooser(SymbolIndex.Build(table, null, null, _folder), limit);
    }

    [Fact]
    public void Before_anything_is_typed_everything_is_offered()
    {
        Library("device.kicad_sym", ("R", "Resistor"), ("C", "Capacitor"));

        var chooser = Chooser();

        Assert.Equal(["device:R", "device:C"], chooser.Results.Select(r => r.LibId));
        Assert.Equal("device", chooser.Results[0].Library);
        Assert.Equal("Resistor", chooser.Results[0].Description);
        Assert.Equal("device:R", chooser.Selected?.LibId);
    }

    [Fact]
    public void Typing_narrows_by_name_by_library_and_by_what_the_part_is_for()
    {
        Library("device.kicad_sym", ("R", "Resistor"), ("C", "Capacitor"));
        Library("conn.kicad_sym", ("DB9", "Serial connector"));

        var chooser = Chooser();

        chooser.Query = "capacitor";
        Assert.Equal(["device:C"], chooser.Results.Select(r => r.LibId));

        chooser.Query = "conn:";
        Assert.Equal(["conn:DB9"], chooser.Results.Select(r => r.LibId));

        chooser.Query = "db9";
        Assert.Equal(["conn:DB9"], chooser.Results.Select(r => r.LibId));

        // Nothing matches: the chooser says so by offering nothing, and picks nothing.
        chooser.Query = "nonsense";
        Assert.Empty(chooser.Results);
        Assert.Null(chooser.Selected);
    }

    [Fact]
    public void A_new_query_puts_the_selection_back_at_the_top()
    {
        Library("device.kicad_sym", ("R", "Resistor"), ("C", "Capacitor"));

        var chooser = Chooser();
        chooser.MoveSelection(1);
        Assert.Equal("device:C", chooser.Selected?.LibId);

        chooser.Query = "device";
        Assert.Equal(0, chooser.SelectedIndex);
    }

    [Fact]
    public void The_selection_stops_at_either_end()
    {
        Library("device.kicad_sym", ("R", "Resistor"), ("C", "Capacitor"));

        var chooser = Chooser();

        chooser.MoveSelection(-5);
        Assert.Equal(0, chooser.SelectedIndex);

        chooser.MoveSelection(99);
        Assert.Equal(1, chooser.SelectedIndex);
    }

    [Fact]
    public void A_library_of_many_parts_is_not_listed_whole()
    {
        Library("big.kicad_sym", [.. Enumerable.Range(0, 30).Select(i => ($"R{i}", "Resistor"))]);

        var chooser = Chooser(limit: 10);

        Assert.Equal(10, chooser.Results.Count);
    }

    [Fact]
    public void The_chosen_line_carries_the_definition_that_would_be_placed()
    {
        Library("device.kicad_sym", ("R", "Resistor"));

        var chooser = Chooser();

        // Enough to place with: the lib_id the sheet will write, and the body it will copy in.
        Assert.Equal("device:R", chooser.Selected?.LibId);
        Assert.Equal("R", chooser.Selected?.Symbol.Name);
    }
}
