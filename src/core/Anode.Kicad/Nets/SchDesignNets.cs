namespace Anode.Kicad;

/// <summary>A pin of a part, and the place in the design it stands in.</summary>
/// <param name="Place">The sheet instance the part is on.</param>
public sealed record DesignPin(SheetInstance Place, SchNetPin Pin)
{
    /// <summary>The designator the part carries in this place, which a reused sheet gives per place.</summary>
    public string Reference => Pin.Symbol.ReferenceAt(Place.Path) ?? Pin.Reference;

    public override string ToString() => $"{Reference}-{Pin.Number}";
}

/// <summary>
/// One net of a whole design: what every sheet of it joins together, and what it is called.
/// </summary>
/// <param name="Name">The net's name as KiCad writes it in a netlist: a global name as it stands, a named net with
/// the sheet path it was named on, an unnamed one after its first pin.</param>
/// <param name="Parts">The local nets it is made of, each with the place its sheet stands in.</param>
public sealed record DesignNet(
    string Name,
    bool IsNamed,
    IReadOnlyList<DesignPin> Pins,
    IReadOnlyList<(SheetInstance Place, SchNet Net)> Parts);

/// <summary>
/// The nets of a design rather than of one sheet. A sheet's own nets are worked out where the wires are
/// (<see cref="SchConnectivity"/>); here the sheets are joined to each other the three ways KiCad joins them:
///
/// - a pin of a sheet symbol meets the hierarchical label of the same name inside that sheet;
/// - a global label joins every net carrying that name, wherever it is;
/// - a power symbol is a global label drawn as a part, and joins the same way.
///
/// A sheet placed twice is two places, each with its own nets: that is what a reused sheet is for.
/// </summary>
public static class SchDesignNets
{
    /// <summary>
    /// The design's nets, walked down from <paramref name="rootFile"/>. Sheets come from <paramref name="open"/>
    /// when it answers — an open tab knows edits the file does not — and from disk otherwise.
    /// </summary>
    public static IReadOnlyList<DesignNet> Build(string rootFile, Func<string, Schematic?>? open = null)
    {
        // One reading of each file, shared by the walk and by the nets: the walk hands back the very sheet objects
        // the nets are made of, and a pin of a sheet symbol is then the same object on both sides of the join.
        var read = new Dictionary<string, (Schematic Sheet, IReadOnlyList<SchNet> Nets)?>(StringComparer.Ordinal);

        Schematic? Open(string file)
        {
            if (!read.TryGetValue(file, out var known))
            {
                Schematic? loaded;
                try
                {
                    loaded = open?.Invoke(file) ?? (File.Exists(file) ? Schematic.Load(file) : null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
                    or Sexpr.SexprParseException or System.Text.DecoderFallbackException)
                {
                    loaded = null;
                }

                read[file] = known = loaded is null ? null : (loaded, SchConnectivity.Build(loaded));
            }

            return known?.Sheet;
        }

        var places = SchHierarchy.Walk(rootFile, Open);
        var locals = new List<(SheetInstance Place, Schematic Sheet, IReadOnlyList<SchNet> Nets)>();
        foreach (var place in places)
        {
            if (read.GetValueOrDefault(place.File) is { } sheet)
            {
                locals.Add((place, sheet.Sheet, sheet.Nets));
            }
        }

        // One entry per local net; joining is by index, as within a sheet it is by point.
        var keys = new List<(int Sheet, int Net)>();
        var index = new Dictionary<(string Path, SchNet Net), int>();
        for (int s = 0; s < locals.Count; s++)
        {
            for (int n = 0; n < locals[s].Nets.Count; n++)
            {
                index[(locals[s].Place.Path, locals[s].Nets[n])] = keys.Count;
                keys.Add((s, n));
            }
        }

        var groups = new int[keys.Count];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = i;
        }

        int Find(int i) => groups[i] == i ? i : groups[i] = Find(groups[i]);
        void Join(int a, int b) => groups[Find(a)] = Find(b);

        // A name that reaches beyond its own sheet: a global label, or a power symbol, which is one drawn as a part.
        var global = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            var (s, n) = keys[i];
            foreach (string name in GlobalNames(locals[s].Nets[n]))
            {
                if (global.TryGetValue(name, out int first))
                {
                    Join(i, first);
                }
                else
                {
                    global[name] = i;
                }
            }
        }

