namespace Anode.Kicad.Tests;

/// <summary>
/// Making a library and adding symbols to it: a new library is what KiCad 9 writes, a new symbol carries KiCad's
/// defaults, names that would not read back are refused, and taking a symbol out puts it back where it stood.
/// </summary>
public class SymbolLibraryEditingTests
{
    [Fact]
    public void A_new_library_is_empty_and_in_KiCad_9s_format()
    {
        var library = SymbolLibrary.Parse(SymbolLibrary.EmptyText);

        Assert.Empty(library.Symbols);
        Assert.Equal(SymbolLibrary.NewVersion, library.Version);
        Assert.Equal("anode", library.Generator);
    }

    /// <summary>
    /// KiCad's New Symbol: reference U and the name as value, both shown; footprint, datasheet and description
    /// there but hidden; one unit, one body, in the bill and on the board.
    /// </summary>
    [Fact]
    public void A_new_symbol_carries_KiCads_defaults()
    {
        var library = SymbolLibrary.Parse(SymbolLibrary.EmptyText);
        var symbol = SymbolLibrary.NewSymbol("OPA1612");
        library.Attach(symbol, int.MaxValue);

        var read = SymbolLibrary.Parse(library.Document.ToString()).Find("OPA1612")!;
        Assert.Equal("U", read.Reference);
        Assert.Equal("OPA1612", read.Value);
        Assert.Equal(["Reference", "Value", "Footprint", "Datasheet", "Description"], read.Fields.Select(f => f.Name));
        Assert.Equal([false, false, true, true, true], read.Fields.Select(f => f.IsHidden));
        Assert.Equal(1, read.UnitCount);
        Assert.False(read.HasAlternateBody);
        Assert.Equal(508_000, read.PinNameOffset);
        Assert.Contains("(in_bom yes)", library.Document.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("  ", "empty")]
    [InlineData("A:B", "characters")]
    [InlineData("A/B", "characters")]
    [InlineData("R", "taken")]
    [InlineData("R_Small", null)]
    public void Names_that_would_not_read_back_are_refused(string name, string? problem)
    {
        var library = SymbolLibrary.Parse(SymbolLibrary.EmptyText);
        library.Attach(SymbolLibrary.NewSymbol("R"), int.MaxValue);

        Assert.Equal(problem, library.NameProblem(name));
    }

    [Fact]
    public void A_symbol_taken_out_goes_back_where_it_stood()
    {
        var library = SymbolLibrary.Parse(SymbolLibrary.EmptyText);
        foreach (string name in new[] { "A", "B", "C" })
        {
            library.Attach(SymbolLibrary.NewSymbol(name), int.MaxValue);
        }

        byte[] before = library.Document.ToBytes();
        var b = library.Find("B")!;

        int at = library.Detach(b);
        Assert.Equal(["A", "C"], library.Symbols.Select(s => s.Name));
        Assert.Null(library.Find("B"));

        library.Attach(b, at);
        Assert.Equal(["A", "B", "C"], library.Symbols.Select(s => s.Name));
        Assert.Same(b, library.Find("B"));
        Assert.Equal(before, library.Document.ToBytes());
    }
}
