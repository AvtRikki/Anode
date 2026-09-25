using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Groups on a sheet, by KiCad's rules (<c>sch_group_tool.cpp</c>, <c>group_tool.cpp</c>, the selection tool): what a
/// click takes, what grouping and ungrouping write, and what is left of a group when its members go.
///
/// A group names its members by id. An item belongs to at most one group, and a group may itself be a member —
/// so a click takes the outermost group an item is in, unless the reader has gone into a group, when it takes the
/// outermost one inside that.
/// </summary>
public static class SchGroups
{
    /// <summary>The group an item is a member of directly, or null.</summary>
    public static SchGroup? Parent(Schematic sheet, SchItem item) =>
        item.Uuid is { } id ? sheet.Groups.FirstOrDefault(g => g.Members.Contains(id, StringComparer.Ordinal)) : null;

    /// <summary>
    /// The outermost group an item is in, stopping inside <paramref name="entered"/>: the group a click on the item
    /// takes. Null when the item is in no group, or only in the one that was entered.
    /// </summary>
    public static SchGroup? Top(Schematic sheet, SchItem item, SchGroup? entered = null)
    {
        SchGroup? top = null;
        var seen = new HashSet<SList>();
        for (var group = Parent(sheet, item); group is not null && seen.Add(group.Node); group = Parent(sheet, group))
        {
            if (entered is not null && ReferenceEquals(group.Node, entered.Node))
            {
                break;
            }

            top = group;
        }

        return top;
    }

    /// <summary>Every drawn item of a group, through the groups inside it.</summary>
    public static IReadOnlyList<SchItem> Leaves(Schematic sheet, SchGroup group)
    {
        var byId = Index(sheet);
        var leaves = new List<SchItem>();
        var seen = new HashSet<SList>();
        Collect(group);
        return leaves;

        void Collect(SchGroup at)
        {
            if (!seen.Add(at.Node))
            {
                return;
            }

            foreach (string id in at.Members)
            {
                switch (byId.GetValueOrDefault(id))
                {
                    case SchGroup inner:
                        Collect(inner);
                        break;
                    case { } item:
                        leaves.Add(item);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// What a click on <paramref name="item"/> selects: the whole of the outermost group it is in, or the item alone.
    /// </summary>
    public static IReadOnlyList<SchItem> Selecting(Schematic sheet, SchItem item, SchGroup? entered = null) =>
        Top(sheet, item, entered) is { } group ? Leaves(sheet, group) : [item];

    /// <summary>
    /// What grouping <paramref name="selected"/> takes into the new group: each item, or the outermost group it is
    /// in, once — so grouping two groups nests them rather than taking them apart. Items already in the entered
    /// group are taken themselves.
    /// </summary>
    public static IReadOnlyList<SchItem> Grouping(Schematic sheet, IEnumerable<SchItem> selected, SchGroup? entered = null)
    {
        var members = new List<SchItem>();
        var seen = new HashSet<SList>();
        foreach (var item in selected)
        {
            SchItem member = Top(sheet, item, entered) ?? item;
            if (member.Uuid is not null && seen.Add(member.Node))
            {
                members.Add(member);
            }
        }

        return members;
    }

    /// <summary>A new group of the given members, written as KiCad writes one: unnamed, its member ids sorted.</summary>
    public static SchGroup New(IEnumerable<SchItem> members, string name = "")
    {
        var ids = members.Select(m => m.Uuid).OfType<string>().Order(StringComparer.Ordinal);
        string text = $"(group {SEscape.Quote(name)} (uuid \"{Guid.NewGuid()}\") (members {string.Join(' ', ids.Select(SEscape.Quote))}))";
        return new SchGroup(SchNodes.Adopt(SDocument.Parse(text).Root));
    }

    /// <summary>Takes members out of a group's list. The group node is changed in place.</summary>
    public static void Remove(SchGroup group, IReadOnlyCollection<string> ids)
    {
        if (group.Node.Find("members") is not { } members)
        {
            return;
        }

        for (int i = members.Count - 1; i >= 1; i--)
        {
            if (members[i] is SAtom atom && ids.Contains(atom.Value))
            {
                members.RemoveAt(i);
            }
        }
    }

    /// <summary>Adds members to a group's list, keeping it sorted as KiCad writes it.</summary>
    public static void Add(SchGroup group, IEnumerable<string> ids)
    {
        var all = group.Members.Concat(ids).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var fresh = SchNodes.Adopt(SDocument.Parse($"(members {string.Join(' ', all.Select(SEscape.Quote))})").Root);
        if (group.Node.Find("members") is { } members)
        {
            int at = group.Node.IndexOf(members);
            group.Node.RemoveAt(at);
            group.Node.Insert(at, fresh);
        }
        else
        {
            group.Node.Add(fresh);
        }
    }

    /// <summary>
    /// The groups that lose members when <paramref name="going"/> leaves the sheet: those left with some keep the
    /// rest, those left with none go too — KiCad never writes an empty group. A group emptied that way is itself a
    /// member going, so its own group is looked at again.
    /// </summary>
    public static (IReadOnlyList<SchGroup> Shrinking, IReadOnlyList<SchGroup> Emptied) Losing(Schematic sheet, IReadOnlyCollection<SchItem> going)
    {
        var gone = going.Select(i => i.Uuid).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var emptied = new List<SchGroup>();
        var shrinking = new List<SchGroup>();

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var group in sheet.Groups)
            {
                if (emptied.Contains(group) || !group.Members.Any(gone.Contains))
                {
                    continue;
                }

                if (group.Members.All(gone.Contains))
                {
                    emptied.Add(group);
                    shrinking.Remove(group);
                    if (group.Uuid is { } id && gone.Add(id))
                    {
                        changed = true;
                    }
                }
                else if (!shrinking.Contains(group))
                {
                    shrinking.Add(group);
                }
            }
        }

        return (shrinking, emptied);
    }

    private static Dictionary<string, SchItem> Index(Schematic sheet)
    {
        var byId = new Dictionary<string, SchItem>(StringComparer.Ordinal);
        foreach (var item in sheet.Items.Concat(sheet.Groups))
        {
            if (item.Uuid is { } id)
            {
                byId.TryAdd(id, item);
            }
        }

        return byId;
    }
}
