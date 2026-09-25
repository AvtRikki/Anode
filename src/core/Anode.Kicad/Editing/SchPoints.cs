using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>What a handle of a shape does when it is pulled.</summary>
public enum SchHandleKind
{
    /// <summary>A point of a line, an outline or a curve: it goes where it is pulled.</summary>
    Point,

    /// <summary>A corner of a rectangle: the two sides that meet there follow it.</summary>
    Corner,

    /// <summary>The middle of a rectangle's side: that side follows it, across only.</summary>
    Side,

    /// <summary>The centre of a circle: the circle goes with it, the same size.</summary>
    Centre,

    /// <summary>A point on a circle: the circle grows or shrinks to pass through it.</summary>
    Radius,
}

/// <summary>One handle of a shape: where it stands, and what pulling it does.</summary>
public readonly record struct SchHandle(Vector2L At, SchHandleKind Kind, int Index);

/// <summary>
/// Editing the points of a drawn shape by pulling handles: KiCad's point editor (<c>sch_point_editor.cpp</c>) for
/// the sheet's graphics — lines and polylines, rule areas, curves, rectangles and circles.
///
/// Every write goes into the shape's own node, so an edit that is undone gives the file back byte for byte, and a
/// shape keeps whatever else it carries — its stroke, its fill, its id.
/// </summary>
public static class SchPoints
{
    /// <summary>The shape a handle belongs to: the item itself, or the outline inside a rule area.</summary>
    public static SchGraphic? Shape(SchItem item) => item switch
    {
        SchGraphic graphic => graphic,
        SchRuleArea area => area.Outline,
        _ => null,
    };

    /// <summary>The handles of an item, or none for one that has no points to pull.</summary>
    public static IReadOnlyList<SchHandle> Handles(SchItem item)
    {
        if (item.IsLocked || Shape(item) is not { } shape)
        {
            return [];
        }

        switch (shape.Kind)
        {
            case SchShapeKind.Polyline:
            case SchShapeKind.Bezier:
            {
                var points = Raw(shape);

                // An outline that closes on itself has its first point written twice; one handle stands for both.
                int count = Closes(points) ? points.Length - 1 : points.Length;
                return [.. points.Take(count).Select((p, i) => new SchHandle(p, SchHandleKind.Point, i))];
            }

            case SchShapeKind.Rectangle:
            {
                var (a, b) = (shape.Start, shape.End);
                return
                [
                    new(a, SchHandleKind.Corner, 0),
                    new(new Vector2L(b.X, a.Y), SchHandleKind.Corner, 1),
                    new(b, SchHandleKind.Corner, 2),
                    new(new Vector2L(a.X, b.Y), SchHandleKind.Corner, 3),
                    new(new Vector2L(Mid(a.X, b.X), a.Y), SchHandleKind.Side, 0),
                    new(new Vector2L(b.X, Mid(a.Y, b.Y)), SchHandleKind.Side, 1),
                    new(new Vector2L(Mid(a.X, b.X), b.Y), SchHandleKind.Side, 2),
                    new(new Vector2L(a.X, Mid(a.Y, b.Y)), SchHandleKind.Side, 3),
                ];
            }

            case SchShapeKind.Circle:
                // The radius handle stands to the right of the centre, where KiCad puts a circle's end point.
                return
                [
                    new(shape.Center, SchHandleKind.Centre, 0),
                    new(new Vector2L(shape.Center.X + shape.Radius, shape.Center.Y), SchHandleKind.Radius, 1),
                ];

            default:
                return [];
        }
    }

