using System.Numerics;
using Anode.Editing;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

namespace Anode.Plugin.Schematic;

/// <summary>
/// What the pointer does on the sheet until another tool takes over. A tool owns no input of its own: the canvas
/// hands it sheet coordinates and it answers with a preview and, when a step is finished, with items for the editor.
/// </summary>
internal interface ISchTool
{
    string Id { get; }

    /// <summary>Primitives drawn over the sheet while the tool is working; null when it has nothing to show.</summary>
    LayerGeometry? Preview { get; }

    /// <summary>The preview changed and the canvas should draw again.</summary>
    event Action? Changed;

    void Move(Vector2L sheetPoint);

    void Click(Vector2L sheetPoint);

    /// <summary>Ends the current run (Enter): true when there was one to end.</summary>
    bool Finish();

    /// <summary>
    /// Drops what is in progress without writing anything. True when there was something to drop — which is how Esc
    /// tells "end this run" from "put the pointer back to selecting".
    /// </summary>
    bool Cancel();
}

/// <summary>
/// Places one item per click and stays armed for the next — what KiCad's no-connect and bus-entry tools do. The item
/// itself comes from <paramref name="make"/>, so the tool is the same whatever it places.
/// </summary>
internal sealed class PlaceTool(SchematicEditor editor, string id, Func<Vector2L, SchItem> make, Func<Vector2L, LayerGeometry?> preview) : ISchTool
{
    private Vector2L _cursor;

    public string Id => id;

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        _cursor = editor.Snap(sheetPoint);
        Preview = preview(_cursor);
        Changed?.Invoke();
    }

    public void Click(Vector2L sheetPoint)
    {
        var point = editor.Snap(sheetPoint);
        editor.Apply(id, [make(point)], []);
        _cursor = point;
    }

    /// <summary>There is no run to end: the tool places one item at a time.</summary>
    public bool Finish() => false;

    /// <summary>Nothing is ever in progress here, so Esc has no run to end and gives the pointer back at once.</summary>
    public bool Cancel()
    {
        Preview = null;
        Changed?.Invoke();
        return false;
    }
}

/// <summary>
/// Draws a shape in two clicks: the first sets a corner, the second finishes it, and in between the shape follows
/// the cursor. A line, a rectangle and a circle are the same gesture over two points.
/// </summary>
internal sealed class ShapeTool(SchematicEditor editor, string id, SchShapeKind kind) : ISchTool
{
    private const double StrokeMm = 0.1524;
    private const int CircleSteps = 48;

    private Vector2L _anchor;
    private Vector2L _cursor;
    private bool _started;

    public string Id => id;

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        _cursor = editor.Snap(sheetPoint);
        Rebuild();
    }

    public void Click(Vector2L sheetPoint)
    {
        var point = editor.Snap(sheetPoint);
        _cursor = point;

        if (!_started)
        {
            _anchor = point;
            _started = true;
            Rebuild();
            return;
        }

        if (point != _anchor)
        {
            editor.Apply(id, [Make(_anchor, point)], []);
        }

        _started = false;
        Rebuild();
    }

    /// <summary>A shape is finished by its second click; there is nothing else to end.</summary>
    public bool Finish() => false;

    public bool Cancel()
    {
        bool had = _started;
        _started = false;
        Preview = null;
        Changed?.Invoke();
        return had;
    }

    private SchItem Make(Vector2L from, Vector2L to) => kind switch
    {
        SchShapeKind.Rectangle => SchNodes.Rectangle(from, to),
        SchShapeKind.Circle => SchNodes.Circle(from, Radius(from, to)),
        _ => SchNodes.Polyline([from, to]),
    };

    private static long Radius(Vector2L center, Vector2L edge)
    {
        double dx = edge.X - center.X;
        double dy = edge.Y - center.Y;
        return (long)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
    }

    private void Rebuild()
    {
        if (!_started)
        {
            Preview = null;
            Changed?.Invoke();
            return;
        }

        var layer = new LayerGeometry(LayerStyle.Sch.Symbol);
        float width = (float)StrokeMm;

        switch (kind)
        {
            case SchShapeKind.Rectangle:
                var corners = new[]
                {
                    _anchor,
                    new Vector2L(_cursor.X, _anchor.Y),
                    _cursor,
                    new Vector2L(_anchor.X, _cursor.Y),
                };

                for (int i = 0; i < corners.Length; i++)
                {
                    layer.Lines.Add(new LinePrim(Scene(corners[i]), Scene(corners[(i + 1) % corners.Length]), width, -1));
                }

                break;

            case SchShapeKind.Circle:
                double radius = Radius(_anchor, _cursor);
                var previous = Scene(new Vector2L(_anchor.X + (long)radius, _anchor.Y));
                for (int i = 1; i <= CircleSteps; i++)
                {
                    double angle = 2 * Math.PI * i / CircleSteps;
                    var next = Scene(new Vector2L(
                        _anchor.X + (long)(radius * Math.Cos(angle)),
                        _anchor.Y + (long)(radius * Math.Sin(angle))));
                    layer.Lines.Add(new LinePrim(previous, next, width, -1));
                    previous = next;
                }

                break;

            default:
                layer.Lines.Add(new LinePrim(Scene(_anchor), Scene(_cursor), width, -1));
                break;
        }

        Preview = layer;
        Changed?.Invoke();
    }

    private Vector2 Scene(Vector2L point)
    {
        var mm = editor.Scene.ToSceneMm(point.ToDouble());
        return new Vector2((float)mm.X, (float)mm.Y);
    }
}

