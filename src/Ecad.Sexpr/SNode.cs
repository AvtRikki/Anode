namespace Ecad.Sexpr;

/// <summary>
/// A node of the concrete syntax tree. Every node remembers the exact whitespace that preceded it
/// in the source, so an unmodified tree is written back byte-for-byte.
/// </summary>
public abstract class SNode
{
    public SList? Parent { get; internal set; }

    /// <summary>
    /// Whitespace preceding this node in the source. <c>null</c> means the node was created or moved
    /// programmatically and the writer will choose KiCad-style whitespace for it.
    /// </summary>
    public string? LeadingTrivia { get; set; }

    public int IndexInParent => Parent?.IndexOf(this) ?? -1;

    public void Remove() => Parent?.Remove(this);

    /// <summary>Detached copy of this subtree, trivia included.</summary>
    public abstract SNode DeepClone();

    public override string ToString() => SWriter.WriteNode(this);
}
