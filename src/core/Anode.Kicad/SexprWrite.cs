using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>In-place writes to CST lists. Only atom text changes, so surrounding whitespace is preserved.</summary>
internal static class SexprWrite
{
    public static void SetNm(this SList list, int index, long nm) =>
        (list.AtomAt(index) ?? throw new KiCadFormatException($"({list.Head} ...) has no value at position {index}."))
        .SetSymbol(KiCadNumber.FormatMm(nm));

    public static void SetPoint(this SList list, Vector2L point, int index = 1)
    {
        list.SetNm(index, point.X);
        list.SetNm(index + 1, point.Y);
    }

    public static void MapPoint(this SList list, Func<Vector2L, Vector2L> map, int index = 1) =>
        list.SetPoint(map(list.Point(index)), index);

    public static void MapChildPoint(this SList list, string head, Func<Vector2L, Vector2L> map)
    {
        if (list.Find(head) is { } child)
        {
            child.MapPoint(map);
        }
    }

    /// <summary>Maps every <c>(xy ...)</c>, and the start/mid/end of embedded <c>(arc ...)</c> entries, in the subtree.</summary>
    public static void MapAllPoints(this SList list, Func<Vector2L, Vector2L> map)
    {
        foreach (var child in list.Lists())
        {
            if (child.Head == "xy" || (child.Head is "start" or "mid" or "end" && list.Head == "arc"))
            {
                child.MapPoint(map);
            }
            else
            {
                child.MapAllPoints(map);
            }
        }
    }

    /// <summary>
    /// Writes an optional angle value such as the third number of <c>(at x y angle)</c>, inserting it when absent.
    /// With <paramref name="omitWhenZero"/> a zero angle is removed, as KiCad writes pads and legacy footprints.
    /// </summary>
    public static void SetAngle(this SList list, int index, double degrees, bool omitWhenZero)
    {
        bool zero = Math.Abs(degrees) < 1e-9;
        bool present = list.AtomAt(index) is { } atom && atom.TryGetDouble(out _);

        if (present)
        {
            if (zero && omitWhenZero)
            {
                list.RemoveAt(index);
            }
            else
            {
                list.AtomAt(index)!.SetSymbol(KiCadNumber.FormatAngle(degrees));
            }
        }
        else if (!(zero && omitWhenZero))
        {
            list.Insert(Math.Min(index, list.Count), SAtom.Symbol(KiCadNumber.FormatAngle(degrees)));
        }
    }
}
