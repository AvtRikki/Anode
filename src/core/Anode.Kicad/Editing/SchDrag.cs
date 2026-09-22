using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>One end of a wire that has to follow something being moved, and which end of it that is.</summary>
public readonly record struct WireEnd(SchWire Wire, int Index);

/// <summary>
/// Working out what follows what when something is dragged rather than moved.
///
/// Moving a part takes it away from its wires and leaves them behind, which is right when the part is being put
/// somewhere else and wrong when it is being nudged: the drawing loses its connections. Dragging keeps them — the
/// ends of the wires that met the part's pins travel with it, and the wires stretch.
///
/// What follows is decided by where things touch, which is the same rule the connectivity is read by: a wire end
/// that sits exactly on a pin, or on the end of another wire being moved, is joined to it and goes along.
/// </summary>
public static class SchDrag
{
    /// <summary>
    /// The wire ends that have to follow <paramref name="moving"/>. A wire that is itself being moved is left out —
    /// it travels whole, and stretching it as well would pull it apart.
    /// </summary>
    public static IReadOnlyList<WireEnd> Following(Schematic sheet, IReadOnlyList<SchItem> moving)
    {
        var held = Points(moving);
        if (held.Count == 0)
        {
            return [];
        }

        var carried = new HashSet<SList>(moving.Select(item => item.Node));
        var ends = new List<WireEnd>();

        foreach (var wire in sheet.Wires)
        {
            if (carried.Contains(wire.Node))
            {
                continue;
            }

            var points = wire.Points;
            for (int i = 0; i < points.Length; i++)
            {
                // Only the ends of a run follow: a corner part way along it stays where the drawing put it.
                if ((i == 0 || i == points.Length - 1) && held.Contains(points[i]))
                {
                    ends.Add(new WireEnd(wire, i));
                }
            }
        }

        return ends;
    }

    /// <summary>
    /// Every point of the moving things that a wire could be joined to: the pins of a part, the ends of a wire, and
    /// the point anything else stands at.
    /// </summary>
    private static HashSet<Vector2L> Points(IReadOnlyList<SchItem> moving)
    {
        var points = new HashSet<Vector2L>();
        foreach (var item in moving)
        {
            switch (item)
            {
                case SymbolInstance symbol when symbol.Definition is { } definition:
                    var toSheet = symbol.ToSheet;
                    foreach (var pin in definition.PinsOf(symbol.Unit, symbol.BodyStyle))
                    {
                        points.Add(toSheet.ApplyRounded(pin.Position));
                    }

                    break;

                case SchWire wire:
                    if (wire.Points is { Length: > 0 } ends)
                    {
                        points.Add(ends[0]);
                        points.Add(ends[^1]);
                    }

                    break;

                case SchSheet sheet:
                    foreach (var pin in sheet.Pins)
                    {
                        points.Add(pin.Position);
                    }

                    break;

                default:
                    points.Add(item.Position);
                    break;
            }
        }

        return points;
    }
}
