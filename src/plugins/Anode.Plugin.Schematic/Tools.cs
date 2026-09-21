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
                ToolDraw.Rectangle(layer, editor, _anchor, _cursor, width);
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

    private Vector2 Scene(Vector2L point) => ToolDraw.Scene(editor, point);
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
/// Places a part that was chosen somewhere else — the symbols panel. The tool itself carries no chooser: it knows
/// the part and drops a copy of it wherever it is clicked, and stays armed so a row of them can be laid down.
/// </summary>
internal sealed class SymbolTool(SchematicEditor editor, string libId, LibSymbol definition, Action<string, LibSymbol, Vector2L> place) : ISchTool
{
    public string Id => "sch.tool.symbol";

    /// <summary>What is chosen, so the panel can mark the row and the button can say what it would place.</summary>
    public string LibId => libId;

    public LayerGeometry? Preview => null;

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
    }

    public void Click(Vector2L sheetPoint) => place(libId, definition, editor.Snap(sheetPoint));

    /// <summary>A part is placed by its click; there is no run to end.</summary>
    public bool Finish() => false;

    public bool Cancel()
    {
        Changed?.Invoke();
        return false;
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

/// <summary>
/// Draws the rectangle of a child sheet in two clicks and then hands it over to be named and written. The tool
/// knows nothing of files: a sheet is a schematic of its own on disk, and making one belongs to the document.
/// </summary>
internal sealed class SheetTool(SchematicEditor editor, Func<Vector2L, Vector2L, Task> place, Action<Exception>? onError = null) : ISchTool
{
    private const double StrokeMm = 0.1524;

    private Vector2L _anchor;
    private Vector2L _cursor;
    private bool _started;
    private bool _placing;

    /// <summary>What this tool is called: the same gesture draws a child sheet and a box of words.</summary>
    public string Id { get; init; } = "sch.tool.sheet";

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        _cursor = editor.Snap(sheetPoint);
        Rebuild();
    }

    public void Click(Vector2L sheetPoint)
    {
        if (_placing)
        {
            return;
        }

        var point = editor.Snap(sheetPoint);
        _cursor = point;

        if (!_started)
        {
            _anchor = point;
            _started = true;
            Rebuild();
            return;
        }

        var corner = new Vector2L(Math.Min(_anchor.X, point.X), Math.Min(_anchor.Y, point.Y));
        var size = new Vector2L(Math.Abs(point.X - _anchor.X), Math.Abs(point.Y - _anchor.Y));

        // A sheet with no width or no height is not a sheet; the second click just has not landed yet.
        if (size.X > 0 && size.Y > 0)
        {
            _started = false;
            _placing = true;
            Preview = null;
            Changed?.Invoke();
            _ = Place(corner, size);
        }
    }

    public bool Finish() => false;

    public bool Cancel()
    {
        bool had = _started;
        _started = false;
        Preview = null;
        Changed?.Invoke();
        return had;
    }

    private async Task Place(Vector2L corner, Vector2L size)
    {
        try
        {
            await place(corner, size);
        }
        catch (Exception ex)
        {
            // Nobody awaits this, so an exception would otherwise disappear without a trace.
            onError?.Invoke(ex);
        }
        finally
        {
            _placing = false;
        }
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
        ToolDraw.Rectangle(layer, editor, _anchor, _cursor, (float)StrokeMm);
        Preview = layer;
        Changed?.Invoke();
    }
}

/// <summary>
/// Puts a pin on the edge of a child sheet. A pin never floats: wherever it is clicked, it lands on the edge it is
/// nearest, which is where a wire can meet it — so the preview shows the point it would take rather than the cursor.
/// </summary>
internal sealed class SheetPinTool(
    SchematicEditor editor,
    Func<Vector2L, SchSheet?> find,
    Func<SchSheet, Vector2L, SheetSide, Task> place,
    Action<Exception>? onError = null) : ISchTool
{
    private const double StrokeMm = 0.1524;
    private const long TickNm = 1_270_000;

    private bool _placing;

    public string Id => "sch.tool.sheetPin";

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        Preview = null;
        if (find(sheetPoint) is { } sheet)
        {
            var at = SchSheets.OnEdge(sheet, editor.Snap(sheetPoint), out var side);
            var layer = new LayerGeometry(LayerStyle.Sch.Symbol);
            var away = side switch
            {
                SheetSide.Left => new Vector2L(at.X - TickNm, at.Y),
                SheetSide.Right => new Vector2L(at.X + TickNm, at.Y),
                SheetSide.Top => new Vector2L(at.X, at.Y - TickNm),
                _ => new Vector2L(at.X, at.Y + TickNm),
            };

            layer.Lines.Add(new LinePrim(ToolDraw.Scene(editor, at), ToolDraw.Scene(editor, away), (float)StrokeMm, -1));
            Preview = layer;
        }

        Changed?.Invoke();
    }

    public void Click(Vector2L sheetPoint)
    {
        if (_placing || find(sheetPoint) is not { } sheet)
        {
            return;
        }

        var at = SchSheets.OnEdge(sheet, editor.Snap(sheetPoint), out var side);
        _placing = true;
        _ = Place(sheet, at, side);
    }

    public bool Finish() => false;

    public bool Cancel()
    {
        Preview = null;
        Changed?.Invoke();
        return false;
    }

    private async Task Place(SchSheet sheet, Vector2L at, SheetSide side)
    {
        try
        {
            await place(sheet, at, side);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
        finally
        {
            _placing = false;
        }
    }
}

