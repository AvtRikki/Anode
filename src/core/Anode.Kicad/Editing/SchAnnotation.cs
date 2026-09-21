using System.Globalization;

namespace Anode.Kicad.Editing;

/// <summary>
/// Giving placed parts their numbers. A part arrives from a library called "R?" — the prefix its library gives it,
/// with a question mark where the number will go — and stays that way until something numbers it.
///
/// The rule is KiCad's: numbers run per prefix, a part takes the first number nobody has, and a number already used
/// is never handed out again — whether it was placed a moment ago or has been in the file for years. What counts as
/// used is the whole design, not one sheet (<see cref="DesignNumbers"/>): two sheets numbered apart would each hand
/// out R1, and a sheet placed twice would call both of its copies the same thing.
/// </summary>
public static class SchAnnotation
{
    /// <summary>Whether this designator is still waiting for its number: "R?", "U?", or nothing at all.</summary>
    public static bool IsUnannotated(string? reference) =>
        string.IsNullOrEmpty(reference) || reference.EndsWith('?');

    /// <summary>The prefix of a designator: the "R" of "R12", the "U" of "U?".</summary>
    public static string PrefixOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return "U";
        }

        int end = reference.Length;
        while (end > 0 && char.IsAsciiDigit(reference[end - 1]))
        {
            end--;
        }

        string prefix = reference[..end].TrimEnd('?');
        return prefix.Length > 0 ? prefix : "U";
    }

    /// <summary>
    /// The first number for <paramref name="prefix"/> that this sheet does not use. For a design of several sheets,
    /// gather the numbers with <see cref="DesignNumbers.Of(string, Func{string, Schematic}, CancellationToken)"/>
    /// instead: one sheet's numbers say nothing about its neighbours'.
    /// </summary>
    /// <param name="sheetPath">The appearance of the sheet being numbered; a reused sheet names its parts per appearance.</param>
    public static int NextNumber(Schematic sheet, string prefix, string? sheetPath = null) =>
        DesignNumbers.Of(sheet, sheetPath).FirstFree(prefix);

    /// <summary>
    /// Numbers the parts that are waiting for it, in the order given, and answers what each was called. Parts that
    /// already carry a number are left exactly as they are: annotation must never renumber a design behind the
    /// designer's back.
    /// </summary>
    public static IReadOnlyList<(SymbolInstance Symbol, string Reference)> Annotate(
        Schematic sheet,
        IEnumerable<SymbolInstance> symbols,
        string? sheetPath = null,
        DesignNumbers? taken = null)
    {
        var numbers = taken ?? DesignNumbers.Of(sheet, sheetPath);
        var given = new List<(SymbolInstance, string)>();

        foreach (var symbol in symbols)
        {
            if (!IsUnannotated(symbol.ReferenceAt(sheetPath)))
            {
                continue;
            }

            string prefix = PrefixOf(symbol.ReferenceAt(sheetPath));
            given.Add((symbol, prefix + numbers.TakeFirstFree(prefix).ToString(CultureInfo.InvariantCulture)));
        }

        return given;
    }

    /// <summary>Everything on the sheet that is still waiting for a number.</summary>
    public static IReadOnlyList<SymbolInstance> Unannotated(Schematic sheet, string? sheetPath = null) =>
        [.. sheet.Symbols.Where(s => IsUnannotated(s.ReferenceAt(sheetPath)))];
}
