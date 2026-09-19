using System.Globalization;

namespace Anode.Kicad.Editing;

/// <summary>
/// Giving placed parts their numbers. A part arrives from a library called "R?" — the prefix its library gives it,
/// with a question mark where the number will go — and stays that way until something numbers it.
///
/// The rule is KiCad's: numbers run per prefix, and a number already taken on the sheet is never handed out again,
/// whether it was placed a moment ago or has been in the file for years.
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
    /// The next free number for <paramref name="prefix"/> on this sheet, counting every designator already there.
    /// </summary>
    /// <param name="sheetPath">The appearance of the sheet being numbered; a reused sheet names its parts per appearance.</param>
    public static int NextNumber(Schematic sheet, string prefix, string? sheetPath = null)
    {
        int highest = 0;
        foreach (var symbol in sheet.Symbols)
        {
            if (symbol.ReferenceAt(sheetPath) is not { Length: > 0 } reference
                || !string.Equals(PrefixOf(reference), prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string digits = new([.. reference.SkipWhile(c => !char.IsAsciiDigit(c))]);
            if (digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// Numbers the parts that are waiting for it, in the order given, and answers what each was called. Parts that
    /// already carry a number are left exactly as they are: annotation must never renumber a design behind the
    /// designer's back.
    /// </summary>
    public static IReadOnlyList<(SymbolInstance Symbol, string Reference)> Annotate(
        Schematic sheet,
        IEnumerable<SymbolInstance> symbols,
        string? sheetPath = null)
    {
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        var given = new List<(SymbolInstance, string)>();

        foreach (var symbol in symbols)
        {
            if (!IsUnannotated(symbol.ReferenceAt(sheetPath)))
            {
                continue;
            }

            string prefix = PrefixOf(symbol.ReferenceAt(sheetPath));
            if (!next.TryGetValue(prefix, out int number))
            {
                number = NextNumber(sheet, prefix, sheetPath);
            }

            string reference = prefix + number.ToString(CultureInfo.InvariantCulture);
            next[prefix] = number + 1;
            given.Add((symbol, reference));
        }

        return given;
    }

    /// <summary>Everything on the sheet that is still waiting for a number.</summary>
    public static IReadOnlyList<SymbolInstance> Unannotated(Schematic sheet, string? sheetPath = null) =>
        [.. sheet.Symbols.Where(s => IsUnannotated(s.ReferenceAt(sheetPath)))];
}
