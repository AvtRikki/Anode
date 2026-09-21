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

    /// <summary>
    /// Numbers the parts again from scratch, in the order KiCad numbers them: within a prefix, left to right and
    /// then top to bottom (<c>SORT_BY_X_POSITION</c>, its default). Where <see cref="Annotate"/> fills in what is
    /// missing and leaves the rest alone, this renames parts that already carry a number — which is what a design
    /// numbered in the order it happened to be drawn needs, and what nothing else should do by accident.
    ///
    /// The sections of one part go on sharing a designator: they are grouped by the one they carry now, so a quad
    /// gate stays one part rather than becoming four. What the parts hold is given back to <paramref name="taken"/>
    /// first, so they are numbered from one again rather than past themselves — but nothing else in the design is,
    /// so a number another sheet uses is still not offered here.
    /// </summary>
    public static IReadOnlyList<(SymbolInstance Symbol, string Reference)> Renumber(
        IEnumerable<SymbolInstance> symbols,
        string? sheetPath,
        DesignNumbers taken)
    {
        var all = symbols.ToList();
        foreach (var symbol in all)
        {
            if (symbol.ReferenceAt(sheetPath) is { Length: > 0 } reference)
            {
                taken.Forget(reference);
            }
        }

        // A part drawn in sections carries one designator across them; parts still waiting for one stand alone,
        // since nothing in the file says which of them belong together.
        var parts = all
            .Select(symbol => (Symbol: symbol, Reference: symbol.ReferenceAt(sheetPath) ?? string.Empty))
            .GroupBy(p => IsUnannotated(p.Reference) ? $"?{p.Symbol.Uuid}" : p.Reference, StringComparer.Ordinal)
            .Select(group => new
            {
                Prefix = PrefixOf(group.First().Reference is { Length: > 0 } written && !IsUnannotated(written)
                    ? written
                    : group.First().Symbol.Definition?.Reference ?? group.First().Reference),
                Symbols = group.Select(p => p.Symbol).ToList(),
            })
            .OrderBy(part => part.Prefix, StringComparer.Ordinal)
            .ThenBy(part => part.Symbols.Min(s => s.Position.X))
            .ThenBy(part => part.Symbols.Min(s => s.Position.Y))
            .ThenBy(part => part.Symbols.Min(s => s.Uuid), StringComparer.Ordinal)
            .ToList();

        var given = new List<(SymbolInstance, string)>();
        foreach (var part in parts)
        {
            string reference = part.Prefix + taken.TakeFirstFree(part.Prefix).ToString(CultureInfo.InvariantCulture);
            foreach (var symbol in part.Symbols)
            {
                given.Add((symbol, reference));
            }
        }

        return given;
    }

    /// <summary>Everything on the sheet that is still waiting for a number.</summary>
    public static IReadOnlyList<SymbolInstance> Unannotated(Schematic sheet, string? sheetPath = null) =>
        [.. sheet.Symbols.Where(s => IsUnannotated(s.ReferenceAt(sheetPath)))];
}
