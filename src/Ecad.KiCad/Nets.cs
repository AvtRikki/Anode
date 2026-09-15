using Ecad.Sexpr;

namespace Ecad.KiCad;

/// <param name="Code">Net code from files before <see cref="KiCadFormat.NetNamesOnly"/>; null afterwards.</param>
public sealed record Net(int? Code, string Name)
{
    public bool IsUnconnected => Name.Length == 0;
}

public sealed class NetTable
{
    private readonly Dictionary<int, Net> _byCode = [];
    private readonly Dictionary<string, Net> _byName = new(StringComparer.Ordinal);
    private readonly List<Net> _nets = [];

    public IReadOnlyList<Net> All => _nets;

    /// <summary>Registers a top-level <c>(net 5 "GND")</c> declaration.</summary>
    internal void Declare(SList node)
    {
        if (node.AtomAt(1) is { Kind: SAtomKind.Symbol } codeAtom && int.TryParse(codeAtom.Raw, out int code))
        {
            var net = new Net(code, node.Str(2) ?? string.Empty);
            if (_byCode.TryAdd(code, net))
            {
                _nets.Add(net);
                _byName.TryAdd(net.Name, net);
            }
        }
    }

    /// <summary>Resolves an item's <c>(net 5)</c>, <c>(net 5 "GND")</c> or <c>(net "GND")</c> reference.</summary>
    public Net? Resolve(SList? netNode)
    {
        if (netNode is null || netNode.Count < 2)
        {
            return null;
        }

        var first = netNode.AtomAt(1);
        if (first is { Kind: SAtomKind.Symbol } && int.TryParse(first.Raw, out int code))
        {
            if (_byCode.TryGetValue(code, out var known))
            {
                return known;
            }

            return netNode.Str(2) is { } legacyName ? GetOrAdd(legacyName, code) : null;
        }

        return first is null ? null : GetOrAdd(first.Value, null);
    }

    public Net? Find(string name) => _byName.GetValueOrDefault(name);

    private Net GetOrAdd(string name, int? code)
    {
        if (_byName.TryGetValue(name, out var net))
        {
            return net;
        }

        net = new Net(code, name);
        _byName[name] = net;
        _nets.Add(net);
        if (code is int c)
        {
            _byCode.TryAdd(c, net);
        }

        return net;
    }
}