    /// <summary>
    /// Pulls a handle to <paramref name="to"/>. The ends of other lines drawn to meet this one's are pulled along —
    /// KiCad keeps two lines that meet at a corner meeting there — which is what <paramref name="sheet"/> is for.
    /// Answers every item that changed.
    /// </summary>
    public static IReadOnlyList<SchItem> Move(Schematic? sheet, SchItem item, SchHandle handle, Vector2L to)
    {
        if (Shape(item) is not { } shape)
        {
            return [];
        }

        var changed = new List<SchItem> { item };
        switch (handle.Kind)
        {
            case SchHandleKind.Point:
            {
                var joined = sheet is not null && shape.Kind == SchShapeKind.Polyline && item is SchGraphic line
                    ? Joined(sheet, line, handle.At)
                    : [];

                SetPoint(shape, handle.Index, to);
                foreach (var (other, index) in joined)
                {
                    SetPoint(other, index, to);
                    changed.Add(other);
                }

                break;
            }

            case SchHandleKind.Corner:
            {
                var (a, b) = (shape.Start, shape.End);
                (a, b) = handle.Index switch
                {
                    0 => (to, b),
                    1 => (new Vector2L(a.X, to.Y), new Vector2L(to.X, b.Y)),
                    2 => (a, to),
                    _ => (new Vector2L(to.X, a.Y), new Vector2L(b.X, to.Y)),
                };
                Set(shape.Node, "start", a);
                Set(shape.Node, "end", b);
                break;
            }

            case SchHandleKind.Side:
            {
                var (a, b) = (shape.Start, shape.End);
                (a, b) = handle.Index switch
                {
                    0 => (new Vector2L(a.X, to.Y), b),
                    1 => (a, new Vector2L(to.X, b.Y)),
                    2 => (a, new Vector2L(b.X, to.Y)),
                    _ => (new Vector2L(to.X, a.Y), b),
                };
                Set(shape.Node, "start", a);
                Set(shape.Node, "end", b);
                break;
            }

            case SchHandleKind.Centre:
                Set(shape.Node, "center", to);
                break;

            case SchHandleKind.Radius:
            {
                double dx = to.X - shape.Center.X, dy = to.Y - shape.Center.Y;
                long radius = (long)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
                (shape.Node.Find("radius") ?? throw new KiCadFormatException("A circle has no radius.")).SetNm(1, radius);
                break;
            }
        }

        foreach (var touched in changed)
        {
            touched.AfterRestore();
        }

        return changed;
    }

    /// <summary>Every item pulling a handle may change: the item, and the lines drawn to meet it there.</summary>
    public static IReadOnlyList<SchItem> Affected(Schematic sheet, SchItem item, SchHandle handle) =>
        handle.Kind == SchHandleKind.Point && item is SchGraphic { Kind: SchShapeKind.Polyline } line
            ? [item, .. Joined(sheet, line, handle.At).Select(j => (SchItem)j.Line)]
            : [item];

    /// <summary>
    /// Whether a corner can be put into the outline at <paramref name="at"/>: a line, a polyline or a rule area's
    /// outline can take one anywhere along it, as KiCad's Add Corner does. A curve cannot.
    /// </summary>
    public static bool CanAddCorner(SchItem item) =>
        !item.IsLocked && Shape(item) is { Kind: SchShapeKind.Polyline };

