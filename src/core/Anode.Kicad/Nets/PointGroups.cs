using Anode.Geometry;

namespace Anode.Kicad;

/// <summary>
/// Points joined into groups, keyed by the exact nanometre: the same point twice is one point, and joining two of
/// them puts their groups together. The plainest union-find, and the reckoning behind both a sheet's nets and its
/// buses.
/// </summary>
internal sealed class PointGroups
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
