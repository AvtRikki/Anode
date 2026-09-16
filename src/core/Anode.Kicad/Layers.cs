using Anode.Sexpr;

namespace Anode.Kicad;

/// <param name="Ordinal">Layer id as stored in the file; numbering differs between KiCad versions.</param>
/// <param name="Name">Canonical name, e.g. <c>F.Cu</c>, <c>F.SilkS</c>.</param>
/// <param name="Type">signal, power, mixed, jumper, user, front, back...</param>
/// <param name="UserName">Optional user-visible name, e.g. <c>F.Silkscreen</c>.</param>
public sealed record Layer(int Ordinal, string Name, string Type, string? UserName)
{
    public bool IsCopper => Name.EndsWith(".Cu", StringComparison.Ordinal);

    public string DisplayName => UserName ?? Name;

    /// <summary>Physical stack position for copper: F.Cu = 0, InN.Cu = N, B.Cu = last.</summary>
    public int CopperIndex => LayerTable.CopperIndex(Name);
}

public sealed class LayerTable : IReadOnlyList<Layer>
{
    private readonly List<Layer> _layers = [];
    private readonly Dictionary<string, Layer> _byName = new(StringComparer.Ordinal);

    internal LayerTable(SList? layersNode)
    {
        if (layersNode is null)
        {
            return;
        }

        foreach (var entry in layersNode.Lists())
        {
            // (0 "F.Cu" signal "top_copper")
            if (entry.AtomAt(0) is not { } ordinalAtom || !int.TryParse(ordinalAtom.Raw, out int ordinal))
            {
                continue;
            }

            var layer = new Layer(ordinal, entry.Str(1) ?? string.Empty, entry.Str(2) ?? "user", entry.Str(3));
            _layers.Add(layer);
            _byName.TryAdd(layer.Name, layer);
        }

        Copper = [.. _layers.Where(l => l.IsCopper).OrderBy(l => l.CopperIndex)];
    }

    public IReadOnlyList<Layer> Copper { get; } = [];

    public int Count => _layers.Count;

    public Layer this[int index] => _layers[index];

    public Layer? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Expands pad/via layer patterns: <c>*.Cu</c>, <c>F&amp;B.Cu</c>, <c>*.Mask</c>, <c>*.In.Cu</c>...
    /// Unknown plain names are returned as-is so nothing is silently dropped.
    /// </summary>
    public IEnumerable<string> Expand(string pattern)
    {
        if (pattern == "*.Cu")
        {
            return Copper.Select(l => l.Name);
        }

        if (pattern == "*.In.Cu")
        {
            return Copper.Where(l => l.Name.StartsWith("In", StringComparison.Ordinal)).Select(l => l.Name);
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            string suffix = pattern[1..];
            return new[] { "F" + suffix, "B" + suffix }.Where(n => _byName.ContainsKey(n) || _layers.Count == 0);
        }

        if (pattern.StartsWith("F&B.", StringComparison.Ordinal))
        {
            string suffix = pattern[3..];
            return ["F" + suffix, "B" + suffix];
        }

        return [pattern];
    }

    /// <summary>Copper layers between two copper layers inclusive, in stack order.</summary>
    public IEnumerable<Layer> CopperSpan(string a, string b)
    {
        int ia = CopperIndex(a), ib = CopperIndex(b);
        (ia, ib) = ia <= ib ? (ia, ib) : (ib, ia);
        return Copper.Where(l => l.CopperIndex >= ia && l.CopperIndex <= ib);
    }

    internal static int CopperIndex(string name)
    {
        if (name == "F.Cu")
        {
            return 0;
        }

        if (name == "B.Cu")
        {
            return int.MaxValue;
        }

        return name.StartsWith("In", StringComparison.Ordinal) && name.EndsWith(".Cu", StringComparison.Ordinal)
               && int.TryParse(name.AsSpan(2, name.Length - 5), out int n)
            ? n
            : -1;
    }

    public IEnumerator<Layer> GetEnumerator() => _layers.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
