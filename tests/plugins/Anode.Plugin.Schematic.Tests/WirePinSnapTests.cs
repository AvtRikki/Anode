using Anode.Geometry;
using Anode.Kicad;
using Anode.Render;

using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

public class WirePinSnapTests
{
    [Fact]
    public void Nearby_pin_is_used_for_preview_and_committed_wire()
    {
        var scene = Sheet();
        var editor = new Anode.Editing.SchematicEditor(scene);
        var tool = new WireTool(editor) { SnapRadiusNm = 500_000 };
        var pin = Pin(scene);
        var near = pin + new Vector2L(300_000, 0);

        tool.Move(near);
        Assert.NotNull(tool.Preview);
        Assert.Equal(2, tool.Preview.Lines.Count); // Cross at the chosen pin.
        tool.Click(near);
        tool.Click(pin + new Vector2L(2_540_000, 0));

        Assert.Contains(scene.Schematic.Wires, wire => wire.Points[0] == pin);
    }

    [Fact]
    public void Pointer_outside_radius_uses_grid_and_has_no_pin_marker()
    {
        var scene = Sheet();
        var editor = new Anode.Editing.SchematicEditor(scene);
        var tool = new WireTool(editor) { SnapRadiusNm = 200_000 };
        var pin = Pin(scene);
        var away = pin + new Vector2L(500_000, 0);

        tool.Move(away);
        Assert.Null(tool.Preview);
        tool.Click(away);
        tool.Click(away + new Vector2L(2_540_000, 0));

        Assert.Contains(scene.Schematic.Wires, wire => wire.Points[0] == editor.Snap(away));
    }

    [Fact]
    public void Tab_switches_the_corner_of_a_running_wire()
    {
        var scene = Sheet();
        var tool = new WireTool(new Anode.Editing.SchematicEditor(scene));
        var start = new Vector2L(10_160_000, 10_160_000);
        var end = new Vector2L(20_320_000, 15_240_000);

        tool.Click(start);
        tool.Move(end);
        var earlyCorner = Assert.Single(tool.Preview!.Lines.Skip(1)).A;

        tool.FlipCorner();
        var otherCorner = Assert.Single(tool.Preview!.Lines.Skip(1)).A;

        Assert.NotEqual(earlyCorner, otherCorner);
        tool.Click(end);
        Assert.Equal(2, scene.Schematic.Wires.Count);
    }

    /// <summary>
    /// With nothing in the way the corner is the one <see cref="Anode.Editing.WireRun.Legs"/> picks on its own —
    /// the longer axis first, which is KiCad's. A tool that preferred the other side would bend every wire the
    /// wrong way, and no test of obstacle avoidance would notice: avoiding something flips the corner anyway.
    /// </summary>
    [Fact]
    public void With_nothing_in_the_way_the_corner_is_the_one_the_run_would_pick()
    {
        var scene = Empty();
        var tool = new WireTool(new Anode.Editing.SchematicEditor(scene));
        var from = new Vector2L(20_320_000, 20_320_000);
        var to = new Vector2L(40_640_000, 25_400_000);

        tool.Click(from);
        tool.Move(to);

        var first = tool.Preview!.Lines[0];
        var legs = Anode.Editing.WireRun.Legs(from, to);

        Assert.Equal(legs[0].From.Y == legs[0].To.Y, Math.Abs(first.A.Y - first.B.Y) < 0.0001f);
    }

    /// <summary>
    /// The route goes round a part rather than through it. The part needs a body for this to mean anything: what
    /// is in the way is what is drawn, and a symbol that is only a pin covers almost nothing.
    /// </summary>
    [Fact]
    public void Route_prefers_the_corner_that_avoids_a_symbol()
    {
        var scene = WithBody();
        var tool = new WireTool(new Anode.Editing.SchematicEditor(scene));

        // The body stands at 50.8 and is twenty millimetres across. From the left of it, level with its middle, to
        // a point beyond it and lower down: the longer axis is across, so the corner the run picks on its own runs
        // the first leg straight through the body, while the other way round misses it on both legs.
        var start = new Vector2L(25_400_000, 50_800_000);
        var end = new Vector2L(76_200_000, 76_200_000);

        var through = Anode.Editing.WireRun.Legs(start, end);
        Assert.Equal(through[0].From.Y, through[0].To.Y);

        tool.Click(start);
        tool.Move(end);

        // So the tool takes the other one: down the far side first, then across below the part.
        var first = tool.Preview!.Lines[0];
        Assert.NotEqual(first.A.Y, first.B.Y);
    }

    private static Vector2L Pin(SchematicScene scene)
    {
        var symbol = Assert.Single(scene.Schematic.Symbols);
        var pin = Assert.Single(symbol.Definition!.PinsOf(symbol.Unit, symbol.BodyStyle));
        return symbol.ToSheet.ApplyRounded(pin.Position);
    }

    /// <summary>A sheet with nothing on it at all.</summary>
    private static SchematicScene Empty() => SchematicSceneBuilder.Build(KicadSchematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\")"
        + " (paper \"A4\") (lib_symbols) (embedded_fonts no))"));

    /// <summary>A part with a body wide enough to be in the way of a wire.</summary>
    private static SchematicScene WithBody() => SchematicSceneBuilder.Build(KicadSchematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "(lib_symbols (symbol \"Device:Box\" (property \"Reference\" \"U\" (at 0 0 0))"
        + " (symbol \"Box_1_1\" (rectangle (start -10.16 -10.16) (end 10.16 10.16)"
        + " (stroke (width 0.254) (type solid)) (fill (type none))))))\n"
        + "(symbol (lib_id \"Device:Box\") (at 50.8 50.8 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000002\")"
        + " (property \"Reference\" \"U1\" (at 50.8 38 0))) (embedded_fonts no))"));

    private static SchematicScene Sheet() => SchematicSceneBuilder.Build(KicadSchematic.Parse(
        "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
        + "(lib_symbols (symbol \"Device:Test\" (property \"Reference\" \"U\" (at 0 0 0))"
        + " (symbol \"Test_1_1\" (pin passive line (at 0 3.81 270) (length 1.27) (name \"~\") (number \"1\")))))\n"
        + "(symbol (lib_id \"Device:Test\") (at 50.95 50.8 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")"
        + " (property \"Reference\" \"U1\" (at 50.95 45 0))) (embedded_fonts no))"));
}
