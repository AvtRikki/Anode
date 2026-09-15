using System.Collections;

namespace Ecad.Sexpr;

public sealed class SList : SNode, IReadOnlyList<SNode>
{
    private SNode[] _items;
    private int _count;

    public SList() => _items = [];

    public SList(string head, params SNode[] children)
    {
        _items = new SNode[children.Length + 1];
        Add(SAtom.Symbol(head));
        foreach (var child in children)
        {
            Add(child);
        }
    }

    /// <summary>
    /// Whitespace before the closing parenthesis. <c>null</c> lets the writer decide.
    /// </summary>
    public string? CloseTrivia { get; set; }

    public int Count => _count;

    public SNode this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            return _items[index];
        }
    }

    /// <summary>The leading symbol, e.g. <c>footprint</c> for <c>(footprint ...)</c>.</summary>
    public string? Head => _count > 0 && _items[0] is SAtom { Kind: SAtomKind.Symbol } a ? a.Raw : null;

    public bool HasListChildren
    {
        get
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i] is SList)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>First direct child list with the given head.</summary>
    public SList? Find(string head)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i] is SList l && l.Head == head)
            {
                return l;
            }
        }

        return null;
    }

    /// <summary>All direct child lists with the given head.</summary>
    public IEnumerable<SList> FindAll(string head)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i] is SList l && l.Head == head)
            {
                yield return l;
            }
        }
    }

    public IEnumerable<SList> Lists()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i] is SList l)
            {
                yield return l;
            }
        }
    }

    public SAtom? AtomAt(int index) => index < _count ? _items[index] as SAtom : null;

    /// <summary>True if a direct child is the bare symbol, e.g. <c>locked</c> in <c>(footprint "x" locked ...)</c>.</summary>
    public bool HasSymbol(string symbol)
    {
        for (int i = 1; i < _count; i++)
        {
            if (_items[i] is SAtom a && a.IsSymbol(symbol))
            {
                return true;
            }
        }

        return false;
    }

    public int IndexOf(SNode node) => Array.IndexOf(_items, node, 0, _count);

    public void Add(SNode node) => Insert(_count, node);

    public void Insert(int index, SNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_count, nameof(index));
        if (node.Parent is not null)
        {
            throw new InvalidOperationException("Node already belongs to a list; remove it first.");
        }

        // An inline list that gains a sub-list becomes multi-line, so its closing paren must move.
        if (node is SList && CloseTrivia is not null && !HasListChildren)
        {
            CloseTrivia = null;
        }

        InsertParsed(index, node);
    }

    public bool Remove(SNode node)
    {
        int index = IndexOf(node);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
        var node = _items[index];
        _count--;
        if (index < _count)
        {
            Array.Copy(_items, index + 1, _items, index, _count - index);
        }

        _items[_count] = null!;
        node.Parent = null;
    }

    /// <summary>Appends without touching trivia; used by the parser.</summary>
    internal void AddParsed(SNode node) => InsertParsed(_count, node);

    private void InsertParsed(int index, SNode node)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length == 0 ? 4 : _items.Length * 2);
        }

        if (index < _count)
        {
            Array.Copy(_items, index, _items, index + 1, _count - index);
        }

        _items[index] = node;
        _count++;
        node.Parent = this;
    }

    internal void TrimExcess()
    {
        if (_count < _items.Length)
        {
            Array.Resize(ref _items, _count);
        }
    }

    public IEnumerator<SNode> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
