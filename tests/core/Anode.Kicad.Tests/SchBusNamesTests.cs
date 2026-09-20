namespace Anode.Kicad.Tests;

/// <summary>
/// What a bus name stands for. Every shape here was taken from the demo sheets rather than imagined, including the
/// one that looks like a group and is not.
/// </summary>
public class SchBusNamesTests
{
    [Fact]
    public void A_vector_bus_spells_out_its_range()
    {
        Assert.Equal(16, SchBusNames.Members("DQ[0..15]").Count);
        Assert.Equal(["DQ0", "DQ1"], SchBusNames.Members("DQ[0..1]"));
    }

    [Fact]
    public void A_range_need_not_start_at_zero()
    {
        // ADR[2..6] is in the demos exactly as written.
        Assert.Equal(["ADR2", "ADR3", "ADR4", "ADR5", "ADR6"], SchBusNames.Members("ADR[2..6]"));
    }

    [Fact]
    public void A_prefix_may_carry_punctuation()
    {
        // BE-[0..3] and PTBE-[0..3] are both in the demos.
        Assert.Equal(["BE-0", "BE-1", "BE-2", "BE-3"], SchBusNames.Members("BE-[0..3]"));
    }

    [Fact]
    public void A_range_written_backwards_is_read_in_the_order_it_is_written()
    {
        Assert.Equal(["D3", "D2", "D1", "D0"], SchBusNames.Members("D[3..0]"));
    }

    [Fact]
    public void A_declared_group_is_what_the_sheet_says_it_is()
    {
        var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["DPHY"] = ["D0_N", "D0_P", "C_N", "C_P"],
        };

        Assert.Equal(["D0_N", "D0_P", "C_N", "C_P"], SchBusNames.Members("DPHY", aliases));
        Assert.True(SchBusNames.IsBus("DPHY", aliases));

        // Without the declaration it is simply a net called DPHY.
        Assert.Equal(["DPHY"], SchBusNames.Members("DPHY"));
        Assert.False(SchBusNames.IsBus("DPHY"));
    }

    [Fact]
    public void Braces_are_an_escaped_character_and_not_a_group()
    {
        // VPP{slash}MCLR is one net in the demos whose name holds a slash: the braces are KiCad's escape, and the
        // net is that one name with the slash put back. Reading them as a group would tear it into members that
        // never existed.
        Assert.Equal(["VPP/MCLR"], SchBusNames.Members("VPP{slash}MCLR"));
        Assert.False(SchBusNames.IsBus("VPP{slash}MCLR"));
    }

    [Theory]
    [InlineData("SDA")]
    [InlineData("+5V")]
    [InlineData("D[1..]")]
    [InlineData("D[..3]")]
    [InlineData("D[a..b]")]
    [InlineData("[0..3]")]
    public void Anything_else_stands_for_the_one_net_it_names(string name)
    {
        Assert.Equal([name], SchBusNames.Members(name));
        Assert.False(SchBusNames.IsBus(name));
    }

    [Fact]
    public void A_name_that_is_nothing_carries_nothing()
    {
        Assert.Empty(SchBusNames.Members(null));
        Assert.Empty(SchBusNames.Members(string.Empty));
    }

    [Fact]
    public void The_sheet_reads_the_groups_it_declares()
    {
        var sheet = Schematic.Parse("""
            (kicad_sch
            	(version 20260206)
            	(generator "anode")
            	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
            	(paper "A4")
            	(lib_symbols)
            	(bus_alias "DPHY"
            		(members "D0_N" "D0_P" "C_N"
            			"C_P"
            		)
            	)
            	(sheet_instances
            		(path "/"
            			(page "1")
            		)
            	)
            	(embedded_fonts no)
            )
            """);

        // The member list wraps across lines in the demos, so it must be read as atoms rather than as one line.
        Assert.Equal(["D0_N", "D0_P", "C_N", "C_P"], sheet.BusAliases["DPHY"]);
    }

    [Theory]
    [InlineData("MEM{A B}", "MEM.A", "MEM.B")]
    [InlineData("{A B}", "A", "B")]
    [InlineData("P{D[0..1]}", "P.D0", "P.D1")]
    public void A_group_spells_out_its_members_with_the_groups_own_name(string bus, params string[] members)
    {
        Assert.Equal(members, SchBusNames.Members(bus));
        Assert.True(SchBusNames.IsBus(bus));
    }

    [Fact]
    public void A_group_takes_the_members_of_an_alias_it_names()
    {
        var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["DPHY"] = ["C_N", "C_P"] };

        Assert.Equal(["DPHY0.C_N", "DPHY0.C_P"], SchBusNames.Members("DPHY0{DPHY}", aliases));
    }

    [Fact]
    public void A_group_of_one_unknown_member_is_still_a_bus()
    {
        // The alias may be declared on another sheet; the member is named for the group all the same.
        Assert.True(SchBusNames.IsBus("ETH_PI{ETH}"));
        Assert.Equal(["ETH_PI.ETH"], SchBusNames.Members("ETH_PI{ETH}"));
    }

}
