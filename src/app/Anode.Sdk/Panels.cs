using Avalonia.Controls;

namespace Anode.Sdk;

/// <summary>
/// Where a panel lives. Navigation goes left, properties of the selection go right, any list of rows with coordinates
/// goes to the bottom. A panel that fits none of these is a document tab, not a panel.
/// </summary>
public enum DockSide
{
    Left,
    Right,
    Bottom,
}

/// <summary>A dockable panel. The workbench decides whether it lands in a dock stack or in the icon rail.</summary>
/// <param name="TitleKey">Translation key of the tab header, e.g. <c>pcb.panel.layers</c>.</param>
public sealed record PanelDescriptor(string Id, string TitleKey, DockSide Side, Func<IWorkbench, Control> CreateContent)
{
    /// <summary>Tab header in the active language.</summary>
    public string Title => Tr.T(TitleKey);

    /// <summary>Name from <see cref="Icons"/> drawn in the icon rail; falls back to <see cref="RailLabel"/>.</summary>
    public string IconKey { get; init; } = string.Empty;

    /// <summary>Translation key of the two-letter rail label, used when the panel has no icon.</summary>
    public string RailLabelKey { get; init; } = string.Empty;

    /// <summary>Icon rail label in the active language.</summary>
    public string RailLabel => RailLabelKey.Length == 0 ? string.Empty : Tr.T(RailLabelKey);

    /// <summary>Panels with the same group open as tabs of one stack ("Инспектор | Цепь").</summary>
    public string? Group { get; init; }

    /// <summary>Lower orders are placed first, so they keep their dock when space runs out.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Document type ids this panel belongs to, e.g. <c>anode.pcb</c>. A panel about the thing on the canvas is only
    /// shown while that thing is open; leave empty for panels that hold for every document (project, inspector, log).
    /// </summary>
    public IReadOnlyList<string> DocumentTypes { get; init; } = [];

    /// <summary>Whether this panel has a place while <paramref name="documentTypeId"/> is the active document.</summary>
    public bool AppliesTo(string? documentTypeId) =>
        DocumentTypes.Count == 0 || (documentTypeId is not null && DocumentTypes.Contains(documentTypeId, StringComparer.Ordinal));
}

public interface IPanelRegistry
{
    IReadOnlyList<PanelDescriptor> Panels { get; }

    event Action? Changed;

    IDisposable Register(PanelDescriptor panel);
}
