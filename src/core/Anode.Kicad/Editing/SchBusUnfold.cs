using Anode.Geometry;

namespace Anode.Kicad.Editing;

/// <summary>What unfolding a net from a bus puts on the sheet: the entry off the bus, and the label that names the net.</summary>
/// <param name="WireEnd">Where the wire from the entry starts: the entry's far end, under the label.</param>
public sealed record SchUnfolding(SchBusEntry Entry, SchLabel Label, Vector2L WireEnd);

/// <summary>
/// Breaking one net out of a bus: KiCad's Unfold from Bus (<c>SCH_LINE_WIRE_BUS_TOOL::doUnfoldBus</c>). The bus
/// offers the nets it carries; the one chosen gets a wire entry where the bus passes nearest the pointer, leaning
/// the way the pointer is, and a label at the entry's end that joins what is drawn from there to that net.
/// </summary>
public static class SchBusUnfold
{
    /// <summary>KiCad's bus entry: 100 mil each way (<c>DEFAULT_SCH_ENTRY_SIZE</c>).</summary>
    public const long EntrySizeNm = 2_540_000;

    /// <summary>
    /// The nets a bus carries, as the names written on it spell them out — a vector's range, a group's members, an
    /// alias's list — each once, in the order they are written. Empty for a bus with no name on it, which KiCad
    /// offers nothing for either ("Bus has no members").
    /// </summary>
    public static IReadOnlyList<string> Members(Schematic sheet, SchWire bus)
    {
        if (!bus.IsBus || bus.Points is not { Length: > 0 } points || SchBuses.At(sheet, points[0]) is not { } found)
        {
            return [];
        }

        var aliases = sheet.BusAliases;
        return
        [
            .. found.Names
                .Where(name => SchBusNames.IsBus(name, aliases))
                .SelectMany(name => SchBusNames.Members(name, aliases))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The entry and label for <paramref name="net"/>: the entry stands on the bus where it passes nearest
    /// <paramref name="near"/>, and leans toward <paramref name="toward"/> — right and down unless the pointer is
    /// left or above, as KiCad flips it while the pointer moves. The label reads to the right, KiCad's default.
    /// </summary>
    public static SchUnfolding? Unfold(SchWire bus, string net, Vector2L near, Vector2L toward)
    {
        var points = bus.Points;
        if (!bus.IsBus || points.Length < 2 || net.Length == 0)
        {
            return null;
        }

        var at = points[0];
        double best = double.MaxValue;
        for (int i = 1; i < points.Length; i++)
        {
            var candidate = Nearest(points[i - 1], points[i], near);
            double dx = candidate.X - near.X, dy = candidate.Y - near.Y;
            double distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                at = candidate;
            }
        }

        var size = new Vector2L(toward.X < at.X ? -EntrySizeNm : EntrySizeNm, toward.Y < at.Y ? -EntrySizeNm : EntrySizeNm);
        var end = at + size;
        var entry = SchNodes.BusEntry(at, size);
        var label = SchNodes.Label(SchLabelKind.Local, net, end);
        return new SchUnfolding(entry, label, end);
    }

    /// <summary>The point of a segment nearest another, rounded to the nanometre: on a bus that runs square, on it exactly.</summary>
    private static Vector2L Nearest(Vector2L a, Vector2L b, Vector2L p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length = (dx * dx) + (dy * dy);
        double t = length == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length, 0, 1);
        return new Vector2L((long)Math.Round(a.X + (t * dx)), (long)Math.Round(a.Y + (t * dy)));
    }
}
