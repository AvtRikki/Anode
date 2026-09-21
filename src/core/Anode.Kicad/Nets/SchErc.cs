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

    /// <summary>A pin of a sheet symbol that names nothing inside the sheet.</summary>
    SheetPinWithoutLabel,

    /// <summary>A hierarchical label inside a sheet that the sheet symbol above has no pin for.</summary>
    LabelWithoutSheetPin,
}

/// <summary>A sheet and what does not match about it, and the item to show for it.</summary>
/// <param name="Place">Where the item stands: the parent for a pin, the sheet itself for a label.</param>
public sealed record SheetFinding(ErcKind Kind, SheetInstance Place, string Name, SchItem Item);

/// <summary>
/// The electrical rules KiCad checks between the pins of a net, ported from its own tables
/// (<c>erc_settings.cpp</c>, <c>erc.cpp</c>):
///
/// - every pair of pins on a net is looked up in the matrix of pin types; a pair may be fine, doubtful or wrong;
/// - a net carrying a pin that waits to be driven — an input, a power input — and nothing that drives it is
///   reported, unless somebody marked it no-connect. A net with a power input is a power net, and only a power
///   output drives one; that is the rule behind KiCad's "missing power flag".
///
/// How strictly each of these is applied — and which pins may meet — is <see cref="ErcRules"/>, which is KiCad's
/// defaults until a project says otherwise.
/// </summary>
public static class SchErc
{
    /// <summary>What drives an ordinary pin.</summary>
    private static readonly HashSet<string> Driving = new(StringComparer.Ordinal)
        { "output", "power_out", "passive", "tri_state", "bidirectional" };

    /// <summary>What drives a power input: only a power output.</summary>
    private static readonly HashSet<string> DrivingPower = new(StringComparer.Ordinal) { "power_out" };

    /// <summary>What waits to be driven.</summary>
    private static readonly HashSet<string> Driven = new(StringComparer.Ordinal) { "input", "power_in" };

    /// <summary>Every rule broken in <paramref name="nets"/>, in the order the nets come.</summary>
    public static IReadOnlyList<ErcFinding> Check(IEnumerable<DesignNet> nets, ErcRules? rules = null)
    {
        var settings = rules ?? ErcRules.Default;
        var findings = new List<ErcFinding>();

        foreach (var net in nets)
        {
            // A pin found twice on a net — a part's shared pin drawn in two units — is one pin.
            var pins = net.Pins
                .GroupBy(p => (p.Reference, p.Pin.Number))
                .Select(g => g.First())
                .OrderBy(p => p.Reference, KicadOrder.Instance)
                .ThenBy(p => p.Pin.Number, KicadOrder.Instance)
                .ToList();

            if (pins.Count == 0)
            {
                continue;
            }

            bool power = pins.Any(p => Type(p) == "power_in");
            bool driven = pins.Any(p => (power ? DrivingPower : Driving).Contains(Type(p)));
            bool marked = net.Parts.Any(part => part.Net.IsNoConnect);

            findings.AddRange(Conflicts(net, pins, settings));

            var kind = power ? ErcKind.PowerNotDriven : ErcKind.NotDriven;
            if (!driven && !marked && settings.Severity(kind) is { } howBad
                && pins.FirstOrDefault(p => Driven.Contains(Type(p))) is { } waiting)
            {
                findings.Add(new ErcFinding(kind, howBad, net, [waiting]));
            }
        }

        return findings;
    }

