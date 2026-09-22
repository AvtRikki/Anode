using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// The wires of a drag, kept square.
///
/// Pulling only the end of a wire that sits on a pin leaves the rest of it where it was, and a wire drawn across and
/// then down comes out of a sideways drag slanted. KiCad does not let it (orthoLineDrag, with its line mode left at
/// the default of right angles): a wire that meets what is moving is taken apart into what the move does along it
/// and what it does across it. Along it, the end just follows. Across it, the whole wire has to go sideways, and
/// what happens to its far end depends on what is there:
/// <list type="bullet">
/// <item>nothing — the wire is loose, and it simply goes along;</item>
/// <item>one other wire, running the way the move does — that wire gives: its end slides with the corner and it
/// grows or shrinks, and the drawing keeps its corner;</item>
/// <item>anything else — a pin, a dot, a label, a branch — cannot be pulled, so the wire stays where it was and a
/// step is put in near the moving end to take up the difference.</item>
/// </list>
///
/// The shape is worked out afresh from the drawing as it was for every position of the pointer rather than grown a
/// step at a time as KiCad does, so it can only depend on where the pointer is, not on the path it took to get there.
/// </summary>
public sealed class WireDrag
{
    private readonly HashSet<WireEnd> _following;
    private readonly IReadOnlyList<Leg> _legs;

    private WireDrag(IReadOnlyList<WireEnd> following, IReadOnlyList<Leg> legs, IReadOnlyList<SchWire> wires)
    {
        Following = following;
        _following = [.. following];
        _legs = legs;
        Wires = wires;
    }

    /// <summary>The wire ends that follow what is moving, as <see cref="SchDrag.Following"/> finds them.</summary>
    public IReadOnlyList<WireEnd> Following { get; }

    /// <summary>Every wire the drag may change: the ones that follow, and the neighbours they may slide.</summary>
    public IReadOnlyList<SchWire> Wires { get; }

    /// <summary>What has to give when <paramref name="moving"/> is dragged, read from the sheet as it stands now.</summary>
    public static WireDrag For(Schematic sheet, IReadOnlyList<SchItem> moving)
    {
        var following = SchDrag.Following(sheet, moving);
        if (following.Count == 0)
        {
            return new WireDrag([], [], []);
        }

        var carried = new HashSet<SList>(moving.Select(item => item.Node));
        var fixedPoints = FixedPoints(sheet, carried);
        var legs = new List<Leg>();
        var wires = new List<SchWire>();

        foreach (var group in following.GroupBy(end => end.Wire))
        {
            var wire = group.Key;
            wires.Add(wire);

            // Both ends following means the wire travels whole; a wire of more than one run, or one that is neither
            // across nor down, has no square to keep. Those stretch as they always did.
            var points = wire.Points;
            if (group.Count() != 1 || points.Length != 2 || points[0] == points[1] || !IsSquare(points[0], points[1]))
            {
                continue;
            }

            int end = group.Single().Index;
            var far = points[1 - end];

            var others = sheet.Wires.Where(w => w != wire && !carried.Contains(w.Node)).ToList();
            var meeting = others.Where(w => w.Points is { Length: > 0 } p && (p[0] == far || p[^1] == far)).ToList();
            bool crossed = others.Any(w => Passes(w, far));
            bool held = crossed || fixedPoints.Contains(far);

            var neighbour = !held && meeting.Count == 1 && meeting[0].Points.Length == 2 ? meeting[0] : null;
            bool free = !held && meeting.Count == 0;

            if (neighbour is not null && !wires.Contains(neighbour))
            {
                wires.Add(neighbour);
            }

            legs.Add(new Leg(wire, end, points[end], far, neighbour, free));
        }

        return new WireDrag(following, legs, wires);
    }

    /// <summary>
    /// The wires as they stand with what is moving taken <paramref name="delta"/> away. <paramref name="grid"/> is how
    /// far apart the steps of neighbouring wires are set, so that they do not land on top of each other.
    /// </summary>
    public WireDragShape Shape(Vector2L delta, long grid)
    {
        var points = Wires.ToDictionary(w => w, w => w.Points);
        var alongX = new Dictionary<Vector2L, long>();
        var alongY = new Dictionary<Vector2L, long>();
        var stepping = new List<Leg>();

        foreach (var leg in _legs)
        {
            long across = leg.Horizontal ? delta.Y : delta.X;
            if (across == 0)
            {
                continue;
            }

            if (leg.Free || (leg.Neighbour is { } n && Gives(n, leg.Horizontal)))
            {
                (leg.Horizontal ? alongY : alongX)[leg.Far] = across;
            }
            else
            {
                stepping.Add(leg);
            }
        }

        foreach (var (wire, line) in points)
        {
            for (int i = 0; i < line.Length; i++)
            {
                var p = line[i];
                line[i] = _following.Contains(new WireEnd(wire, i))
                    ? p + delta
                    : new Vector2L(p.X + alongX.GetValueOrDefault(p), p.Y + alongY.GetValueOrDefault(p));
            }
        }

        var added = new List<(SchWire Like, Vector2L From, Vector2L To)>();
        foreach (var group in stepping.GroupBy(leg => leg.Horizontal))
        {
            // One grid step further back for each wire, in the order KiCad takes them: the ones trailing the move
            // first, so that each step is tucked inside the next and none of them crosses another.
            bool horizontal = group.Key;
            bool forward = (horizontal ? delta.Y : delta.X) >= 0;
            var ordered = group.OrderBy(leg => horizontal ? leg.Near.Y : leg.Near.X);
            int count = 0;

            foreach (var leg in forward ? ordered : ordered.Reverse())
            {
                var line = points[leg.Wire];
                var path = Stepped(line[1 - leg.End], line[leg.End], horizontal, count++ * grid);

                line[1 - leg.End] = path[0];
                line[leg.End] = path.Count > 1 ? path[1] : path[0];
                for (int k = 2; k < path.Count; k++)
                {
                    added.Add((leg.Wire, path[k - 1], path[k]));
                }
            }
        }

        return new WireDragShape(points, added);
    }

