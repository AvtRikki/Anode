using System.Globalization;

namespace Anode.Kicad;

/// <summary>
/// What a bus is made of. A bus carries several nets at once and says which in its own name: a vector bus spells
/// them out as a range, <c>DQ[0..15]</c>; a group bus lists them in braces, <c>PREFIX{A B}</c>, where a member may
/// itself be a vector or a name the sheet declared with <c>(bus_alias "DPHY" (members …))</c>, and each member of a
/// named group is called <c>PREFIX.MEMBER</c>.
///
/// A name that is neither is not a bus at all, and stands for the single net it names. The name is read as KiCad
/// shows it, escapes undone first: <c>VPP{slash}MCLR</c> is one ordinary net whose name holds a slash, not a group.
/// </summary>
public static class SchBusNames
{
    /// <summary>
    /// Whether this name stands for nets carried together rather than for one net. A group of a single member is
    /// still a bus — the member is named for the group, and the alias naming it may live on another sheet.
    /// </summary>
    public static bool IsBus(string? name, IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases = null)
    {
        if (name is not { Length: > 0 })
        {
            return false;
        }

        string shown = KicadText.Unescape(name);
        return aliases?.ContainsKey(shown) == true || Group(shown, aliases) is not null || Vector(shown) is not null;
    }

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

        string shown = KicadText.Unescape(name);

        // A declared group is what the sheet says it is, whatever the name looks like.
        if (aliases?.TryGetValue(shown, out var declared) == true)
        {
            return declared;
        }

        return Group(shown, aliases) ?? Vector(shown) ?? [shown];
    }

    /// <summary>
    /// <c>prefix{A B C}</c> spelled out: each member in its own right — an alias stands for its members, a vector
    /// for its range — and, when the group is named, each carrying the prefix, as <c>DPHY0_P.D0_N</c>.
    /// </summary>
    private static IReadOnlyList<string>? Group(string name, IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases)
    {
        if (!name.EndsWith('}'))
        {
            return null;
        }

        // The brace that opens the member list is the one that is not part of the text's own markup.
        int open = -1;
        for (int i = 0; i < name.Length - 1; i++)
        {
            if (name[i] == '{' && (i == 0 || name[i - 1] is not ('^' or '_' or '~' or '$')))
            {
                open = i;
                break;
            }
        }

        if (open < 0)
        {
            return null;
        }

        string prefix = name[..open];
        var members = new List<string>();
        foreach (string part in name[(open + 1)..^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string member in Members(part, aliases))
            {
                members.Add(prefix.Length > 0 ? $"{prefix}.{member}" : member);
            }
        }

        return members.Count > 0 ? members : null;
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
