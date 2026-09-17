using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>An undoable change to a file's tree.</summary>
public interface IEditCommand
{
    string Name { get; }

    /// <summary>Top-level items whose geometry or presence changes; views refresh them around Apply and Revert.</summary>
    IReadOnlyList<INodeItem> Affected { get; }

    void Apply();

    void Revert();
}

/// <summary>
/// Changes items in place. The first Apply runs the mutation; undo and redo restore exact snapshots of the items'
/// trees, so reverting returns the file byte for byte.
/// </summary>
public sealed class ModifyNodesCommand(string name, IReadOnlyList<INodeItem> items, Action mutate) : IEditCommand
{
    private SList[]? _before;
    private SList[]? _after;

    public string Name => name;

    public IReadOnlyList<INodeItem> Affected => items;

    public void Apply()
    {
        if (_before is null)
        {
            _before = Snapshot();
            try
            {
                mutate();
            }
            catch
            {
                Restore(_before);
                _before = null;
                throw;
            }

            _after = Snapshot();
        }
        else
        {
            Restore(_after!);
        }
    }

    public void Revert() => Restore(_before ?? throw new InvalidOperationException("Command has not been applied."));

    private SList[] Snapshot() => [.. items.Select(i => i.Node.CloneList())];

    private void Restore(SList[] snapshots)
    {
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Node.RestoreFrom(snapshots[i]);
        }

        // A restored subtree invalidates whatever was cached on top of it — a footprint's pads, say.
        foreach (var item in items)
        {
            item.AfterRestore();
        }
    }
}

/// <summary>Adds top-level items to a document; undo takes them out again, redo puts them back where they were.</summary>
public sealed class AddNodesCommand(INodeHost host, IReadOnlyList<INodeItem> items) : IEditCommand
{
    private readonly List<int> _indices = [];

    public string Name => items.Count == 1 ? "Add item" : $"Add {items.Count} items";

    public IReadOnlyList<INodeItem> Affected => items;

    public void Apply()
    {
        for (int i = 0; i < items.Count; i++)
        {
            // New items go to the end of the file, which is where KiCad appends them; redo restores the exact place.
            host.Attach(items[i], i < _indices.Count ? _indices[i] : int.MaxValue);
            if (i >= _indices.Count)
            {
                _indices.Add(items[i].Node.Parent?.IndexOf(items[i].Node) ?? 0);
            }
        }
    }

    public void Revert()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            host.Detach(items[i]);
        }
    }
}

/// <summary>Removes top-level items; undo puts them back at their original positions in the file.</summary>
public sealed class DeleteNodesCommand(INodeHost host, IReadOnlyList<INodeItem> items) : IEditCommand
{
    private readonly List<(INodeItem Item, int Index)> _removed = [];

    public string Name => items.Count == 1 ? "Delete item" : $"Delete {items.Count} items";

    public IReadOnlyList<INodeItem> Affected => items;

    public void Apply()
    {
        _removed.Clear();
        foreach (var item in items.OrderByDescending(i => i.Node.Parent?.IndexOf(i.Node) ?? 0))
        {
            _removed.Add((item, host.Detach(item)));
        }
    }

    public void Revert()
    {
        foreach (var (item, index) in _removed.OrderBy(r => r.Index))
        {
            host.Attach(item, index);
        }

        _removed.Clear();
    }
}

/// <summary>
/// Several changes as one step. A single click of a drawing tool can draw a wire, dot it and cut the wire it ran
/// into; undo must take all of that back at once, or the file passes through states the user never made.
/// </summary>
public sealed class CompositeCommand(string name, IReadOnlyList<IEditCommand> commands) : IEditCommand
{
    public string Name => name;

    public IReadOnlyList<INodeItem> Affected => [.. commands.SelectMany(c => c.Affected)];

    public void Apply()
    {
        foreach (var command in commands)
        {
            command.Apply();
        }
    }

    public void Revert()
    {
        // Backwards, so each command undoes into the tree the one before it left.
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            commands[i].Revert();
        }
    }
}

public sealed class UndoStack
{
    private readonly List<IEditCommand> _done = [];
    private readonly List<IEditCommand> _undone = [];
    private int _savedAt;

    public event Action? Changed;

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    public string? UndoName => CanUndo ? _done[^1].Name : null;

    public string? RedoName => CanRedo ? _undone[^1].Name : null;

    /// <summary>True when the board differs from the last saved (or loaded) state.</summary>
    public bool IsDirty => _savedAt != _done.Count;

    public void Execute(IEditCommand command)
    {
        command.Apply();

        // The saved state lived in the redo branch that is about to be discarded.
        if (_savedAt > _done.Count)
        {
            _savedAt = -1;
        }

        _done.Add(command);
        _undone.Clear();
        Changed?.Invoke();
    }

    public IEditCommand? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        var command = _done[^1];
        command.Revert();
        _done.RemoveAt(_done.Count - 1);
        _undone.Add(command);
        Changed?.Invoke();
        return command;
    }

    public IEditCommand? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var command = _undone[^1];
        command.Apply();
        _undone.RemoveAt(_undone.Count - 1);
        _done.Add(command);
        Changed?.Invoke();
        return command;
    }

    public void MarkSaved()
    {
        _savedAt = _done.Count;
        Changed?.Invoke();
    }
}