    /// <summary>
    /// From the end that stays to the end that moved, square: along the wire's own direction, across in one step,
    /// and along again. The step sits <paramref name="back"/> from the moving end, never past the end that stays.
    /// </summary>
    private static List<Vector2L> Stepped(Vector2L from, Vector2L to, bool horizontal, long back)
    {
        List<Vector2L> path;
        if (horizontal)
        {
            long run = to.X - from.X;
            long x = to.X - (Math.Sign(run) * Math.Min(back, Math.Abs(run)));
            path = [from, new Vector2L(x, from.Y), new Vector2L(x, to.Y), to];
        }
        else
        {
            long run = to.Y - from.Y;
            long y = to.Y - (Math.Sign(run) * Math.Min(back, Math.Abs(run)));
            path = [from, new Vector2L(from.X, y), new Vector2L(to.X, y), to];
        }

        for (int i = path.Count - 1; i > 0; i--)
        {
            if (path[i] == path[i - 1])
            {
                path.RemoveAt(i);
            }
        }

        return path;
    }

    /// <summary>
    /// A neighbour gives if it runs the way the corner is being pulled, so that sliding its end keeps it straight —
    /// or if it has no length at all and so no direction to lose.
    /// </summary>
    private static bool Gives(SchWire neighbour, bool legHorizontal)
    {
        var p = neighbour.Points;
        return p[0] == p[1] || (legHorizontal ? p[0].X == p[1].X : p[0].Y == p[1].Y);
    }

    private static bool IsSquare(Vector2L a, Vector2L b) => a.X == b.X || a.Y == b.Y;

    /// <summary>The point lies part way along the wire, not at an end: a branch leaving it there.</summary>
    private static bool Passes(SchWire wire, Vector2L point)
    {
        var p = wire.Points;
        for (int i = 1; i < p.Length; i++)
        {
            if (SchJunctions.IsInside(p[i - 1], p[i], point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every point on the sheet something other than a wire joins at, and which a drag must not tear a wire off:
    /// pins of parts that stay, dots, labels, flags, sheet pins, bus entries.
    /// </summary>
    private static HashSet<Vector2L> FixedPoints(Schematic sheet, HashSet<SList> carried)
    {
        var points = new HashSet<Vector2L>();
        foreach (var symbol in sheet.Symbols)
        {
            if (!carried.Contains(symbol.Node) && symbol.Definition is { } definition)
            {
                var toSheet = symbol.ToSheet;
                foreach (var pin in definition.PinsOf(symbol.Unit, symbol.BodyStyle))
                {
                    points.Add(toSheet.ApplyRounded(pin.Position));
                }
            }
        }

        points.UnionWith(sheet.Junctions.Select(j => j.Position));
        points.UnionWith(sheet.NoConnects.Select(n => n.Position));
        points.UnionWith(sheet.Labels.Select(l => l.Position));
        foreach (var entry in sheet.BusEntries)
        {
            points.Add(entry.Position);
            points.Add(entry.EndPoint);
        }

        foreach (var child in sheet.Sheets)
        {
            points.UnionWith(child.Pins.Select(pin => pin.Position));
        }

        return points;
    }

    /// <summary>A wire with one end following something: which end, which way it runs, and what its far end meets.</summary>
    private sealed record Leg(SchWire Wire, int End, Vector2L Near, Vector2L Far, SchWire? Neighbour, bool Free)
    {
        public bool Horizontal => Near.Y == Far.Y;
    }
}

/// <summary>The wires of a drag at one position of the pointer: where each one's points are, and the steps put in.</summary>
public sealed class WireDragShape(
    IReadOnlyDictionary<SchWire, Vector2L[]> points,
    IReadOnlyList<(SchWire Like, Vector2L From, Vector2L To)> added)
{
    /// <summary>Every wire of the drag, changed or not, with its points where they now are.</summary>
    public IReadOnlyDictionary<SchWire, Vector2L[]> Points => points;

    /// <summary>The steps that had to be put in, each to be drawn as the wire it was made for.</summary>
    public IReadOnlyList<(SchWire Like, Vector2L From, Vector2L To)> Added => added;

    /// <summary>Wires the drag has shrunk to nothing, which would be left on the sheet as a dot of wire.</summary>
    public IEnumerable<SchWire> Collapsed => points.Where(kv => kv.Value.All(p => p == kv.Value[0])).Select(kv => kv.Key);

    /// <summary>Writes the new points into the wires that changed. Wires that did not are left alone, byte for byte.</summary>
    public void Write()
    {
        foreach (var (wire, line) in points)
        {
            if (wire.Node.Find("pts") is not { } pts || wire.Points.SequenceEqual(line))
            {
                continue;
            }

            int at = 0;
            foreach (var point in pts.Lists().Where(l => l.Head == "xy"))
            {
                var to = line[at++];
                point.MapPoint(_ => to);
            }
        }
    }

    /// <summary>The steps as wires, written as the wire each was made for.</summary>
    public IReadOnlyList<SchWire> NewWires() => [.. added.Select(a => SchWires.Like(a.Like, [a.From, a.To]))];
}
