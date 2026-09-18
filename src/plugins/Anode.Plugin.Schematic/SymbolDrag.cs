using Avalonia.Input;
using Anode.Kicad;

namespace Anode.Plugin.Schematic;

/// <summary>
/// What a part looks like while it is being dragged. The payload is an in-process format, so the part itself
/// travels rather than its name: nothing has to look it up again on the other side, and nothing leaks to other
/// applications, which have no use for it.
/// </summary>
internal static class SymbolDrag
{
    public static readonly DataFormat<SymbolChoice> Format =
        DataFormat.CreateInProcessFormat<SymbolChoice>("anode.symbol");

    /// <summary>The part a drag is carrying, or null when the drag is somebody else's.</summary>
    public static SymbolChoice? Carried(IDataTransfer transfer) =>
        transfer.Contains(Format) ? transfer.GetItems(Format).FirstOrDefault()?.TryGetRaw(Format) as SymbolChoice : null;
}
