using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Cutting a wire in two. One wire is one thing to the editor — it moves and deletes whole — so a run that wants
/// to be handled in halves has to become two wires, which is what KiCad's Break does.
///
/// Where the wire is cut it does not change shape: the two halves together cover exactly what the one covered, and
/// they meet at the point of the cut. Whether they are still one net is then a question for the drawing — two wires
/// meeting at a point are connected, and a dot there says so plainly.
/// </summary>
public static class SchWires
{
    /// <summary>
    /// The wire under a point: the first one the point lies on, at an end or part way along. A point at an end
    /// counts for finding a wire but not for cutting it, since there would be nothing on one side.
    /// </summary>
    public static SchWire? At(IEnumerable<SchWire> wires, Vector2L point)
    {
        foreach (var wire in wires)
        {
            var points = wire.Points;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i - 1] == point || points[i] == point || SchJunctions.IsInside(points[i - 1], points[i], point))
                {
                    return wire;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The two wires a wire becomes when it is cut at <paramref name="at"/>, or null when the point is not part way
    /// along it — cutting at an end, or off the wire altogether, would leave a wire of no length.
    ///
    /// Both halves are written as the original was: a bus stays a bus, and the stroke it was drawn with travels to
    /// each half, so cutting a dashed line does not leave one half solid.
    /// </summary>
    public static (SchWire First, SchWire Second)? Cut(SchWire wire, Vector2L at)
    {
        var points = wire.Points;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i - 1] == at || points[i] == at || !SchJunctions.IsInside(points[i - 1], points[i], at))
            {
                continue;
            }

            var before = points[..i].Append(at).ToList();
            var after = points[i..].Prepend(at).ToList();
            return (Like(wire, before), Like(wire, after));
        }

        return null;
    }

    /// <summary>A wire through the given points, written as <paramref name="like"/> was.</summary>
    private static SchWire Like(SchWire like, IReadOnlyList<Vector2L> points)
    {
        var wire = SchNodes.Wire(points, like.IsBus);

        if (like.Node.Find("stroke") is { } stroke && wire.Node.Find("stroke") is { } theirs)
        {
            int at = wire.Node.IndexOf(theirs);
            wire.Node.RemoveAt(at);
            wire.Node.Insert(at, SchNodes.Adopt(stroke));
        }

        return wire;
    }
}