    /// <summary>
    /// Puts a corner into the outline where it passes nearest <paramref name="at"/>: into the side it is nearest,
    /// as KiCad does, at the point given. Answers the handle the new corner is.
    /// </summary>
    public static SchHandle? AddCorner(SchItem item, Vector2L at)
    {
        if (!CanAddCorner(item) || Shape(item) is not { } shape || shape.Node.Find("pts") is not { } pts)
        {
            return null;
        }

        var points = Raw(shape);
        if (points.Length < 2)
        {
            return null;
        }

        int best = 0;
        double nearest = double.MaxValue;
        for (int i = 0; i + 1 < points.Length; i++)
        {
            double distance = DistanceToSegment(at, points[i], points[i + 1]);
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        var xy = pts.Lists().Where(l => l.Head == "xy").ToList();
        var fresh = SchNodes.Adopt(SDocument.Parse($"(xy {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)})").Root);
        pts.Insert(pts.IndexOf(xy[best]) + 1, fresh);
        item.AfterRestore();
        return new SchHandle(at, SchHandleKind.Point, best + 1);
    }

    /// <summary>
    /// Whether a corner can be taken out: never below two points for a line, three for a rule area, whose outline
    /// has to stay an area — KiCad's limits.
    /// </summary>
    public static bool CanRemoveCorner(SchItem item, SchHandle handle)
    {
        if (item.IsLocked || handle.Kind != SchHandleKind.Point || Shape(item) is not { Kind: SchShapeKind.Polyline } shape)
        {
            return false;
        }

        var points = Raw(shape);
        int distinct = Closes(points) ? points.Length - 1 : points.Length;
        return distinct > (item is SchRuleArea ? 3 : 2);
    }

    /// <summary>Takes a corner out; an outline that closes on itself stays closed when its first corner goes.</summary>
    public static bool RemoveCorner(SchItem item, SchHandle handle)
    {
        if (!CanRemoveCorner(item, handle) || Shape(item) is not { } shape || shape.Node.Find("pts") is not { } pts)
        {
            return false;
        }

        var points = Raw(shape);
        bool closes = Closes(points);
        var xy = pts.Lists().Where(l => l.Head == "xy").ToList();

        pts.RemoveAt(pts.IndexOf(xy[handle.Index]));
        if (closes && handle.Index == 0)
        {
            // The closing point was the first one again; it becomes the new first.
            xy[^1].SetPoint(points[1]);
        }

        item.AfterRestore();
        return true;
    }

    /// <summary>
    /// Other lines of the sheet with an end exactly where this one's handle stands — two-point lines only, as KiCad
    /// joins only its graphic lines, never a polyline's corners.
    /// </summary>
    private static List<(SchGraphic Line, int Index)> Joined(Schematic sheet, SchGraphic line, Vector2L at)
    {
        var joined = new List<(SchGraphic, int)>();
        if (Raw(line).Length != 2)
        {
            return joined;
        }

        foreach (var other in sheet.Graphics)
        {
            if (ReferenceEquals(other.Node, line.Node) || other.Kind != SchShapeKind.Polyline || other.IsLocked)
            {
                continue;
            }

            var points = Raw(other);
            if (points.Length != 2)
            {
                continue;
            }

            if (points[0] == at)
            {
                joined.Add((other, 0));
            }
            else if (points[1] == at)
            {
                joined.Add((other, 1));
            }
        }

        return joined;
    }

    /// <summary>The points as written, without the arcs a polyline may carry turned into segments.</summary>
    private static Vector2L[] Raw(SchGraphic shape) =>
        shape.Node.Find("pts") is { } pts ? [.. pts.Lists().Where(l => l.Head == "xy").Select(l => l.Point())] : [];

    private static bool Closes(Vector2L[] points) => points.Length > 2 && points[0] == points[^1];

    /// <summary>Writes a point of a polyline or a curve; the closing point of an outline follows its first.</summary>
    private static void SetPoint(SchGraphic shape, int index, Vector2L to)
    {
        if (shape.Node.Find("pts") is not { } pts)
        {
            throw new KiCadFormatException("A shape has no points.");
        }

        var xy = pts.Lists().Where(l => l.Head == "xy").ToList();
        bool closes = Closes([.. xy.Select(l => l.Point())]);
        xy[index].SetPoint(to);
        if (closes && index == 0)
        {
            xy[^1].SetPoint(to);
        }
    }

    private static void Set(SList node, string head, Vector2L to) =>
        (node.Find(head) ?? throw new KiCadFormatException($"The shape has no {head}.")).SetPoint(to);

    private static long Mid(long a, long b) => a + ((b - a) / 2);

    private static double DistanceToSegment(Vector2L p, Vector2L a, Vector2L b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length = (dx * dx) + (dy * dy);
        double t = length == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length, 0, 1);
        double x = a.X + (t * dx) - p.X, y = a.Y + (t * dy) - p.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