/// <summary>
/// Places something that has to be named first: a label without a name says nothing, and neither does free text.
/// The asking belongs to whoever owns the screen, so it arrives as a callback rather than as a dialog in here.
/// </summary>
internal sealed class PromptTool(
    SchematicEditor editor,
    string id,
    Func<Vector2L, Task<string?>> ask,
    Func<string, Vector2L, SchItem> make,
    Action<Exception>? onError = null) : ISchTool
{
    private bool _asking;

    public string Id => id;

    public LayerGeometry? Preview => null;

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
    }

    public void Click(Vector2L sheetPoint)
    {
        if (_asking)
        {
            return;
        }

        var point = editor.Snap(sheetPoint);
        _asking = true;
        _ = Ask(point);
    }

    public bool Finish() => false;

    public bool Cancel()
    {
        Changed?.Invoke();
        return false;
    }

    private async Task Ask(Vector2L point)
    {
        try
        {
            if (await ask(point) is { Length: > 0 } written)
            {
                editor.Apply(Id, [make(written, point)], []);
            }
        }
        catch (Exception ex)
        {
            // The task is not awaited by anyone, so an exception here would otherwise disappear without a trace.
            onError?.Invoke(ex);
        }
        finally
        {
            _asking = false;
        }
    }
}

/// <summary>
/// Draws wires and buses. The run itself is <see cref="WireRun"/>; this turns it into pointer input, a preview and
/// items in the file. Each leg is its own item, which is how KiCad stores wires.
/// </summary>
internal sealed class WireTool(SchematicEditor editor, bool bus = false) : ISchTool
{
    private const double WireWidthMm = 0.1524;
    private const double BusWidthMm = 0.3048;

    private readonly WireRun _run = new();
    private Vector2L _cursor;
    private bool _hasCursor;

    public string Id { get; } = bus ? "sch.tool.bus" : "sch.tool.wire";

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        _cursor = editor.Snap(sheetPoint);
        _hasCursor = true;
        Rebuild();
    }

    public void Click(Vector2L sheetPoint)
    {
        var point = editor.Snap(sheetPoint);
        var legs = _run.Click(point);

        _cursor = point;
        _hasCursor = true;
        Rebuild();

        if (legs.Count > 0)
        {
            // The wires, the dot and the cut are the editor's business; the tool only says where they go.
            editor.DrawWire(legs, bus);
        }
    }

    public bool Finish()
    {
        bool ended = _run.Finish();
        Rebuild();
        return ended;
    }

    public bool Cancel()
    {
        bool ended = _run.Cancel();

        // Nothing is left on the sheet and nothing is left under the cursor: the next move starts from scratch.
        _hasCursor = false;
        Preview = null;
        Changed?.Invoke();
        return ended;
    }

    /// <summary>The leg that follows the cursor, in scene millimetres.</summary>
    private void Rebuild()
    {
        var legs = _hasCursor ? _run.Preview(_cursor) : [];
        if (legs.Count == 0)
        {
            Preview = null;
            Changed?.Invoke();
            return;
        }

        var layer = new LayerGeometry(bus ? LayerStyle.Sch.Bus : LayerStyle.Sch.Wire);
        float width = (float)(bus ? BusWidthMm : WireWidthMm);

        foreach (var (from, to) in legs)
        {
            layer.Lines.Add(new LinePrim(Scene(from), Scene(to), width, -1));
        }

        Preview = layer;
        Changed?.Invoke();

        Vector2 Scene(Vector2L point)
        {
            var mm = editor.Scene.ToSceneMm(point.ToDouble());
            return new Vector2((float)mm.X, (float)mm.Y);
        }
    }
}
