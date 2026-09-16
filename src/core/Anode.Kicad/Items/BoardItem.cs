using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>Typed view over a CST list. Reads are computed from the tree, so the file stays the source of truth.</summary>
public abstract class BoardItem(SList node, Board board)
{
    public SList Node { get; } = node;

    public Board Board { get; } = board;

    public string? Uuid => Node.ChildString("uuid");

    public bool IsLocked => Node.HasSymbol("locked") || Node.ChildBool("locked");

    /// <summary>The item selected, moved and deleted as a unit: the footprint for its pads, texts, graphics and zones.</summary>
    public virtual BoardItem TopLevel => this;

    /// <summary>False once a top-level item has been removed from the board.</summary>
    public bool IsAttached => Node.Parent is not null;

    /// <summary>Transform from the coordinates stored in this item to board coordinates.</summary>
    public virtual Transform2D ToBoard => Transform2D.Identity;
}
