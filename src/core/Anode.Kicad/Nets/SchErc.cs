namespace Anode.Kicad;

/// <summary>How badly a rule is broken: KiCad's own two levels.</summary>
public enum ErcSeverity
{
    Warning,
    Error,
}

/// <summary>What was found, where, and which pins it is about.</summary>
/// <param name="Net">The net the finding is on.</param>
/// <param name="Pins">The pins it names — two for a conflict, one for a net nothing drives.</param>
public sealed record ErcFinding(ErcKind Kind, ErcSeverity Severity, DesignNet Net, IReadOnlyList<DesignPin> Pins);

public enum ErcKind
{
    /// <summary>Two pins that may not be wired together — two outputs, an output and a supply.</summary>
    PinConflict,

    /// <summary>A pin that waits to be driven, on a net where nothing drives it.</summary>
    NotDriven,

    /// <summary>A power input on a net no supply drives: KiCad's missing power flag.</summary>
    PowerNotDriven,
}

/// <summary>
/// The electrical rules KiCad checks between the pins of a net, ported from its own tables
/// (<c>erc_settings.cpp</c>, <c>erc.cpp</c>):
///
/// - every pair of pins on a net is looked up in the matrix of pin types; a pair may be fine, doubtful or wrong;
/// - a net carrying a pin that waits to be driven — an input, a power input — and nothing that drives it is
///   reported, unless somebody marked it no-connect. A net with a power input is a power net, and only a power
///   output drives one; that is the rule behind KiCad's "missing power flag".
/// </summary>
public static class SchErc
{
    private const string Input = "input";
    private const string Output = "output";
    private const string Bidirectional = "bidirectional";
    private const string TriState = "tri_state";
    private const string Passive = "passive";
    private const string Free = "free";
    private const string Unspecified = "unspecified";
    private const string PowerIn = "power_in";
    private const string PowerOut = "power_out";
    private const string OpenCollector = "open_collector";
    private const string OpenEmitter = "open_emitter";
    private const string NoConnect = "no_connect";

    /// <summary>The order of KiCad's matrix: the columns are the same types in the same order.</summary>
    private static readonly string[] Types =
        [Input, Output, Bidirectional, TriState, Passive, Free, Unspecified, PowerIn, PowerOut, OpenCollector, OpenEmitter, NoConnect];

    /// <summary>What drives an ordinary pin.</summary>
    private static readonly HashSet<string> Driving = new(StringComparer.Ordinal) { Output, PowerOut, Passive, TriState, Bidirectional };

    /// <summary>What drives a power input: only a power output.</summary>
    private static readonly HashSet<string> DrivingPower = new(StringComparer.Ordinal) { PowerOut };

    /// <summary>What waits to be driven.</summary>
    private static readonly HashSet<string> Driven = new(StringComparer.Ordinal) { Input, PowerIn };

    /// <summary>
    /// KiCad's default pin matrix, row by row as it writes it: <c>.</c> the pins may meet, <c>w</c> doubtful,
    /// <c>e</c> wrong. Rows and columns are <see cref="Types"/>, in that order.
    /// </summary>
    private static readonly string[] Matrix =
    [
        /* input          */ "......w....e",
        /* output         */ ".e.w..w.eeee",
        /* bidirectional  */ "......w.w.we",
        /* tri_state      */ ".w....wwewwe",
        /* passive        */ "......w....e",
        /* free           */ "...........e",
        /* unspecified    */ "wwwww.wwwwwe",
        /* power_in       */ "...w..w....e",
        /* power_out      */ ".ewe..w.eeee",
        /* open_collector */ ".e.w..w.e..e",
        /* open_emitter   */ ".eww..w.e..e",
        /* no_connect     */ "eeeeeeeeeeee",
    ];

    /// <summary>Every rule broken in <paramref name="nets"/>, in the order the nets come.</summary>
    public static IReadOnlyList<ErcFinding> Check(IEnumerable<DesignNet> nets)
    {
        var findings = new List<ErcFinding>();

        foreach (var net in nets)
        {
            // A pin found twice on a net — a part's shared pin drawn in two units — is one pin.
            var pins = net.Pins
                .GroupBy(p => (p.Reference, p.Pin.Number))
                .Select(g => g.First())
                .OrderBy(p => p.Reference, StringComparer.Ordinal)
                .ThenBy(p => p.Pin.Number, StringComparer.Ordinal)
                .ToList();

            if (pins.Count == 0)
            {
                continue;
            }

            bool power = pins.Any(p => Type(p) == PowerIn);
            bool driven = pins.Any(p => (power ? DrivingPower : Driving).Contains(Type(p)));
            bool marked = net.Parts.Any(part => part.Net.IsNoConnect);

            // Every pair that may not meet; what to show of them is the caller's to decide.
            for (int i = 0; i < pins.Count; i++)
            {
                for (int j = i + 1; j < pins.Count; j++)
                {
                    if (Conflict(Type(pins[i]), Type(pins[j])) is { } severity)
                    {
                        findings.Add(new ErcFinding(ErcKind.PinConflict, severity, net, [pins[i], pins[j]]));
                    }
                }
            }

            if (!driven && !marked && pins.FirstOrDefault(p => Driven.Contains(Type(p))) is { } waiting)
            {
                findings.Add(new ErcFinding(
                    power ? ErcKind.PowerNotDriven : ErcKind.NotDriven,
                    ErcSeverity.Error,
                    net,
                    [waiting]));
            }
        }

        return findings;
    }

    /// <summary>What the matrix says about two pin types meeting; null when they may.</summary>
    public static ErcSeverity? Conflict(string first, string second)
    {
        int row = Array.IndexOf(Types, first), column = Array.IndexOf(Types, second);
        if (row < 0 || column < 0)
        {
            return null;
        }

        return Matrix[row][column] switch
        {
            'e' => ErcSeverity.Error,
            'w' => ErcSeverity.Warning,
            _ => null,
        };
    }

    private static string Type(DesignPin pin) => pin.Pin.Pin.ElectricalType;
}
