using System.Globalization;

namespace Anode.Kicad.Editing;

/// <summary>
/// The designator numbers a design has already used, per prefix — every R, every U, wherever it stands.
///
/// It exists because a number must be unique across a whole design, not across one sheet: two sheets numbered apart
/// would each hand out R1, and a sheet placed twice would give both of its copies the same designator. So the set is
/// gathered from every place of every sheet, and a number handed out is remembered, which is what keeps a row of
/// parts laid down one after another from all taking the same number.
///
/// The rule for choosing is KiCad's: the first number nobody has, counting up from one — gaps are filled rather than
/// stepped over (<c>REFDES_TRACKER::GetNextRefDesForUnits</c>).
/// </summary>
public sealed class DesignNumbers
{
    private readonly Dictionary<string, HashSet<int>> _taken = new(StringComparer.Ordinal);

    private DesignNumbers()
    {
    }

    /// <summary>What one sheet has used, as one place of it — for a design that is a single file.</summary>
    public static DesignNumbers Of(Schematic sheet, string? sheetPath = null)
    {
        var numbers = new DesignNumbers();
        numbers.Read(sheet, sheetPath);
        return numbers;
    }

    /// <summary>
    /// What a whole design has used: every place of every sheet, as the hierarchy is walked from its root. A sheet
    /// that cannot be read contributes nothing rather than stopping the count — a design half of which is missing is
    /// still one to place parts on, and the numbers of the sheets that do read are still better than one sheet's.
    /// </summary>
    public static DesignNumbers Of(
        string rootFile,
        Func<string, Schematic?>? open = null,
        CancellationToken cancellationToken = default)
    {
        var numbers = new DesignNumbers();
        var read = new Dictionary<string, Schematic?>(StringComparer.Ordinal);

        foreach (var place in SchHierarchy.Walk(rootFile, open, cancellationToken: cancellationToken))
        {
            if (!read.TryGetValue(place.File, out var sheet))
            {
                read[place.File] = sheet = Read(place.File, open);
            }

            if (sheet is not null)
            {
                numbers.Read(sheet, place.Path);
            }
        }

        return numbers;
    }

    /// <summary>Whether that designator is spoken for.</summary>
    public bool IsTaken(string prefix, int number) =>
        _taken.TryGetValue(prefix, out var used) && used.Contains(number);

    /// <summary>The first number for this prefix that nobody has, counting up from <paramref name="from"/>.</summary>
    public int FirstFree(string prefix, int from = 1)
    {
        int candidate = Math.Max(from, 1);
        while (IsTaken(prefix, candidate))
        {
            candidate++;
        }

        return candidate;
    }

    /// <summary>The first free number, marked as used so that the next call gives the one after it.</summary>
    public int TakeFirstFree(string prefix, int from = 1)
    {
        int number = FirstFree(prefix, from);
        Take(prefix, number);
        return number;
    }

    /// <summary>Marks a number as used, whether it was handed out here or read from a file.</summary>
    public void Take(string prefix, int number)
    {
        if (!_taken.TryGetValue(prefix, out var used))
        {
            _taken[prefix] = used = [];
        }

        used.Add(number);
    }

    /// <summary>Reads what a sheet uses in one of its places, adding it to what is already known.</summary>
    public void Read(Schematic sheet, string? sheetPath = null)
    {
        foreach (var symbol in sheet.Symbols)
        {
            if (symbol.ReferenceAt(sheetPath) is { Length: > 0 } reference && NumberOf(reference) is { } number)
            {
                Take(SchAnnotation.PrefixOf(reference), number);
            }
        }
    }

    /// <summary>The number in a designator — the 12 of "R12" — or none, for one still waiting.</summary>
    private static int? NumberOf(string reference)
    {
        int start = reference.Length;
        while (start > 0 && char.IsAsciiDigit(reference[start - 1]))
        {
            start--;
        }

        return start < reference.Length
            && int.TryParse(reference.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    private static Schematic? Read(string file, Func<string, Schematic?>? open)
    {
        try
        {
            return open?.Invoke(file) ?? (File.Exists(file) ? Schematic.Load(file) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
            or Sexpr.SexprParseException or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }
}
