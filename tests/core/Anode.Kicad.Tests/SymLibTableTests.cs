namespace Anode.Kicad.Tests;

/// <summary>
/// Reading a library table. KiCad keeps no such file in the demos, so the fixtures are written here — which is
/// honest enough, the format being four fields and a nickname.
/// </summary>
public class SymLibTableTests
{
    private const string Table = """
        (sym_lib_table
        	(version 7)
        	(lib (name "Device")(type "KiCad")(uri "${KICAD9_SYMBOL_DIR}/Device.kicad_sym")(options "")(descr "Basic parts"))
        	(lib (name "Local")(type "KiCad")(uri "${KIPRJMOD}/parts.kicad_sym")(options "")(descr ""))
        	(lib (name "Old")(type "KiCad")(uri "/opt/legacy.kicad_sym")(options "")(descr "")(disabled))
        )
        """;

    [Fact]
    public void The_rows_are_read_in_the_order_they_are_searched()
    {
        var table = SymLibTable.Parse(Table);

        Assert.Equal(7, table.Version);
        Assert.Equal(["Device", "Local", "Old"], table.Entries.Select(e => e.Name));
        Assert.Equal("Basic parts", table.Entries[0].Description);
        Assert.Equal("KiCad", table.Entries[0].Type);
    }

    [Fact]
    public void A_row_is_found_by_its_nickname()
    {
        var table = SymLibTable.Parse(Table);

        Assert.Equal("${KIPRJMOD}/parts.kicad_sym", table.Find("Local")?.Uri);
        Assert.Null(table.Find("device"));
    }

    [Fact]
    public void A_row_kept_but_not_loaded_says_so()
    {
        var table = SymLibTable.Parse(Table);

        Assert.True(table.Find("Old")!.IsDisabled);
        Assert.False(table.Find("Device")!.IsDisabled);
    }

    [Fact]
    public void A_uri_is_resolved_from_the_variables_it_names()
    {
        var table = SymLibTable.Parse(Table);
        var variables = new Dictionary<string, string>
        {
            ["KICAD9_SYMBOL_DIR"] = "/usr/share/kicad/symbols",
            ["KIPRJMOD"] = "/home/a/project",
        };

        Assert.Equal("/usr/share/kicad/symbols/Device.kicad_sym", table.Find("Device")!.Resolve(variables));
        Assert.Equal("/home/a/project/parts.kicad_sym", table.Find("Local")!.Resolve(variables));

        // A path with no variable in it is its own answer.
        Assert.Equal("/opt/legacy.kicad_sym", table.Find("Old")!.Resolve(variables));
    }

    [Fact]
    public void A_variable_nobody_knows_is_left_standing()
    {
        var table = SymLibTable.Parse(Table);

        // Better a path that names what is missing than a blank one, or a guess.
        Assert.Equal("${KICAD9_SYMBOL_DIR}/Device.kicad_sym", table.Find("Device")!.Resolve(new Dictionary<string, string>()));
    }

    [Fact]
    public void A_table_reads_and_writes_back_byte_for_byte()
    {
        var table = SymLibTable.Parse(Table);

        Assert.Equal(Table, System.Text.Encoding.UTF8.GetString(table.Document.ToBytes()));
    }

    [Fact]
    public void Something_that_is_not_a_table_is_refused()
    {
        Assert.Throws<KiCadFormatException>(() => SymLibTable.Parse("(kicad_sch (version 20250324))"));
    }
}
