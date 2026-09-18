using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad;

/// <summary>A pin of a placed part, and where it sits on the sheet.</summary>
public sealed record SchNetPin(SymbolInstance Symbol, SchPin Pin, Vector2L At)
{
    /// <summary>The designator of the part the pin belongs to, e.g. "R1".</summary>
    public string Reference => Symbol.Reference ?? "?";

    public string Number => Pin.Number;

    public override string ToString() => $"{Reference}-{Number}";
}

/// <summary>
/// One net of a sheet: what is joined to what, and what it is called.
///
/// Deliberately not <see cref="Net"/>, which is the board's. A board reads its nets from the file — they arrive
/// declared, with a code — while a sheet's nets exist nowhere until they are worked out from the geometry. Sharing
/// one type would mean carrying a code that a schematic never has and a name that a schematic may not know yet.
/// </summary>
/// <param name="Name">What the net is called: a label on it, or a name made from the first pin.</param>
/// <param name="IsNamed">Whether a label gave it that name, as against one made up here.</param>
public sealed record SchNet(string Name, bool IsNamed, IReadOnlyList<SchNetPin> Pins, IReadOnlyList<SchItem> Items)
{
    /// <summary>
    /// Somebody put a no-connect here, which says a pin is meant to lead nowhere. A check that complains about
    /// unconnected pins must keep quiet about these: the mark exists precisely to say "I know".
    /// </summary>
    public bool IsNoConnect => Items.OfType<SchNoConnect>().Any();
}

/// <summary>
/// Working out what is connected to what. The rule for whether two wires meet is the one the junction dots already
/// answer in <see cref="SchJunctions"/> — a T needs a dot, a crossing is not a connection — and it is used from
/// there rather than written again, so the drawing and the netlist cannot come to different conclusions.
///
/// This first pass knows wires, junctions, pins and local labels. Buses, global and hierarchical labels and power
/// symbols join later; until then a bus is left out rather than guessed at.
/// </summary>
public static class SchConnectivity
{
    /// <summary>The nets of a sheet, in a stable order: named ones first, then by name.</summary>
    public static IReadOnlyList<SchNet> Build(Schematic sheet)
    {
        var groups = new PointGroups();
        var segments = Segments(sheet);

        // A wire ties its own ends together.
        foreach (var (a, b, _) in segments)
        {
            groups.Join(a, b);
        }

        // A dot ties whatever passes through it to whatever ends there.
        foreach (var dot in sheet.Junctions)
        {
            foreach (var (a, b, _) in segments)
            {
                if (SchJunctions.IsInside(a, b, dot.Position))
                {
                    groups.Join(dot.Position, a);
                }
            }

            groups.Add(dot.Position);
        }

        var pins = PinsOf(sheet);
        foreach (var pin in pins)
        {
            groups.Add(pin.At);
        }

        foreach (var mark in sheet.NoConnects)
        {
            groups.Add(mark.Position);
        }

        var naming = Names(sheet, pins);
        foreach (var (position, _, _) in naming)
        {
            groups.Add(position);
        }

        // A name written twice on a sheet means one net, wherever the two places are — that is what a label is for,
        // and what a power symbol is entirely. Local labels say so only within the sheet; global and hierarchical
        // ones will also reach beyond it, once sheets are understood.
        foreach (var sameName in naming.GroupBy(n => n.Name, StringComparer.Ordinal))
        {
            var places = sameName.Select(n => n.Position).ToList();
            for (int i = 1; i < places.Count; i++)
            {
                groups.Join(places[0], places[i]);
            }
        }

        return WithBuses(Assemble(groups, segments, pins, naming, sheet.NoConnects), sheet);
    }

    /// <summary>
    /// The nets a bus declares. A bus carries several nets along one path, which the point-keyed reckoning above
    /// cannot express: joining its members at the points the bus passes through would fuse them into one net, which
    /// is the opposite of what a bus is.
    ///
    /// So a labelled bus is read as a declaration rather than a connection — these nets exist — and the wires that
    /// tap it join their members by name, through the ordinary rules. A member already found on the sheet gains the
    /// bus among its items; one nobody has tapped yet stands as a net with nothing on it.
    /// </summary>
    private static IReadOnlyList<SchNet> WithBuses(IReadOnlyList<SchNet> nets, Schematic sheet)
    {
        var aliases = sheet.BusAliases;
        var declared = new List<(string Name, SchItem Bus)>();

        foreach (var label in sheet.Labels)
        {
            var members = SchBusNames.Members(label.Text, aliases);
            if (members.Count > 1)
            {
                declared.AddRange(members.Select(m => (m, (SchItem)label)));
            }
        }

        if (declared.Count == 0)
        {
            return nets;
        }

        var byName = nets.ToDictionary(n => n.Name, StringComparer.Ordinal);
        var result = new List<SchNet>(nets);

        foreach (var (name, bus) in declared)
        {
            if (byName.TryGetValue(name, out var found))
            {
                if (!found.Items.Contains(bus))
                {
                    var items = new List<SchItem>(found.Items) { bus };
                    var joined = found with { Items = items };
                    result[result.IndexOf(found)] = joined;
                    byName[name] = joined;
                }

                continue;
            }

            var declaredNet = new SchNet(name, true, [], [bus]);
            byName[name] = declaredNet;
            result.Add(declaredNet);
        }

        return [.. result.OrderByDescending(n => n.IsNamed).ThenBy(n => n.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Everything that gives a net a name, and where it says so: labels, and power symbols, which are nothing but a
    /// name for a net drawn as a symbol. A net-class flag names nothing — it carries rules, not identity.
    ///
    /// A label naming a bus is left out here: it names several nets at once, which is handled where buses are.
    /// </summary>
    private static List<(Vector2L Position, string Name, SchItem Item)> Names(Schematic sheet, IReadOnlyList<SchNetPin> pins)
    {
        var names = new List<(Vector2L, string, SchItem)>();

        var aliases = sheet.BusAliases;
        foreach (var label in sheet.Labels.Where(l =>
            l.Kind is SchLabelKind.Local or SchLabelKind.Global or SchLabelKind.Hierarchical))
        {
            if (SchBusNames.Members(label.Text, aliases).Count <= 1)
            {
                names.Add((label.Position, label.Text, label));
            }
        }

        foreach (var pin in pins)
        {
            if (pin.Symbol.Definition is { IsPower: true } definition
                && (pin.Symbol.Value ?? definition.Value) is { Length: > 0 } name)
            {
                names.Add((pin.At, name, pin.Symbol));
            }
        }

        return names;
    }

    /// <summary>Every pin of every placed part, in sheet coordinates.</summary>
    public static IReadOnlyList<SchNetPin> PinsOf(Schematic sheet)
    {
        var pins = new List<SchNetPin>();
        foreach (var symbol in sheet.Symbols)
        {
            if (symbol.Definition is not { } definition)
            {
                continue;
            }

            var toSheet = symbol.ToSheet;
            foreach (var pin in definition.PinsOf(symbol.Unit, symbol.BodyStyle))
            {
                pins.Add(new SchNetPin(symbol, pin, toSheet.ApplyRounded(pin.EndPoint)));
            }
        }

        return pins;
    }

    private static List<(Vector2L A, Vector2L B, SchWire Wire)> Segments(Schematic sheet)
    {
        var segments = new List<(Vector2L, Vector2L, SchWire)>();
        foreach (var wire in sheet.Wires.Where(w => !w.IsBus))
        {
            var points = wire.Points;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i - 1] != points[i])
                {
                    segments.Add((points[i - 1], points[i], wire));
                }
            }
        }

        return segments;
    }

