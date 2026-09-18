using System.Globalization;

namespace Anode.Kicad;

/// <summary>
/// What a bus is made of. A bus carries several nets at once and says which in its own name: a vector bus spells
/// them out as a range, <c>DQ[0..15]</c>, while a group bus is a name the sheet declares elsewhere with
/// <c>(bus_alias "DPHY" (members …))</c>.
///
/// A name that is neither is not a bus at all, and stands for the single net it names. That matters more than it
/// sounds: KiCad escapes awkward characters in braces, so <c>VPP{slash}MCLR</c> is one ordinary net whose name
/// contains a slash — reading braces as a group would tear it into members that never existed.
/// </summary>
public static class SchBusNames
{
    /// <summary>Whether this name stands for several nets rather than one.</summary>
    public static bool IsBus(string? name, IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases = null) =>
        Members(name, aliases).Count > 1 || (name is { Length: > 0 } && aliases?.ContainsKey(name) == true);

    /// <summary>
    /// The nets a name carries: the members of a group bus, the range of a vector bus, or the name itself when it
    /// is an ordinary net.
    /// </summary>
    public static IReadOnlyList<string> Members(
        string? name,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases = null)
    {
        if (name is not { Length: > 0 })
        {
            return [];
        }

        // A declared group is what the sheet says it is, whatever the name looks like.
        if (aliases?.TryGetValue(name, out var declared) == true)
        {
            return declared;
        }

        return Vector(name) ?? [name];
    }

    /// <summary>
    /// <c>prefix[first..last]</c> spelled out. The prefix may hold punctuation — <c>BE-[0..3]</c> is real — and the
    /// range need not start at zero, as <c>ADR[2..6]</c> does not.
    /// </summary>
    private static IReadOnlyList<string>? Vector(string name)
    {
        // Braces mean an escaped character, not a group; a name carrying one is left whole.
        if (name.Contains('{', StringComparison.Ordinal) || !name.EndsWith(']'))
        {
            return null;
        }

        int open = name.LastIndexOf('[');
        if (open <= 0)
        {
            return null;
        }

        string prefix = name[..open];
        string range = name[(open + 1)..^1];

        int dots = range.IndexOf("..", StringComparison.Ordinal);
        if (dots <= 0)
        {
            return null;
        }

        if (!int.TryParse(range[..dots], NumberStyles.None, CultureInfo.InvariantCulture, out int first)
            || !int.TryParse(range[(dots + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out int last))
        {
            return null;
        }

        var members = new List<string>(Math.Abs(last - first) + 1);
        int step = last >= first ? 1 : -1;
        for (int i = first; ; i += step)
        {
            members.Add(prefix + i.ToString(CultureInfo.InvariantCulture));
            if (i == last)
            {
                break;
            }
        }

        return members;
    }
}
