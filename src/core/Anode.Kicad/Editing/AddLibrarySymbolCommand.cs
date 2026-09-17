using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Copies a definition into the sheet's <c>lib_symbols</c> as an undoable step. Placing a part writes two things —
/// the instance and the definition it draws from — and undoing the placement has to take both back, or the file is
/// left carrying a body for a part that is no longer there and no longer matches what it was before.
///
/// A definition the sheet already had is left alone, and undo then leaves it alone too: it was not ours to remove.
/// </summary>
public sealed class AddLibrarySymbolCommand(Schematic sheet, string libId, LibSymbol definition) : IEditCommand
{
    private SList? _added;

    public string Name => "Add symbol definition";

    /// <summary>Nothing top-level changes: the instance travelling in the same step is what the views redraw.</summary>
    public IReadOnlyList<INodeItem> Affected => [];

    public void Apply()
    {
        _added = SchSymbols.Ensure(sheet, libId, definition)
            ? sheet.Root.Find("lib_symbols")?.Lists().FirstOrDefault(l =>
                l.Head == "symbol" && string.Equals(l.Str(1), libId, StringComparison.Ordinal))
            : null;
    }

    public void Revert()
    {
        if (_added is null)
        {
            return;
        }

        sheet.Root.Find("lib_symbols")?.Remove(_added);
        sheet.Unregister(libId);
        _added = null;
    }
}
