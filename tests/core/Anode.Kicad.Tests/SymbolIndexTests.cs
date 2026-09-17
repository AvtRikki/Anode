namespace Anode.Kicad.Tests;

/// <summary>
/// Every library a project can draw from, merged the way KiCad merges them. The rule worth writing down is the
/// precedence: a nickname in the project's own table wins over the same nickname installed globally, because a part
/// kept beside the project is the one meant to be used.
/// </summary>
public class SymbolIndexTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-libs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A library of one symbol, written where the tables can point at it.</summary>
    private string Library(string file, string symbol, string description = "")
    {
        string path = Path.Combine(_folder, file);
        File.WriteAllText(path, $"""
            (kicad_symbol_lib
            	(version 20250324)
            	(generator "anode")
            	(symbol "{symbol}"
            		(property "Description" "{description}"
            			(at 0 0 0)
            		)
            		(symbol "{symbol}_1_1"
            			(pin passive line
            				(at 0 3.81 270)
            				(length 1.27)
            				(name "~")
            				(number "1")
            			)
            		)
            	)
            )
            """);

        return path;
    }

    private static SymLibTable Table(params (string Nickname, string File)[] rows) => SymLibTable.Parse(
        "(sym_lib_table\n\t(version 7)\n"
        + string.Concat(rows.Select(r => $"\t(lib (name \"{r.Nickname}\")(type \"KiCad\")(uri \"${{LIBS}}/{r.File}\")(options \"\")(descr \"\"))\n"))
        + ")");

    private Dictionary<string, string> Variables => new() { ["LIBS"] = _folder };

    [Fact]
    public void Both_tables_are_offered_with_the_project_first()
    {
        Library("device.kicad_sym", "R");
        Library("local.kicad_sym", "R");

        var index = SymbolIndex.Build(Table(("Local", "local.kicad_sym")), Table(("Device", "device.kicad_sym")), Variables);

        Assert.Equal(["Local", "Device"], index.Libraries.Select(l => l.Nickname));
        Assert.True(index.Libraries[0].IsProject);
        Assert.False(index.Libraries[1].IsProject);
    }

    [Fact]
    public void A_nickname_the_project_also_claims_is_the_projects()
    {
        Library("mine.kicad_sym", "Mine");
        Library("theirs.kicad_sym", "Theirs");

        var index = SymbolIndex.Build(Table(("Device", "mine.kicad_sym")), Table(("Device", "theirs.kicad_sym")), Variables);

        var row = Assert.Single(index.Libraries);
        Assert.True(row.IsProject);
        Assert.NotNull(index.Find("Device:Mine"));
        Assert.Null(index.Find("Device:Theirs"));
    }

    [Fact]
    public void A_symbol_is_found_by_its_lib_id_and_by_name_alone()
    {
        Library("device.kicad_sym", "R");

        var index = SymbolIndex.Build(null, Table(("Device", "device.kicad_sym")), Variables);

        Assert.Equal("R", index.Find("Device:R")?.Name);
        Assert.Equal("R", index.Find("R")?.Name);
        Assert.Null(index.Find("Device:NotHere"));
        Assert.Null(index.Find("Missing:R"));
    }

    [Fact]
    public void A_library_that_cannot_be_read_is_remembered_rather_than_thrown()
    {
        Library("good.kicad_sym", "Good");

        var index = SymbolIndex.Build(null, Table(("Gone", "absent.kicad_sym"), ("Good", "good.kicad_sym")), Variables);

        // One unreadable library must not empty the chooser: the rest still answer.
        Assert.Equal("Good", index.Find("Good:Good")?.Name);

        var broken = index.Open("Gone");
        Assert.NotNull(broken);
        Assert.Null(broken!.Library);
        Assert.False(string.IsNullOrEmpty(broken.Problem));
    }

    [Fact]
    public void A_row_that_is_disabled_is_not_offered()
    {
        Library("off.kicad_sym", "Off");

        var table = SymLibTable.Parse(
            "(sym_lib_table\n\t(version 7)\n"
            + $"\t(lib (name \"Off\")(type \"KiCad\")(uri \"${{LIBS}}/off.kicad_sym\")(options \"\")(descr \"\")(disabled))\n"
            + ")");

        var index = SymbolIndex.Build(null, table, Variables);

        Assert.Empty(index.Libraries);
        Assert.Null(index.Find("Off:Off"));
    }

    [Fact]
    public void Searching_matches_the_name_the_library_and_the_description()
    {
        Library("device.kicad_sym", "R", "Resistor, general purpose");
        Library("conn.kicad_sym", "DB9");

        var index = SymbolIndex.Build(null, Table(("Device", "device.kicad_sym"), ("Conn", "conn.kicad_sym")), Variables);

        Assert.Equal(["Device:R"], index.Search("resistor").Select(r => r.LibId));
        Assert.Equal(["Conn:DB9"], index.Search("db9").Select(r => r.LibId));
        Assert.Equal(["Device:R"], index.Search("Device:").Select(r => r.LibId));

        // Nothing typed lists everything, which is what a chooser shows before the first keystroke.
        Assert.Equal(["Device:R", "Conn:DB9"], index.Search(string.Empty).Select(r => r.LibId));

        // And a search stops where it is told to.
        Assert.Single(index.Search(string.Empty, limit: 1));
    }

    [Fact]
    public void The_projects_own_folder_resolves_KIPRJMOD()
    {
        Library("parts.kicad_sym", "Part");

        var table = SymLibTable.Parse(
            "(sym_lib_table\n\t(version 7)\n"
            + "\t(lib (name \"Local\")(type \"KiCad\")(uri \"${KIPRJMOD}/parts.kicad_sym\")(options \"\")(descr \"\"))\n"
            + ")");

        var index = SymbolIndex.Build(table, null, variables: null, projectDirectory: _folder);

        Assert.Equal("Part", index.Find("Local:Part")?.Name);
    }
}