    private static IReadOnlyList<SchNet> Assemble(
        PointGroups groups,
        List<(Vector2L A, Vector2L B, SchWire Wire)> segments,
        IReadOnlyList<SchNetPin> pins,
        List<(Vector2L Position, string Name, SchItem Item)> naming,
        IReadOnlyList<SchNoConnect> noConnects)
    {
        var pinsOf = new Dictionary<int, List<SchNetPin>>();
        var itemsOf = new Dictionary<int, List<SchItem>>();
        var namesOf = new Dictionary<int, SortedSet<string>>();

        foreach (var pin in pins)
        {
            Bucket(pinsOf, groups.Of(pin.At)).Add(pin);
        }

        foreach (var (a, _, wire) in segments)
        {
            var items = Bucket(itemsOf, groups.Of(a));
            if (!items.Contains(wire))
            {
                items.Add(wire);
            }
        }

        foreach (var mark in noConnects)
        {
            Bucket(itemsOf, groups.Of(mark.Position)).Add(mark);
        }

        foreach (var (position, name, item) in naming)
        {
            int group = groups.Of(position);
            var items = Bucket(itemsOf, group);
            if (!items.Contains(item))
            {
                items.Add(item);
            }

            if (!namesOf.TryGetValue(group, out var names))
            {
                namesOf[group] = names = new SortedSet<string>(StringComparer.Ordinal);
            }

            names.Add(name);
        }

        var nets = new List<SchNet>();
        foreach (int group in pinsOf.Keys.Concat(itemsOf.Keys).Distinct())
        {
            var groupPins = pinsOf.GetValueOrDefault(group) ?? [];
            var groupItems = itemsOf.GetValueOrDefault(group) ?? [];

            // A group with nothing on it is a point nobody uses; a label alone is a net waiting for a wire.
            if (groupPins.Count == 0 && groupItems.Count == 0)
            {
                continue;
            }

            bool named = namesOf.TryGetValue(group, out var names) && names.Count > 0;

            // Several labels on one net: KiCad keeps the first by name, and so do we.
            string name = named ? names!.First() : Made(groupPins);
            nets.Add(new SchNet(name, named, groupPins, groupItems));
        }

        return [.. nets.OrderByDescending(n => n.IsNamed).ThenBy(n => n.Name, StringComparer.Ordinal)];
    }

    /// <summary>A name for a net nobody labelled, after the first pin on it, as KiCad makes one.</summary>
    private static string Made(IReadOnlyList<SchNetPin> pins) =>
        pins.Count == 0 ? "Net-()" : $"Net-({pins.OrderBy(p => p.ToString(), StringComparer.Ordinal).First()})";

    private static List<T> Bucket<T>(Dictionary<int, List<T>> buckets, int key)
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            buckets[key] = bucket = [];
        }

        return bucket;
    }

    /// <summary>Points joined to points: the plainest union-find, keyed by the exact nanometre.</summary>
    private sealed class PointGroups
    {
        private readonly Dictionary<Vector2L, int> _index = [];
        private readonly List<int> _parent = [];

        public int Add(Vector2L point)
        {
            if (_index.TryGetValue(point, out int existing))
            {
                return existing;
            }

            int id = _parent.Count;
            _parent.Add(id);
            _index[point] = id;
            return id;
        }

        public int Of(Vector2L point) => Root(Add(point));

        public void Join(Vector2L a, Vector2L b)
        {
            int rootA = Root(Add(a));
            int rootB = Root(Add(b));
            if (rootA != rootB)
            {
                _parent[rootB] = rootA;
            }
        }

        private int Root(int id)
        {
            while (_parent[id] != id)
            {
                _parent[id] = _parent[_parent[id]];
                id = _parent[id];
            }

            return id;
        }
    }
}
