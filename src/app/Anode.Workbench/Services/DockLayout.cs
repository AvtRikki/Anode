using Anode.Sdk;

namespace Anode.Workbench.Services;

/// <summary>A stack: a tab header over one body, collapsible. Stacks are separated by a line, not a frame.</summary>
public sealed record DockStack(DockSide Side, IReadOnlyList<PanelDescriptor> Panels)
{
    public string Key => Panels[0].Group ?? Panels[0].Id;
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
/// Placement rules from the mockups (2b): four fixed places, not "any number of panels".
/// A new panel first becomes a tab in an existing stack (its group), then a new stack, but never more than two stacks
/// on the left and three on the right; the bottom dock is a single stack of tabs. Whatever does not fit goes to the
/// icon rail and opens over the canvas. Panels the user sent to the rail stay there; panels pinned from the rail take a
/// dock place and push the last stack on that side to the rail instead.
/// </summary>
public static class DockPlanner
{
    public const int MaxLeftStacks = 2;
    public const int MaxRightStacks = 3;

    public static DockLayout Plan(IEnumerable<PanelDescriptor> panels, IReadOnlySet<string>? sentToRail = null, IReadOnlyList<string>? pinned = null)
    {
        sentToRail ??= new HashSet<string>();
        pinned ??= [];

        var ordered = panels.OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        var rail = ordered.Where(p => sentToRail.Contains(p.Id)).ToList();
        var candidates = ordered.Where(p => !sentToRail.Contains(p.Id)).ToList();

        // Bottom: one stack, every bottom panel a tab.
        var bottomPanels = candidates.Where(p => p.Side == DockSide.Bottom).ToList();
        var bottom = bottomPanels.Count > 0 ? new DockStack(DockSide.Bottom, bottomPanels) : null;

        var left = PlaceSide(candidates, DockSide.Left, MaxLeftStacks, pinned, rail);
        var right = PlaceSide(candidates, DockSide.Right, MaxRightStacks, pinned, rail);

        return new DockLayout(left, right, bottom, rail);
    }

    private static List<DockStack> PlaceSide(List<PanelDescriptor> candidates, DockSide side, int maxStacks, IReadOnlyList<string> pinned, List<PanelDescriptor> rail)
    {
        // Group into stacks in order of first appearance.
        var stacks = new List<List<PanelDescriptor>>();
        foreach (var panel in candidates.Where(p => p.Side == side))
        {
            var existing = panel.Group is null ? null : stacks.FirstOrDefault(s => s[0].Group == panel.Group);
            if (existing is not null)
            {
                existing.Add(panel);
            }
            else
            {
                stacks.Add([panel]);
            }
        }

        // Stacks containing a pinned panel are placed first, most recently pinned first, so pins win the space.
        int PinRank(List<PanelDescriptor> stack)
        {
            int rank = -1;
            foreach (var panel in stack)
            {
                rank = Math.Max(rank, pinned.ToList().IndexOf(panel.Id));
            }

            return rank;
        }

        var placed = stacks
            .Select((stack, index) => (stack, index, pin: PinRank(stack)))
            .OrderByDescending(s => s.pin >= 0)
            .ThenByDescending(s => s.pin)
            .ThenBy(s => s.index)
            .ToList();

        var kept = placed.Take(maxStacks).OrderBy(s => s.index).Select(s => new DockStack(side, s.stack)).ToList();
        foreach (var overflow in placed.Skip(maxStacks))
        {
            rail.AddRange(overflow.stack);
        }

        return kept;
    }
}
