using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Copying an item of a sheet. The copy is a separate subtree with identities of its own: a uuid names one item in
/// one file, so a clone that kept it would be a second item claiming to be the first, and KiCad would be right to
/// complain. Everything else — geometry, fields, stroke, the parts we do not model — is carried over untouched,
/// which is what keeps copy-and-paste lossless for what we have never taught the app to read.
/// </summary>
public static class SchClone
{
    /// <summary>A free copy of <paramref name="item"/>, typed for <paramref name="sheet"/> but not yet on it.</summary>
    public static SchItem? Of(Schematic sheet, SchItem item) => Of(sheet, item.Node);

    /// <summary>A free copy of a stored subtree — what pasting does, once for every paste.</summary>
    public static SchItem? Of(Schematic sheet, SList node)
    {
        var copy = node.CloneList();
        Rename(copy);
        return sheet.Wrap(copy);
    }

    /// <summary>Gives every <c>(uuid …)</c> in the subtree a new one; a symbol carries one per pin as well.</summary>
    private static void Rename(SList list)
    {
        if (list.Head == "uuid")
        {
            list.AtomAt(1)?.SetString(Guid.NewGuid().ToString("D"));
            return;
        }

        foreach (var child in list.Lists())
        {
            Rename(child);
        }
    }
}
