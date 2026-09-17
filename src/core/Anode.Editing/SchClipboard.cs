using Anode.Kicad;
using Anode.Sexpr;

namespace Anode.Editing;

/// <summary>
/// What was copied, waiting to be pasted. It holds detached subtrees rather than items of a sheet, so copying and
/// then closing the sheet — or editing what was copied — leaves the clipboard saying what it said when it was filled.
///
/// This clipboard is the application's own: it carries between open sheets, but not yet to and from other programs.
/// KiCad exchanges s-expression text through the system clipboard, and that seam belongs to the app layer.
/// </summary>
public static class SchClipboard
{
    private static readonly List<SList> Stored = [];

    public static bool HasContent => Stored.Count > 0;

    /// <summary>The stored subtrees. They are never attached to anything; a paste clones them again.</summary>
    public static IReadOnlyList<SList> Content => Stored;

    public static void Put(IEnumerable<SchItem> items)
    {
        Stored.Clear();
        foreach (var item in items)
        {
            Stored.Add(item.Node.CloneList());
        }
    }

    public static void Clear() => Stored.Clear();
}
