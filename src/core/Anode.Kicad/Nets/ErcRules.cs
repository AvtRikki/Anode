namespace Anode.Kicad;

/// <summary>
/// How strictly each electrical rule is applied, and which pins may meet — KiCad's own defaults, or what a project
/// says instead.
///
/// A project keeps this in its <c>.kicad_pro</c>: <c>erc.rule_severities</c> names each rule "error", "warning" or
/// "ignore", and <c>erc.pin_map</c> is the matrix of pin types, its cells 0 (fine), 1 (doubtful) or 2 (wrong). A
/// rule set to "ignore" is not reported at all, which is how a designer silences what they have decided is fine.
///
/// The pin matrix is a rule with two settings at once, as KiCad reads it (<c>ERC_SETTINGS::GetSeverity</c>): the
/// <c>pin_to_pin</c> severity says only whether conflicts are reported, and the matrix cell says whether a conflict
/// is a warning or an error.
/// </summary>
public sealed class ErcRules
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

    /// <summary>The order of KiCad's matrix: the rows and the columns are these types, in this order.</summary>
    internal static readonly string[] Types =
        [Input, Output, Bidirectional, TriState, Passive, Free, Unspecified, PowerIn, PowerOut, OpenCollector, OpenEmitter, NoConnect];

    /// <summary>
    /// KiCad's default pin matrix, row by row as it writes it: <c>.</c> the pins may meet, <c>w</c> doubtful,
    /// <c>e</c> wrong.
    /// </summary>
    private static readonly string[] DefaultMatrix =
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

    /// <summary>What each of our findings is called in a project file.</summary>
    private static readonly Dictionary<ErcKind, string> Names = new()
    {
        [ErcKind.PinConflict] = "pin_to_pin",
        [ErcKind.NotDriven] = "pin_not_driven",
        [ErcKind.PowerNotDriven] = "power_pin_not_driven",
        [ErcKind.SheetPinWithoutLabel] = "hier_label_mismatch",
        [ErcKind.LabelWithoutSheetPin] = "hier_label_mismatch",
    };

    private readonly IReadOnlyDictionary<string, string> _severities;
    private readonly string[] _matrix;

    private ErcRules(IReadOnlyDictionary<string, string>? severities, string[]? matrix)
    {
        _severities = severities ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _matrix = matrix ?? DefaultMatrix;
    }

    /// <summary>KiCad's own defaults, for a design whose project says nothing.</summary>
    public static ErcRules Default { get; } = new(null, null);

    /// <summary>
    /// What a project asks for, falling back to the defaults for anything it does not mention. A project that will
    /// not read is a project without settings, as everywhere else.
    /// </summary>
    public static ErcRules Of(ProjectFile? project) => project?.Erc ?? Default;

    /// <summary>The rules beside <paramref name="schematicFile"/>, from the project that holds it.</summary>
    public static ErcRules For(string? schematicFile) => Of(ProjectFile.For(schematicFile));

    /// <summary>
    /// Built from what a project file says. <paramref name="severities"/> is <c>rule_severities</c> and
    /// <paramref name="pinMap"/> is <c>pin_map</c>, twelve rows of twelve cells; either may be null.
    /// </summary>
    internal static ErcRules From(IReadOnlyDictionary<string, string>? severities, IReadOnlyList<IReadOnlyList<int>>? pinMap)
    {
        string[]? matrix = null;
        if (pinMap is { Count: 12 } rows && rows.All(r => r.Count == 12))
        {
            matrix = [.. rows.Select(row => new string([.. row.Select(cell => cell switch { 1 => 'w', 2 => 'e', _ => '.' })]))];
        }

        return new ErcRules(severities, matrix);
    }

    /// <summary>How strictly a finding of this kind is treated; null when the project asks for it to be ignored.</summary>
    public ErcSeverity? Severity(ErcKind kind)
    {
        // The pin matrix decides warning or error for itself; its setting says only whether to look at all.
        if (kind == ErcKind.PinConflict)
        {
            return Written(kind) == "ignore" ? null : ErcSeverity.Error;
        }

        return Written(kind) switch
        {
            "ignore" => null,
            "warning" => ErcSeverity.Warning,

            // Everything KiCad does not list as milder is an error, and so is anything we cannot read.
            _ => ErcSeverity.Error,
        };
    }

    /// <summary>
    /// What the matrix says about two pin types meeting: null when they may meet, or when conflicts are ignored.
    /// A type neither KiCad nor we know is treated as no conflict rather than as a wrong one.
    /// </summary>
    public ErcSeverity? Conflict(string first, string second)
    {
        if (Written(ErcKind.PinConflict) == "ignore")
        {
            return null;
        }

        int row = Array.IndexOf(Types, first), column = Array.IndexOf(Types, second);
        if (row < 0 || column < 0 || row >= _matrix.Length || column >= _matrix[row].Length)
        {
            return null;
        }

        return _matrix[row][column] switch
        {
            'e' => ErcSeverity.Error,
            'w' => ErcSeverity.Warning,
            _ => null,
        };
    }

    private string? Written(ErcKind kind) =>
        Names.TryGetValue(kind, out string? name) && _severities.TryGetValue(name, out string? severity) ? severity : null;
}
