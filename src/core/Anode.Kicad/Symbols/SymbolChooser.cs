namespace Anode.Kicad;

/// <summary>One line of the chooser: what would be placed, and enough to tell it from its neighbours.</summary>
/// <param name="LibId">What gets written into the sheet, e.g. <c>Device:R</c>.</param>
public sealed record SymbolChoice(string LibId, string Name, string Library, string? Description, LibSymbol Symbol);

/// <summary>
/// Choosing a part. This is the whole of the chooser except the window: a query, the parts that match it, and which
/// one is picked. Kept away from any window on purpose — a modal dialog cannot be driven in a headless test, and a
/// chooser nobody can test is a chooser nobody can trust.
/// </summary>
public sealed class SymbolChooser
{
    private readonly SymbolIndex _index;
    private string _query = string.Empty;
    private int _selected;

    public SymbolChooser(SymbolIndex index, int limit = 200)
    {
        _index = index;
        Limit = limit;
        Refresh();
    }

    /// <summary>How many lines are offered at most; a library of thousands must not be listed whole.</summary>
    public int Limit { get; }

    /// <summary>What was typed. Setting it finds again and puts the selection back at the top.</summary>
    public string Query
    {
        get => _query;
        set
        {
            string text = value ?? string.Empty;
            if (text == _query)
            {
                return;
            }

            _query = text;
            Refresh();
        }
    }

    public IReadOnlyList<SymbolChoice> Results { get; private set; } = [];

    /// <summary>Which line is picked; always a real line while there is one.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = Results.Count == 0 ? 0 : Math.Clamp(value, 0, Results.Count - 1);
    }

    public SymbolChoice? Selected => Results.Count == 0 ? null : Results[SelectedIndex];

    /// <summary>Moves the selection by <paramref name="delta"/> lines, stopping at either end.</summary>
    public void MoveSelection(int delta) => SelectedIndex = _selected + delta;

    /// <summary>Reads the libraries again — after one has been written to, or a table has changed.</summary>
    public void Refresh()
    {
        Results = [.. _index.Search(_query, Limit).Select(found => new SymbolChoice(
            found.LibId,
            found.Symbol.Name,
            Nickname(found.LibId),
            Description(found.Symbol),
            found.Symbol))];

        SelectedIndex = 0;
    }

    private static string Nickname(string libId)
    {
        int colon = libId.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? string.Empty : libId[..colon];
    }

    /// <summary>What the library says the part is for, when it says anything.</summary>
    private static string? Description(LibSymbol symbol) => symbol.Description;
}
