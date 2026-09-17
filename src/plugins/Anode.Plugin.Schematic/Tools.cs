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
            editor.Add([.. legs.Select(leg => SchNodes.Wire([leg.From, leg.To], bus))]);
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