/// <summary>
/// Collects a fixed number of points, one per click, and then makes something of them — an arc through three, a
/// curve through four. Between clicks the shape follows the pointer, drawn from the points so far and the one it
/// is over, so what will be made is what is shown.
/// </summary>
internal sealed class PointsTool(
    SchematicEditor editor,
    string id,
    int wanted,
    Func<IReadOnlyList<Vector2L>, SchItem> make,
    Func<IReadOnlyList<Vector2L>, IReadOnlyList<Vector2D>> shape) : ISchTool
{
    private const double StrokeMm = 0.1524;

    private readonly List<Vector2L> _points = [];
    private Vector2L _cursor;

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
        _points.Add(editor.Snap(sheetPoint));
        if (_points.Count < wanted)
        {
            Rebuild();
            return;
        }

        var made = make(_points);
        _points.Clear();
        Preview = null;
        editor.Apply(id, [made], []);
    }

    /// <summary>A shape needs all its points; there is nothing to finish early.</summary>
    public bool Finish() => false;

    public bool Cancel()
    {
        bool had = _points.Count > 0;
        _points.Clear();
        Preview = null;
        Changed?.Invoke();
        return had;
    }

    private void Rebuild()
    {
        Preview = null;
        if (_points.Count > 0)
        {
            var so_far = _points.Append(_cursor).ToList();
            var layer = new LayerGeometry(LayerStyle.Sch.Symbol);
            var drawn = shape(so_far);
            for (int i = 1; i < drawn.Count; i++)
            {
                layer.Lines.Add(new LinePrim(
                    ToolDraw.Scene(editor, drawn[i - 1].Round()),
                    ToolDraw.Scene(editor, drawn[i].Round()),
                    (float)StrokeMm,
                    -1));
            }

            Preview = layer;
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// Cuts a wire in two where it is clicked. The point is snapped to the grid like any other, and a cut at an end of
/// a wire does nothing: there would be nothing on one side of it.
/// </summary>
internal sealed class CutTool(SchematicEditor editor, Func<Vector2L, SchWire?> find, Action<SchWire, Vector2L> cut) : ISchTool
{
    private const double StrokeMm = 0.1524;
    private const long ArmNm = 635_000;

    public string Id => "sch.tool.cut";

    public LayerGeometry? Preview { get; private set; }

    public event Action? Changed;

    public void Move(Vector2L sheetPoint)
    {
        Preview = null;
        var at = editor.Snap(sheetPoint);

        // The mark shows where the cut would fall, which is not always where the pointer is.
        if (find(at) is not null)
        {
            var layer = new LayerGeometry(LayerStyle.Sch.NoConnect);
            layer.Lines.Add(new LinePrim(
                ToolDraw.Scene(editor, new Vector2L(at.X, at.Y - ArmNm)),
                ToolDraw.Scene(editor, new Vector2L(at.X, at.Y + ArmNm)),
                (float)StrokeMm,
                -1));
            Preview = layer;
        }

        Changed?.Invoke();
    }

    public void Click(Vector2L sheetPoint)
    {
        var at = editor.Snap(sheetPoint);
        if (find(at) is { } wire)
        {
            cut(wire, at);
        }
    }

    public bool Finish() => false;

    public bool Cancel()
    {
        Preview = null;
        Changed?.Invoke();
        return false;
    }
}

/// <summary>What a tool draws over the sheet while it works, in the canvas's own coordinates.</summary>
internal static class ToolDraw
{
    public static Vector2 Scene(SchematicEditor editor, Vector2L point)
    {
        var mm = editor.Scene.ToSceneMm(point.ToDouble());
        return new Vector2((float)mm.X, (float)mm.Y);
    }

    public static void Rectangle(LayerGeometry layer, SchematicEditor editor, Vector2L a, Vector2L b, float width)
    {
        Vector2L[] corners = [a, new(b.X, a.Y), b, new(a.X, b.Y)];
        for (int i = 0; i < corners.Length; i++)
        {
            layer.Lines.Add(new LinePrim(Scene(editor, corners[i]), Scene(editor, corners[(i + 1) % corners.Length]), width, -1));
        }
    }
}
