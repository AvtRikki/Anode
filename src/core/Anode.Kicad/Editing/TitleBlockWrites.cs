using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writing a file's title block — the name, date, revision and company printed in the drawing's corner. Sheets and
/// boards keep it the same way, as <c>(title_block …)</c> under the root after <c>(paper …)</c>, so this works on
/// the root of either. KiCad writes only the fields that say something, in a fixed order, and no block at all when
/// none do; so does this.
/// </summary>
public static class TitleBlockWrites
{
    /// <summary>The fields in the order KiCad writes them. Comments follow and are left as they are.</summary>
    public static readonly IReadOnlyList<string> Fields = ["title", "date", "rev", "company"];

    public static void Set(SList root, string field, string value)
    {
        int order = IndexOf(field);
        var block = root.Find("title_block");

        if (value.Length == 0)
        {
            if (block?.Find(field) is { } gone)
            {
                block.Remove(gone);
                if (!block.Lists().Any())
                {
                    root.Remove(block);
                }
            }

            return;
        }

        if (block is null)
        {
            block = SchNodes.Adopt(SDocument.Parse("(title_block)").Root);
            root.Insert(root.Find("paper") is { } paper ? root.IndexOf(paper) + 1 : root.Count, block);
        }

        if (block.Find(field) is { } existing)
        {
            (existing.AtomAt(1) ?? throw new KiCadFormatException($"({field}) has no value.")).SetString(value);
            return;
        }

        // Before the first field that KiCad writes later, and before any comment.
        var fresh = SchNodes.Adopt(SDocument.Parse($"({field} {SEscape.Quote(value)})").Root);
        var next = block.Lists().FirstOrDefault(l => l.Head is "comment" || (l.Head is { } head && Fields.Contains(head) && IndexOf(head) > order));
        block.Insert(next is null ? block.Count : block.IndexOf(next), fresh);
    }

    private static int IndexOf(string field)
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Fields[i] == field)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(field), field, "Not a title block field.");
    }
}

/// <summary>
/// Changes a child of the file's root that is not an item on the sheet — the title block — and that the change may
/// create or remove. Undo puts back exactly what was there, where it was, or takes away what the change added.
/// </summary>
public sealed class RootChildCommand(SList root, string head, string name, Action mutate) : IEditCommand
{
    private (SList? Node, int Index) _before;
    private (SList? Node, int Index) _after;
    private bool _applied;

    public string Name => name;

    /// <summary>Nothing drawn on the sheet changes.</summary>
    public IReadOnlyList<INodeItem> Affected => [];

    public void Apply()
    {
        if (_applied)
        {
            Put(_after);
            return;
        }

        _before = Take();
        mutate();
        _after = Take();
        _applied = true;
    }

    public void Revert() => Put(_before);

    private (SList? Node, int Index) Take() =>
        root.Find(head) is { } node ? (node.CloneList(), root.IndexOf(node)) : (null, -1);

    private void Put((SList? Node, int Index) state)
    {
        if (root.Find(head) is { } current)
        {
            root.Remove(current);
        }

        if (state.Node is { } node)
        {
            root.Insert(state.Index, node.CloneList());
        }
    }
}