        // A sheet's pin on the parent, and the hierarchical label of that name inside the sheet, are one net.
        var byPath = locals.Select((l, i) => (l.Place.Path, Index: i)).ToDictionary(x => x.Path, x => x.Index, StringComparer.Ordinal);
        foreach (var (place, sheet, nets) in locals)
        {
            if (place.Parent is not { } parentPath || place.Placement is not { } placement
                || !byPath.TryGetValue(parentPath, out int parent))
            {
                continue;
            }

            foreach (var pin in placement.Pins)
            {
                string name = KicadText.Unescape(pin.Name);
                int above = locals[parent].Nets.ToList().FindIndex(net => net.SheetPins.Contains(pin));
                int below = nets.ToList().FindIndex(net => net.Items.OfType<SchLabel>()
                    .Any(l => l.Kind == SchLabelKind.Hierarchical && l.Shown == name));

                if (above >= 0 && below >= 0)
                {
                    Join(index[(parentPath, locals[parent].Nets[above])], index[(place.Path, nets[below])]);
                }
            }
        }

        return Assemble(locals, keys, Find);
    }

    private static IReadOnlyList<DesignNet> Assemble(
        List<(SheetInstance Place, Schematic Sheet, IReadOnlyList<SchNet> Nets)> locals,
        List<(int Sheet, int Net)> keys,
        Func<int, int> find)
    {
        var parts = new Dictionary<int, List<(SheetInstance Place, SchNet Net)>>();
        for (int i = 0; i < keys.Count; i++)
        {
            var (s, n) = keys[i];
            if (!parts.TryGetValue(find(i), out var list))
            {
                parts[find(i)] = list = [];
            }

            list.Add((locals[s].Place, locals[s].Nets[n]));
        }

        var nets = new List<DesignNet>();
        foreach (var group in parts.Values)
        {
            var pins = group.SelectMany(p => p.Net.Pins.Select(pin => new DesignPin(p.Place, pin))).ToList();
            var (name, named) = NameOf(group, pins);
            nets.Add(new DesignNet(name, named, pins, group));
        }

        return [.. nets.OrderByDescending(n => n.IsNamed).ThenBy(n => n.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What KiCad calls the net: a global name as it stands, since it means the same everywhere; otherwise the name
    /// it was given on the shallowest sheet that names it, with that sheet's path before it; otherwise its first pin.
    /// </summary>
    private static (string Name, bool Named) NameOf(
        List<(SheetInstance Place, SchNet Net)> group,
        IReadOnlyList<DesignPin> pins)
    {
        var globals = group.SelectMany(p => GlobalNames(p.Net)).Order(StringComparer.Ordinal).ToList();
        if (globals.Count > 0)
        {
            return (globals[0], true);
        }

        var named = group.Where(p => p.Net.IsNamed)
            .OrderBy(p => p.Place.Depth)
            .ThenBy(p => p.Place.Trail, StringComparer.Ordinal)
            .ThenBy(p => p.Net.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (named.Net is not null)
        {
            return (named.Place.Trail + named.Net.Name, true);
        }

        if (pins.Count == 0)
        {
            return ("Net-()", false);
        }

        var driver = pins.OrderBy(p => p.Reference, StringComparer.Ordinal).ThenBy(p => p.Pin.Number, StringComparer.Ordinal).First();
        return (SchNetNames.FromPin(driver.Reference, driver.Pin.Pin, driver.Pin.Symbol.Definition, group.Any(p => p.Net.IsNoConnect)), false);
    }

    /// <summary>The names this net carries that mean the same on every sheet: global labels and power symbols.</summary>
    private static IEnumerable<string> GlobalNames(SchNet net)
    {
        foreach (var item in net.Items)
        {
            if (item is SchLabel { Kind: SchLabelKind.Global } label)
            {
                yield return label.Shown;
            }
            else if (item is SymbolInstance { Definition.IsPower: true } symbol
                && (symbol.Value ?? symbol.Definition.Value) is { Length: > 0 } power)
            {
                yield return KicadText.Unescape(power);
            }
        }
    }
}
