using Ecad.Sexpr;

namespace Ecad.KiCad.Editing;

/// <summary>An undoable change to a board.</summary>
public interface IBoardCommand
{
    string Name { get; }

    /// <summary>Top-level items whose geometry or presence changes; views refresh them around Apply and Revert.</summary>
    IReadOnlyList<BoardItem> Affected { get; }

    void Apply();

    void Revert();
}

/// <summary>
/// Changes items in place. The first Apply runs the mutation; undo and redo restore exact snapshots of the items'
/// trees, so reverting returns the file byte for byte.
/// </summary>
public sealed class ModifyItemsCommand(string name, IReadOnlyList<BoardItem> items, Action mutate) : IBoardCommand
{
    private SList[]? _before;
    private SList[]? _after;

    public string Name => name;

    public IReadOnlyList<BoardItem> Affected => items;

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

        foreach (var footprint in items.OfType<Footprint>())
        {
            footprint.Refresh();
        }
    }
}

/// <summary>Removes top-level items; undo puts them back at their original positions in the file.</summary>
public sealed class DeleteItemsCommand(Board board, IReadOnlyList<BoardItem> items) : IBoardCommand
{
    private readonly List<(BoardItem Item, int Index)> _removed = [];

    public string Name => items.Count == 1 ? "Delete item" : $"Delete {items.Count} items";

    public IReadOnlyList<BoardItem> Affected => items;

    public void Apply()
    {
        _removed.Clear();
        foreach (var item in items.OrderByDescending(i => board.Root.IndexOf(i.Node)))
        {
            _removed.Add((item, board.Detach(item)));
        }
    }

    public void Revert()
    {
        foreach (var (item, index) in _removed.OrderBy(r => r.Index))
        {
            board.Attach(item, index);
        }

        _removed.Clear();
    }
}

public sealed class UndoStack
{
    private readonly List<IBoardCommand> _done = [];
    private readonly List<IBoardCommand> _undone = [];
    private int _savedAt;

    public event Action? Changed;

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    public string? UndoName => CanUndo ? _done[^1].Name : null;

    public string? RedoName => CanRedo ? _undone[^1].Name : null;

    /// <summary>True when the board differs from the last saved (or loaded) state.</summary>
    public bool IsDirty => _savedAt != _done.Count;

    public void Execute(IBoardCommand command)
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

    public IBoardCommand? Undo()
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

    public IBoardCommand? Redo()
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
