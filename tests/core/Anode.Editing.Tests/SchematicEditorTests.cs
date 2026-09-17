using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

namespace Anode.Editing.Tests;

/// <summary>
/// Editing a sheet through the editor. The rule that matters here is quiet but visible: drawing does not select what
/// it drew. A selection with no preview dims everything else, so a tool that selected each committed leg made the
/// whole sheet blink between the click and the next rubber band.
/// </summary>
public class SchematicEditorTests
{
    private const string Text = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(wire
        		(pts
        			(xy 50.8 50.8) (xy 63.5 50.8)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000001")
        	)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    private static readonly Vector2L A = new(76_200_000, 50_800_000);
    private static readonly Vector2L B = new(76_200_000, 38_100_000);

    private static SchematicEditor Editor(out Schematic sheet)
    {
        sheet = Schematic.Parse(Text);
        return new SchematicEditor(SchematicSceneBuilder.Build(sheet));
    }

    [Fact]
    public void Drawing_does_not_select_what_it_drew()
    {
        var editor = Editor(out var sheet);
        Assert.Empty(editor.Selection);

        editor.Add([SchNodes.Wire([A, B])]);

        Assert.Equal(2, sheet.Wires.Count);
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void Adding_can_select_when_that_is_what_is_wanted()
    {
        var editor = Editor(out _);

        var wire = SchNodes.Wire([A, B]);
        editor.Add([wire], select: true);

        Assert.Same(wire, Assert.Single(editor.Selection));
    }

    [Fact]
    public void What_was_drawn_is_on_the_sheet_and_undo_takes_it_off()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        editor.Add([SchNodes.Wire([A, B])]);
        Assert.True(editor.History.CanUndo);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        editor.Undo();

        Assert.Single(sheet.Wires);
        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void A_tap_dots_the_meeting_point_and_cuts_what_it_ran_into()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        // The sheet holds one wire from 50.8 to 63.5; the new one ends halfway along it.
        var tap = new Vector2L(57_150_000, 50_800_000);
        editor.DrawWire([(new Vector2L(57_150_000, 38_100_000), tap)]);

        // The wire drawn, and the one it ran into now in two pieces that meet at the tap.
        Assert.Equal(3, sheet.Wires.Count);
        Assert.Equal(tap, Assert.Single(sheet.Junctions).Position);
        Assert.Contains(sheet.Wires, w => w.Points is [var a, var b] && a == new Vector2L(50_800_000, 50_800_000) && b == tap);
        Assert.Contains(sheet.Wires, w => w.Points is [var a, var b] && a == tap && b == new Vector2L(63_500_000, 50_800_000));

        // One click was one step: a single undo takes the wire, the dot and the cut back together.
        editor.Undo();

        Assert.Single(sheet.Wires);
        Assert.Empty(sheet.Junctions);
        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void A_corner_gets_neither_a_dot_nor_a_cut()
    {
        var editor = Editor(out var sheet);

        // Starting where the existing wire ends: two ends meeting, which is a corner and nothing more.
        editor.DrawWire([(new Vector2L(63_500_000, 50_800_000), new Vector2L(63_500_000, 38_100_000))]);

        Assert.Equal(2, sheet.Wires.Count);
        Assert.Empty(sheet.Junctions);
    }

    [Fact]
    public void A_bus_is_drawn_without_dots_or_cuts()
    {
        var editor = Editor(out var sheet);

        editor.DrawWire([(new Vector2L(57_150_000, 38_100_000), new Vector2L(57_150_000, 50_800_000))], bus: true);

        // Wires and buses share one list in the model, told apart by IsBus: the bus arrived, the wire it crosses was
        // left whole, and a bus meets a wire through an entry rather than a dot.
        Assert.Single(sheet.Wires, w => w.IsBus);
        var wire = Assert.Single(sheet.Wires, w => !w.IsBus);
        Assert.Equal([new Vector2L(50_800_000, 50_800_000), new Vector2L(63_500_000, 50_800_000)], wire.Points);
        Assert.Empty(sheet.Junctions);
    }

    [Fact]
    public void A_placed_label_is_on_the_sheet_and_in_the_scene()
    {
        var editor = Editor(out var sheet);
        int before = editor.Scene.TopLevelItems.Count();

        var label = SchNodes.Label(SchLabelKind.Local, "VCC", A);
        editor.Apply("Place a label", [label], []);

        // On the sheet as an item of the file...
        Assert.Same(label, Assert.Single(sheet.Labels));

        // ...and drawn, which is the half that was in doubt when a placed label could not be seen.
        Assert.Contains(editor.Scene.TopLevelItems, item => ReferenceEquals(item, label));
        Assert.Equal(before + 1, editor.Scene.TopLevelItems.Count());

        editor.Undo();

        Assert.Empty(sheet.Labels);
        Assert.Equal(before, editor.Scene.TopLevelItems.Count());
    }

    [Fact]
    public void A_drawn_wire_is_in_the_scene_and_leaves_it_again()
    {
        var editor = Editor(out _);
        int before = editor.Scene.TopLevelItems.Count();

        editor.Add([SchNodes.Wire([A, B])]);
        Assert.Equal(before + 1, editor.Scene.TopLevelItems.Count());

        editor.Undo();
        Assert.Equal(before, editor.Scene.TopLevelItems.Count());
    }

    [Fact]
    public void Deleting_a_tap_takes_the_dot_it_needed_with_it()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        var tap = new Vector2L(57_150_000, 50_800_000);
        var start = new Vector2L(57_150_000, 38_100_000);
        editor.DrawWire([(start, tap)]);
        Assert.Single(sheet.Junctions);

        editor.SetSelection([sheet.Wires.Single(w => w.Points is [var a, _] && a == start)]);
        editor.DeleteSelection();

        // The branch is gone, and so is the dot: what is left of the wire it tapped meets end to end, which is a
        // corner and needs nothing.
        Assert.Empty(sheet.Junctions);
        Assert.Equal(2, sheet.Wires.Count);

        // The delete was one step, the drawing another; undoing both gives the file back byte for byte.
        editor.Undo();
        Assert.Single(sheet.Junctions);
        editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void A_duplicate_is_a_second_item_with_an_identity_of_its_own()
    {
        var editor = Editor(out var sheet);
        var original = sheet.Wires.Single();
        editor.SetSelection([original]);

        editor.Duplicate();

        Assert.Equal(2, sheet.Wires.Count);
        var copy = Assert.IsType<SchWire>(Assert.Single(editor.Selection));
        Assert.NotSame(original, copy);

        // A uuid names one item in one file. A copy that kept it would be a second item claiming to be the first.
        Assert.NotEqual(original.Uuid, copy.Uuid);

        // One grid square down and to the right, so the copy can be seen and taken hold of.
        Assert.Equal(original.Points[0] + new Vector2L(1_270_000, 1_270_000), copy.Points[0]);

        editor.Undo();
        Assert.Single(sheet.Wires);
    }

    [Fact]
    public void What_was_copied_can_be_pasted_where_it_is_asked_for_and_again_after_that()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();
        editor.SetSelection([sheet.Wires.Single()]);

        editor.Copy();
        Assert.True(editor.CanPaste);

        var at = new Vector2L(101_600_000, 76_200_000);
        editor.Paste(at);

        Assert.Equal(2, sheet.Wires.Count);
        var pasted = Assert.IsType<SchWire>(Assert.Single(editor.Selection));
        Assert.Equal(at, pasted.Points[0]);

        // The clipboard holds a copy of its own, so a second paste is a second item rather than the first moved again.
        editor.Paste(at);
        Assert.Equal(3, sheet.Wires.Count);
        Assert.NotEqual(pasted.Uuid, Assert.IsType<SchWire>(Assert.Single(editor.Selection)).Uuid);

        editor.Undo();
        editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void Cutting_takes_it_off_the_sheet_and_keeps_it()
    {
        var editor = Editor(out var sheet);
        editor.SetSelection([sheet.Wires.Single()]);

        editor.Cut();

        Assert.Empty(sheet.Wires);
        Assert.True(editor.CanPaste);

        // What was cut survives the item it came from being gone from the sheet.
        editor.Paste(new Vector2L(50_800_000, 50_800_000));
        Assert.Single(sheet.Wires);
    }
}
