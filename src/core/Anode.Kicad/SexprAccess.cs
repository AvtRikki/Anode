using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>Typed reads from CST lists. Nothing here mutates the tree.</summary>
internal static class SexprAccess
{
    public static double Double(this SList list, int index, double fallback = 0) =>
        list.AtomAt(index) is { } a && a.TryGetDouble(out var v) ? v : fallback;

    public static long Nm(this SList list, int index) => Units.MmToNm(list.Double(index));

    public static string? Str(this SList list, int index) => list.AtomAt(index)?.Value;

    public static Vector2L Point(this SList list, int index = 1) => new(list.Nm(index), list.Nm(index + 1));

    public static Vector2L? ChildPoint(this SList list, string head) => list.Find(head) is { } c ? c.Point() : null;

    public static long? ChildNm(this SList list, string head) => list.Find(head) is { Count: > 1 } c ? c.Nm(1) : null;

    public static double? ChildDouble(this SList list, string head) =>
        list.Find(head) is { Count: > 1 } c && c.AtomAt(1) is { } a && a.TryGetDouble(out var v) ? v : null;

    public static string? ChildString(this SList list, string head) => list.Find(head)?.Str(1);

    /// <summary>Reads <c>(head yes|no)</c>; a bare <c>(head)</c> counts as true (older files).</summary>
    public static bool ChildBool(this SList list, string head, bool fallback = false)
    {
        if (list.Find(head) is not { } c)
        {
            return fallback;
        }

        return c.Count == 1 || c.AtomAt(1)?.Raw is "yes" or "true";
    }

    /// <summary>Names from <c>(layer "X")</c> or <c>(layers "X" "Y" ...)</c>.</summary>
    public static IReadOnlyList<string> LayerNames(this SList list)
    {
        if (list.Find("layers") is { } layers)
        {
            var names = new string[layers.Count - 1];
            for (int i = 1; i < layers.Count; i++)
            {
                names[i - 1] = layers.Str(i) ?? string.Empty;
            }

            return names;
        }

        return list.ChildString("layer") is { } layer ? [layer] : [];
    }

    /// <summary>Vertices of a <c>(pts ...)</c> list; embedded <c>(arc ...)</c> entries are tessellated.</summary>
    public static Vector2L[] Points(this SList pts, double maxError = ArcMath.DefaultMaxErrorNm)
    {
        var result = new List<Vector2L>(pts.Count);
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i] is not SList item)
            {
                continue;
            }

            if (item.Head == "xy")
            {
                result.Add(item.Point());
            }
            else if (item.Head == "arc"
                     && item.ChildPoint("start") is { } s && item.ChildPoint("mid") is { } m && item.ChildPoint("end") is { } e)
            {
                if (ArcMath.FromStartMidEnd(s.ToDouble(), m.ToDouble(), e.ToDouble()) is { } arc)
                {
                    foreach (var p in ArcMath.Tessellate(arc, maxError))
                    {
                        result.Add(p.Round());
                    }
                }
                else
                {
                    result.Add(s);
                    result.Add(e);
                }
            }
        }

        return [.. result];
    }
}
