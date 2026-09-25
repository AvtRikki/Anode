using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad;

/// <summary>
/// One bus of a sheet: the bus wires that run together, what is written on them, and what hangs off them.
/// </summary>
/// <param name="Names">The bus names written on it — <c>ADR[0..7]</c>, <c>top{a_xyz}</c> — as the labels say them.</param>
public sealed record SchBus(IReadOnlyList<string> Names, IReadOnlyList<SchLabel> Labels, IReadOnlyList<SchSheetPin> SheetPins);

/// <summary>
/// The buses of a sheet, found the way its nets are: bus wires that meet are one bus, and a label or a sheet pin
/// that lands on one belongs to it.
///
/// A bus is not a net and is deliberately kept apart from one: it carries several nets at once, and joining what it
/// touches would fuse them. What it is for is saying which bus a thing is on — so that the members a label declares
/// can be matched with the pin of the sheet the bus runs into.
/// </summary>
public static class SchBuses
{
    public static IReadOnlyList<SchBus> Of(Schematic sheet) => Build(sheet, only: null);

    /// <summary>The bus a point is on — at an end of a bus wire or part way along one — or null.</summary>
    public static SchBus? At(Schematic sheet, Vector2L point) => Build(sheet, only: point).FirstOrDefault();

    private static IReadOnlyList<SchBus> Build(Schematic sheet, Vector2L? only)
    {
        var segments = new List<(Vector2L A, Vector2L B)>();
        foreach (var wire in sheet.Wires.Where(w => w.IsBus))
        {
            var points = wire.Points;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i - 1] != points[i])
                {
                    segments.Add((points[i - 1], points[i]));
                }
            }
        }

        // A bus entry joins a wire to the bus it lands on; its bus end is part of the bus.
        foreach (var entry in sheet.BusEntries)
        {
            segments.Add((entry.Position, entry.EndPoint));
        }

        if (segments.Count == 0)
        {
            return [];
        }

        var groups = new PointGroups();
        foreach (var (a, b) in segments)
        {
            groups.Join(a, b);
        }

        // A dot on a bus ties what passes through it to what ends there, as it does on a wire.
        foreach (var dot in sheet.Junctions)
        {
            foreach (var (a, b) in segments)
            {
                if (SchJunctions.IsInside(a, b, dot.Position))
                {
                    groups.Join(dot.Position, a);
                }
            }
        }

        int? wanted = only is { } point ? On(segments, groups, point) : null;
        if (only is not null && wanted is null)
        {
            return [];
        }

        var labels = new Dictionary<int, List<SchLabel>>();
        var pins = new Dictionary<int, List<SchSheetPin>>();

        foreach (var label in sheet.Labels)
        {
            if (On(segments, groups, label.Position) is { } group)
            {
                Bucket(labels, group).Add(label);
            }
        }

        foreach (var pin in sheet.Sheets.SelectMany(s => s.Pins))
        {
            if (On(segments, groups, pin.Position) is { } group)
            {
                Bucket(pins, group).Add(pin);
            }
        }

        var buses = new List<SchBus>();
        foreach (int group in labels.Keys.Concat(pins.Keys).Distinct().Where(g => wanted is null || g == wanted))
        {
            var onIt = labels.GetValueOrDefault(group) ?? [];
            var reaching = pins.GetValueOrDefault(group) ?? [];
            buses.Add(new SchBus([.. onIt.Select(l => l.Shown).Distinct(StringComparer.Ordinal)], onIt, reaching));
        }

        return buses;
    }

    /// <summary>The bus group a point sits on — at an end of a segment or part way along one — or none.</summary>
    private static int? On(List<(Vector2L A, Vector2L B)> segments, PointGroups groups, Vector2L point)
    {
        foreach (var (a, b) in segments)
        {
            if (a == point || b == point || SchJunctions.IsInside(a, b, point))
            {
                return groups.Of(a);
            }
        }

        return null;
    }

    private static List<T> Bucket<T>(Dictionary<int, List<T>> buckets, int key)
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            buckets[key] = bucket = [];
        }

        return bucket;
    }
}
