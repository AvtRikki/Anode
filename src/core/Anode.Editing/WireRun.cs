using Anode.Geometry;

namespace Anode.Editing;

/// <summary>
/// Drawing a run of wires, as state rather than as input handling: the points clicked so far, and what each further
/// click turns into. It is kept out of the canvas because "Esc leaves nothing behind" and "a click writes the legs up
/// to it" are rules about this state, and rules deserve tests rather than a hand on a mouse.
///
/// Coordinates are sheet nanometres, already snapped by whoever calls in.
/// </summary>
public sealed class WireRun
{
    private readonly List<Vector2L> _points = [];

    /// <summary>A run is in progress: there is a point the next leg starts from.</summary>
    public bool IsRunning => _points.Count > 0;

    /// <summary>Where the next leg starts; meaningless while nothing is running.</summary>
    public Vector2L Start => _points.Count > 0 ? _points[^1] : default;

    /// <summary>
    /// A click: the first one starts the run, each later one returns the legs to write and carries the run on from
    /// the point clicked. Clicking where the run already stands ends it and writes nothing.
    /// </summary>
    public IReadOnlyList<(Vector2L From, Vector2L To)> Click(Vector2L point)
    {
        if (_points.Count == 0)
        {
            _points.Add(point);
            return [];
        }

        if (point == _points[^1])
        {
            Finish();
            return [];
        }

        var legs = Legs(_points[^1], point);
        _points.Clear();
        _points.Add(point);
        return legs;
    }

    /// <summary>Ends the run. The legs were written as they were clicked, so there is nothing left to write.</summary>
    public bool Finish()
    {
        bool running = _points.Count > 0;
        _points.Clear();
        return running;
    }

    /// <summary>Drops the run; true when there was one to drop, which is how Esc tells "ended" from "leave the tool".</summary>
    public bool Cancel() => Finish();

    /// <summary>The legs that a click at <paramref name="cursor"/> would write: what the preview draws.</summary>
    public IReadOnlyList<(Vector2L From, Vector2L To)> Preview(Vector2L cursor) =>
        _points.Count == 0 ? [] : Legs(_points[^1], cursor);

    /// <summary>
    /// An orthogonal run from one point to the other: the longer axis is travelled first, which is the corner KiCad
    /// picks. Points on one line give a single leg, and a leg of no length is never returned.
    /// </summary>
    public static IReadOnlyList<(Vector2L From, Vector2L To)> Legs(Vector2L from, Vector2L to)
    {
        if (from == to)
        {
            return [];
        }

        if (from.X == to.X || from.Y == to.Y)
        {
            return [(from, to)];
        }

        var corner = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y)
            ? new Vector2L(to.X, from.Y)
            : new Vector2L(from.X, to.Y);

        return [(from, corner), (corner, to)];
    }
}
