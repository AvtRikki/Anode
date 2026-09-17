using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// An item that owns one list of a file's tree. Editing works against this: the undo stack snapshots and restores
/// subtrees, and neither it nor the commands need to know whether the tree is a board or a sheet.
/// </summary>
public interface INodeItem
{
    SList Node { get; }

    /// <summary>False once the item has been removed from its document.</summary>
    bool IsAttached { get; }

    /// <summary>Called after undo or redo swapped the subtree, for views cached on top of it.</summary>
    void AfterRestore();
}

/// <summary>A document that can take a top-level item out of its tree and put it back exactly where it was.</summary>
public interface INodeHost
{
    int Detach(INodeItem item);

    void Attach(INodeItem item, int index);
}