    /// <summary>
    /// What does not match between a sheet symbol and the sheet it stands for: KiCad pairs a pin of the symbol with
    /// the hierarchical label of the same name inside, and says so when either has no partner. A pin with nothing to
    /// answer it carries no signal in; a label with no pin carries one nowhere.
    /// </summary>
    public static IReadOnlyList<SheetFinding> CheckSheets(
        IEnumerable<SheetInstance> places,
        Func<string, Schematic?> open,
        ErcRules? rules = null)
    {
        var settings = rules ?? ErcRules.Default;
        var findings = new List<SheetFinding>();

        // Both halves of the mismatch are one rule to KiCad, so a project that silences it silences both.
        if (settings.Severity(ErcKind.SheetPinWithoutLabel) is null)
        {
            return findings;
        }


        foreach (var place in places)
        {
            if (place.Placement is not { } placement || place.Parent is null || open(place.File) is not { } sheet)
            {
                continue;
            }

            var pins = placement.Pins
                .GroupBy(p => KicadText.Unescape(p.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var labels = sheet.Labels
                .Where(l => l.Kind == SchLabelKind.Hierarchical)
                .GroupBy(l => l.Shown, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var (name, pin) in pins.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!labels.ContainsKey(name))
                {
                    findings.Add(new SheetFinding(ErcKind.SheetPinWithoutLabel, place, name, pin));
                }
            }

            foreach (var (name, label) in labels.OrderBy(l => l.Key, StringComparer.Ordinal))
            {
                if (!pins.ContainsKey(name))
                {
                    findings.Add(new SheetFinding(ErcKind.LabelWithoutSheetPin, place, name, label));
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// The conflicts of one net, condensed as KiCad condenses them (<c>ERC_TESTER::TestPinToPin</c>). Every pair
    /// that may not meet is found first, but they are not all reported: a net where six pins quarrel has fifteen
    /// pairs, and fifteen lines saying the same thing is a list nobody reads.
    ///
    /// So the pins are taken in the order of how well each speaks for a conflict — an unspecified pin says more
    /// than a power output, which is what KiCad's weights mean — and each one, in its turn, swallows every pair it
    /// takes part in and is reported once, against whichever of those partners is nearest to it on the sheet. A
    /// partner on another sheet is taken only while nothing on this one has been found.
    /// </summary>
    private static IEnumerable<ErcFinding> Conflicts(DesignNet net, List<DesignPin> pins, ErcRules settings)
    {
        var mismatches = new List<(int First, int Second, ErcSeverity Severity)>();
        for (int i = 0; i < pins.Count; i++)
        {
            for (int j = i + 1; j < pins.Count; j++)
            {
                if (!Stacked(pins[i], pins[j]) && settings.Conflict(Type(pins[i]), Type(pins[j])) is { } severity)
                {
                    mismatches.Add((i, j, severity));
                }
            }
        }

        var order = Enumerable.Range(0, pins.Count)
            .Where(i => mismatches.Any(m => m.First == i || m.Second == i))
            .OrderByDescending(i => Weight(Type(pins[i])))
            .ThenBy(i => i)
            .ToList();

        var findings = new List<ErcFinding>();
        foreach (int i in order)
        {
            if (mismatches.Count == 0)
            {
                break;
            }

            int nearest = -1;
            double smallest = double.PositiveInfinity;
            var severity = ErcSeverity.Warning;

            mismatches.RemoveAll(m =>
            {
                int other = m.First == i ? m.Second : m.Second == i ? m.First : -1;
                if (other < 0)
                {
                    return false;
                }

                if (pins[i].Place.Path != pins[other].Place.Path)
                {
                    if (double.IsInfinity(smallest))
                    {
                        (nearest, severity) = (other, m.Severity);
                    }
                }
                else
                {
                    double distance = Distance(pins[i], pins[other]);
                    if (double.IsInfinity(smallest) || distance < smallest)
                    {
                        (smallest, nearest, severity) = (distance, other, m.Severity);
                    }
                }

                return true;
            });

            if (nearest >= 0)
            {
                findings.Add(new ErcFinding(ErcKind.PinConflict, severity, net, [pins[i], pins[nearest]]));
            }
        }

        return findings;
    }

    /// <summary>
    /// Pins of one part drawn on top of each other — the several ground pins of a chip, shown as one — are stacked,
    /// and stacked pins are one connection, not a conflict with themselves.
    /// </summary>
    private static bool Stacked(DesignPin first, DesignPin second) =>
        first.Place.Path == second.Place.Path
        && string.Equals(first.Reference, second.Reference, StringComparison.Ordinal)
        && first.Pin.At == second.Pin.At
        && string.Equals(first.Pin.Pin.Name, second.Pin.Pin.Name, StringComparison.Ordinal)
        && string.Equals(Type(first), Type(second), StringComparison.Ordinal);

    private static double Distance(DesignPin first, DesignPin second)
    {
        double dx = first.Pin.At.X - second.Pin.At.X, dy = first.Pin.At.Y - second.Pin.At.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// How well a pin type speaks for a conflict, as KiCad weighs them: the vaguer the type, the more likely it is
    /// the one at fault and so the one the finding is written against.
    /// </summary>
    private static int Weight(string type) => type switch
    {
        "free" => 11,
        "unspecified" => 10,
        "passive" => 9,
        "open_collector" => 8,
        "open_emitter" => 7,
        "input" => 6,
        "tri_state" => 5,
        "bidirectional" => 4,
        "output" => 3,
        "power_in" => 2,
        "power_out" => 1,
        _ => 0,
    };

    /// <summary>What KiCad's own matrix says about two pin types meeting; null when they may.</summary>
    public static ErcSeverity? Conflict(string first, string second) => ErcRules.Default.Conflict(first, second);

    private static string Type(DesignPin pin) => pin.Pin.Pin.ElectricalType;
}
