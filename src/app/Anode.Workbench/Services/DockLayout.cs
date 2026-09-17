using Anode.Sdk;

namespace Anode.Workbench.Services;

/// <summary>A section: a tab header over one body, which can be closed. Sections are separated by a line, not a frame.</summary>
public sealed record DockStack(DockArea Area, IReadOnlyList<PanelDescriptor> Panels)
{
    public string Key => Area.ToString();
}

public sealed record DockLayout(
    IReadOnlyList<DockStack> Left,
    IReadOnlyList<DockStack> Right,
    DockStack? Bottom,
    IReadOnlyList<PanelDescriptor> Rail)
{
    public int DockedPanelCount => Left.Concat(Right).Sum(s => s.Panels.Count) + (Bottom?.Panels.Count ?? 0);
}

/// <summary>
/// Placement from the mockups (2b): five fixed places — left top and bottom, right top and bottom, and the bottom
/// dock — not "any number of panels anywhere". A place holds one section and the panels of that place are its tabs,
/// lowest order first. Panels the user sent to the rail keep out of the docks until they are pinned back, and a place
/// nobody asked for simply has no section.
/// </summary>
public static class DockPlanner
{
    public static DockLayout Plan(IEnumerable<PanelDescriptor> panels, IReadOnlySet<string>? sentToRail = null)
    {
        sentToRail ??= new HashSet<string>();
        var ordered = panels.OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        var rail = ordered.Where(p => sentToRail.Contains(p.Id)).ToList();
        var docked = ordered.Where(p => !sentToRail.Contains(p.Id)).ToList();

        DockStack? Section(DockArea area) =>
            docked.Where(p => p.Area == area).ToList() is { Count: > 0 } tabs ? new DockStack(area, tabs) : null;

        var left = new[] { Section(DockArea.LeftTop), Section(DockArea.LeftBottom) }.OfType<DockStack>().ToList();
        var right = new[] { Section(DockArea.RightTop), Section(DockArea.RightBottom) }.OfType<DockStack>().ToList();

        return new DockLayout(left, right, Section(DockArea.Bottom), rail);
    }
}
