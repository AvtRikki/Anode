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

    [Fact]
    public void A_value_committed_in_the_inspector_is_one_step_of_the_history()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        var label = SchNodes.Label(SchLabelKind.Local, "VCC", A);
        editor.Apply("Place a label", [label], []);
        byte[] placed = sheet.Document.ToBytes();

        // What the inspector does when a field is written: one named step, through the same stack as everything else.
        editor.Modify("Rename", [label], () => SchWrites.SetText(label, "+3V3"));

        Assert.Equal("+3V3", label.Text);
        Assert.NotEqual(placed, sheet.Document.ToBytes());

        // One undo takes back the edit, not the placing; the second takes the file back to where it started.
        editor.Undo();
        Assert.Equal("VCC", label.Text);
        Assert.Equal(placed, sheet.Document.ToBytes());

        editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void Committing_nothing_writes_nothing()
    {
        var editor = Editor(out _);

        editor.Modify("Rename", [], () => throw new InvalidOperationException("must not run"));

        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void An_edited_item_is_drawn_again_as_it_now_is()
    {
        var editor = Editor(out var sheet);
        var label = SchNodes.Label(SchLabelKind.Local, "VCC", A);
        editor.Apply("Place a label", [label], []);

        editor.Modify("Move", [label], () => SchWrites.SetPosition(label, B));

        // The scene follows the file: the item is still drawn, and drawn where it now is.
        Assert.Contains(editor.Scene.TopLevelItems, item => ReferenceEquals(item, label));
        Assert.Equal(B, label.Position);
        Assert.Single(sheet.Labels);
    }

    [Fact]
    public void A_deleted_item_stops_being_drawn()
    {
        var editor = Editor(out var sheet);
        var wire = sheet.Wires.Single();
        editor.SetSelection([wire]);

        editor.DeleteSelection();

        // The file is only half the answer: an item still listed by the scene is an item still on the screen, which
        // is what "I deleted it and it did not go" looks like.
        Assert.Empty(sheet.Wires);
        Assert.DoesNotContain(editor.Scene.TopLevelItems, item => ReferenceEquals(item, wire));
        Assert.Empty(editor.Scene.OwnersOf(wire));
    }

    [Fact]
    public void A_rotated_item_is_drawn_once_not_twice()
    {
        var editor = Editor(out var sheet);
        var wire = sheet.Wires.Single();
        editor.SetSelection([wire]);

        int before = editor.Scene.OwnersOf(wire).Count;
        editor.Rotate(90);

        // Rotation redraws the item; leaving the old primitives behind would double it.
        Assert.Equal(before, editor.Scene.OwnersOf(wire).Count);
        Assert.Single(editor.Scene.TopLevelItems, item => ReferenceEquals(item, wire));
    }

    [Fact]
    public void An_edited_value_does_not_double_the_drawing()
    {
        var editor = Editor(out var sheet);
        var label = SchNodes.Label(SchLabelKind.Local, "VCC", A);
        editor.Apply("Place a label", [label], []);

        int before = editor.Scene.OwnersOf(label).Count;
        editor.Modify("Rename", [label], () => SchWrites.SetText(label, "+3V3"));

        Assert.Equal(before, editor.Scene.OwnersOf(label).Count);
        Assert.Single(editor.Scene.TopLevelItems, item => ReferenceEquals(item, label));
    }

    /// <summary>
    /// Bringing a selection into line. Items slide and nothing else: a drawing that is already wired must not be
    /// turned or resized by tidying it up.
    /// </summary>
    [Theory]
    [InlineData(SchematicEditor.AlignTo.Left)]
    [InlineData(SchematicEditor.AlignTo.Right)]
    [InlineData(SchematicEditor.AlignTo.Top)]
    [InlineData(SchematicEditor.AlignTo.Bottom)]
    public void Aligning_brings_every_item_to_the_same_edge(SchematicEditor.AlignTo edge)
    {
        var editor = Scattered(out var sheet);
        editor.SetSelection(sheet.Graphics);

        editor.Align(edge);

        var boxes = sheet.Graphics.Select(g => editor.Scene.BoundsOf(g)).ToList();
        double Edge(RectD box) => edge switch
        {
            SchematicEditor.AlignTo.Left => box.MinX,
            SchematicEditor.AlignTo.Right => box.MaxX,
            SchematicEditor.AlignTo.Top => box.MinY,
            _ => box.MaxY,
        };

        // Within a nanometre of each other: the move is whole nanometres on the sheet, not exact millimetres.
        Assert.All(boxes, box => Assert.Equal(Edge(boxes[0]), Edge(box), 3));
    }

    [Fact]
    public void Aligning_moves_things_without_turning_or_resizing_them()
    {
        var editor = Scattered(out var sheet);

        // Measured on the sheet rather than on the screen: the scene's own numbers wobble in their last bits when
        // anything moves, and a wobble is not what this is about.
        var sizes = sheet.Graphics.Select(g => g.End - g.Start).ToList();

        editor.SetSelection(sheet.Graphics);
        editor.Align(SchematicEditor.AlignTo.Left);

        Assert.Equal(sizes, sheet.Graphics.Select(g => g.End - g.Start));
    }

    [Fact]
    public void Aligning_is_one_step_to_undo()
    {
        var editor = Scattered(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        editor.SetSelection(sheet.Graphics);
        editor.Align(SchematicEditor.AlignTo.Right);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        editor.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void One_item_has_nothing_to_come_into_line_with()
    {
        var editor = Scattered(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        editor.SetSelection([sheet.Graphics[0]]);
        editor.Align(SchematicEditor.AlignTo.Left);

        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void Bringing_to_the_grid_needs_only_one_item()
    {
        var editor = Scattered(out var sheet);
        editor.GridNm = 2_540_000;

        editor.SetSelection([sheet.Graphics[0]]);
        editor.Align(SchematicEditor.AlignTo.Grid);

        // Its own point sits on the grid afterwards; it was written off it on purpose.
        var at = SchEdits.Anchor(sheet.Graphics[0]);
        Assert.Equal(0, at.X % 2_540_000);
        Assert.Equal(0, at.Y % 2_540_000);
    }

    /// <summary>
    /// A lock has to hold. Greying out a button would not be a lock: what is held must stay put through moving,
    /// turning, tidying and deleting, since each of those is a different road to the same item.
    /// </summary>
    [Fact]
    public void What_is_held_in_place_does_not_move_or_turn()
    {
        var editor = Scattered(out var sheet);
        var held = sheet.Graphics[0];
        SchWrites.SetFlag(held, "locked", true);

        var was = held.Start;
        editor.SetSelection(sheet.Graphics);

        editor.Align(SchematicEditor.AlignTo.Right);
        Assert.Equal(was, held.Start);

        editor.Rotate(90);
        Assert.Equal(was, held.Start);

        editor.Mirror(horizontal: true);
        Assert.Equal(was, held.Start);

        // The others were free to move, so the tidying did happen — the lock held one thing, not everything.
        Assert.NotEqual(was, sheet.Graphics[1].Start);
    }

    [Fact]
    public void What_is_held_in_place_is_not_deleted_with_the_rest()
    {
        var editor = Scattered(out var sheet);
        var held = sheet.Graphics[0];
        SchWrites.SetFlag(held, "locked", true);

        editor.SetSelection(sheet.Graphics);
        editor.DeleteSelection();

        // It is still there, and still selected, so it is plain which one stayed and why.
        Assert.Single(sheet.Graphics);
        Assert.Same(held, sheet.Graphics[0]);
        Assert.Same(held, Assert.Single(editor.Selection));
    }

    [Fact]
    public void A_selection_of_nothing_but_held_items_deletes_nothing()
    {
        var editor = Scattered(out var sheet);
        foreach (var graphic in sheet.Graphics)
        {
            SchWrites.SetFlag(graphic, "locked", true);
        }

        byte[] original = sheet.Document.ToBytes();
        editor.SetSelection(sheet.Graphics);
        editor.DeleteSelection();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Equal(3, editor.Selection.Count);
    }

    [Fact]
    public void Letting_go_gives_the_item_back_its_freedom()
    {
        var editor = Scattered(out var sheet);
        var held = sheet.Graphics[0];
        SchWrites.SetFlag(held, "locked", true);
        Assert.False(SchEdits.CanTransform(held));

        SchWrites.SetFlag(held, "locked", false);

        Assert.True(SchEdits.CanTransform(held));
        Assert.False(held.IsLocked);
    }

    /// <summary>
    /// Dragging keeps the wiring. Moving a part takes it away from its wires, which is right when it is being put
    /// somewhere else and wrong when it is being nudged: the drawing would quietly lose its connections.
    /// </summary>
    [Fact]
    public void Dragging_a_part_takes_the_wires_that_meet_it_along()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();

        var was = wire.Points;
        editor.SetSelection([part]);
        Assert.True(editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true));

        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(0, 2_540_000)).ToDouble()));
        editor.CommitMove();

        // The end that met the pin came along; the far end stayed where the drawing put it.
        Assert.Equal(was[0], wire.Points[0]);
        Assert.Equal(was[1] + new Vector2L(0, 2_540_000), wire.Points[1]);
    }

    [Fact]
    public void Moving_a_part_leaves_its_wires_where_they_are()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();
        var was = wire.Points;

        editor.SetSelection([part]);
        Assert.True(editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble())));
        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(0, 2_540_000)).ToDouble()));
        editor.CommitMove();

        Assert.Equal(was, wire.Points);
    }

    [Fact]
    public void A_drag_is_one_step_to_undo_wires_and_all()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        byte[] original = sheet.Document.ToBytes();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);
        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(2_540_000, 0)).ToDouble()));
        editor.CommitMove();

        Assert.NotEqual(original, sheet.Document.ToBytes());

        editor.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void A_wire_that_is_itself_being_dragged_is_not_stretched_as_well()
    {
        var editor = Wired(out var sheet);
        var wire = sheet.Wires.Single();
        var was = wire.Points;

        editor.SetSelection([wire]);
        editor.BeginMove(wire, editor.Scene.ToSceneMm(was[0].ToDouble()), stretching: true);
        editor.UpdateMove(editor.Scene.ToSceneMm((was[0] + new Vector2L(0, 2_540_000)).ToDouble()));
        editor.CommitMove();

        // It travels whole: both ends moved by the same amount, rather than one end being pulled away.
        Assert.Equal(was[0] + new Vector2L(0, 2_540_000), wire.Points[0]);
        Assert.Equal(was[1] + new Vector2L(0, 2_540_000), wire.Points[1]);
    }

    /// <summary>
    /// While the pointer is moving, the stretched wire is drawn where it stands: the end in hand follows, the far
    /// end does not. It cannot be drawn by carrying the wire along, which is what the rest of the preview does.
    /// </summary>
    [Fact]
    public void The_stretched_wire_is_drawn_as_it_is_being_stretched()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();
        var far = editor.Scene.ToSceneMm(wire.Points[0].ToDouble());

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);

        var rubber = editor.Move!.Rubber;
        Assert.NotNull(rubber);
        Assert.Single(rubber.Layer.Lines);

        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(0, 5_080_000)).ToDouble()));

        var line = Assert.Single(rubber.Layer.Lines);
        var ends = new[] { line.A, line.B };

        // The far end is where it always was; the other end has moved five millimetres down the sheet.
        Assert.Contains(ends, p => Math.Abs(p.X - far.X) < 0.001 && Math.Abs(p.Y - far.Y) < 0.001);
        Assert.Contains(ends, p => Math.Abs(p.Y - (far.Y - 8.89 + 5.08)) < 0.01);
    }

    [Fact]
    public void The_wire_being_stretched_is_not_left_on_the_sheet_underneath()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);

        // Drawn twice — once where it was and once where it is going — would look like two wires.
        Assert.True(editor.Scene.BoundsOf(wire).IsEmpty, "the wire is still drawn where it was");
    }

    [Fact]
    public void A_drag_that_is_called_off_changes_nothing_and_puts_the_wire_back()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();
        byte[] original = sheet.Document.ToBytes();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);
        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(0, 5_080_000)).ToDouble()));
        editor.CancelMove();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.False(editor.Scene.BoundsOf(wire).IsEmpty, "the wire was not put back on the sheet");
    }

    [Fact]
    public void A_drag_committed_without_moving_puts_the_wire_back()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();
        var wire = sheet.Wires.Single();
        byte[] original = sheet.Document.ToBytes();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);
        editor.CommitMove();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.False(editor.Scene.BoundsOf(wire).IsEmpty);
    }

    [Theory]
    [InlineData(false, 0.4)]
    [InlineData(true, 0)]
    public void Stretched_lines_keep_their_layer_and_width(bool bus, double strokeWidthMm)
    {
        var editor = Wired(out var sheet, bus, strokeWidthMm);
        var part = sheet.Symbols.Single();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);

        var rubber = Assert.IsType<RubberBand>(editor.Move!.Rubber);
        var layer = bus ? rubber.BusLayer : rubber.Layer;
        var line = Assert.Single(layer.Lines);
        Assert.Equal(bus ? LayerStyle.Sch.Bus : LayerStyle.Sch.Wire, layer.Name);
        Assert.Equal(bus ? 0.3048f : 0.4f, line.Width, 4);
        Assert.Empty((bus ? rubber.Layer : rubber.BusLayer).Lines);
    }

    [Fact]
    public void An_ordinary_move_has_nothing_to_stretch()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()));

        Assert.Null(editor.Move!.Rubber);
        Assert.Empty(editor.Move.Stretching);
    }

    /// <summary>
    /// A drag keeps the wiring square. The wire from the pin runs down to a corner and the wire beyond it runs
    /// across; pulling the part sideways slides the corner along the second wire, as KiCad does, rather than
    /// leaving the first one slanted.
    /// </summary>
    [Fact]
    public void Dragging_across_a_wire_slides_its_corner_along_the_next_one()
    {
        var editor = Wired(out var sheet, beyond: Wire(2, 50.8, 63.5, 76.2, 63.5));
        var part = sheet.Symbols.Single();

        Drag(editor, part, new Vector2L(2_540_000, 0));

        Assert.Equal(2, sheet.Wires.Count);
        Assert.Equal([Mm(53.34, 63.5), Mm(53.34, 54.61)], sheet.Wires[0].Points);
        Assert.Equal([Mm(53.34, 63.5), Mm(76.2, 63.5)], sheet.Wires[1].Points);
    }

    /// <summary>
    /// Where the far end cannot give — here it is a branch, two other wires meeting it — the wire stays put and a
    /// step is put in at the pin to reach where the part has gone. Nothing is left slanted, nothing torn off.
    /// </summary>
    [Fact]
    public void Where_the_far_end_is_held_a_step_is_put_in_at_the_pin()
    {
        var editor = Wired(out var sheet, beyond: Wire(2, 25.4, 63.5, 50.8, 63.5) + Wire(3, 50.8, 63.5, 76.2, 63.5));
        var part = sheet.Symbols.Single();

        Drag(editor, part, new Vector2L(2_540_000, 0));

        Assert.Equal(4, sheet.Wires.Count);
        Assert.Equal([Mm(50.8, 63.5), Mm(50.8, 54.61)], sheet.Wires[0].Points);
        var step = sheet.Wires[^1].Points;
        Assert.Equal([Mm(50.8, 54.61), Mm(53.34, 54.61)], step);
        Assert.All(sheet.Wires, wire => Assert.True(
            wire.Points[0].X == wire.Points[1].X || wire.Points[0].Y == wire.Points[1].Y, "a wire came out slanted"));
    }

    /// <summary>
    /// A wire whose far end meets nothing goes sideways whole; along its own length it just stretches, the far end
    /// staying where the drawing put it.
    /// </summary>
    [Fact]
    public void A_loose_wire_goes_sideways_whole_and_stretches_along()
    {
        var editor = Wired(out var sheet);
        var part = sheet.Symbols.Single();

        Drag(editor, part, new Vector2L(2_540_000, 2_540_000));

        Assert.Equal([Mm(53.34, 63.5), Mm(53.34, 57.15)], Assert.Single(sheet.Wires).Points);
    }

    [Fact]
    public void A_drag_that_put_a_step_in_is_one_step_to_undo()
    {
        var editor = Wired(out var sheet, beyond: Wire(2, 25.4, 63.5, 50.8, 63.5) + Wire(3, 50.8, 63.5, 76.2, 63.5));
        var part = sheet.Symbols.Single();
        byte[] original = sheet.Document.ToBytes();

        Drag(editor, part, new Vector2L(2_540_000, 0));
        Assert.Equal(4, sheet.Wires.Count);

        editor.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Equal(3, sheet.Wires.Count);
    }

    /// <summary>The preview shows the square shape too, step included, not just the finished drawing.</summary>
    [Fact]
    public void The_step_is_drawn_while_dragging()
    {
        var editor = Wired(out var sheet, beyond: Wire(2, 25.4, 63.5, 50.8, 63.5) + Wire(3, 50.8, 63.5, 76.2, 63.5));
        var part = sheet.Symbols.Single();

        editor.SetSelection([part]);
        editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true);
        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + new Vector2L(2_540_000, 0)).ToDouble()));

        // The wire to the pin and the step from it; the branch at the far end is not part of the drag.
        Assert.Equal(2, editor.Move!.Rubber!.Layer.Lines.Count);
        editor.CancelMove();
    }

    private static void Drag(SchematicEditor editor, SchItem part, Vector2L by)
    {
        editor.SetSelection([part]);
        Assert.True(editor.BeginMove(part, editor.Scene.ToSceneMm(part.Position.ToDouble()), stretching: true));
        editor.UpdateMove(editor.Scene.ToSceneMm((part.Position + by).ToDouble()));
        editor.CommitMove();
    }

    private static Vector2L Mm(double x, double y) => new((long)Math.Round(x * 1_000_000), (long)Math.Round(y * 1_000_000));

    private static string Wire(int n, double x1, double y1, double x2, double y2) =>
        FormattableString.Invariant($"\t(wire (pts (xy {x1} {y1}) (xy {x2} {y2})) (stroke (width 0) (type default))")
        + $" (uuid \"1a1b2c3d-0000-4000-8000-00000000000{n}\"))\n";

    /// <summary>A resistor with a wire running from its lower pin.</summary>
    private static SchematicEditor Wired(out Schematic sheet, bool bus = false, double strokeWidthMm = 0, string beyond = "")
    {
        sheet = Schematic.Parse(
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + "\t(lib_symbols\n"
            + "\t\t(symbol \"Device:R\" (property \"Reference\" \"R\" (at 0 0 0))\n"
            + "\t\t\t(symbol \"R_1_1\"\n"
            + "\t\t\t\t(pin passive line (at 0 3.81 270) (length 1.27) (name \"~\") (number \"1\"))\n"
            + "\t\t\t\t(pin passive line (at 0 -3.81 90) (length 1.27) (name \"~\") (number \"2\")))))\n"
            + "\t(symbol (lib_id \"Device:R\") (at 50.8 50.8 0) (unit 1) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\")\n"
            + "\t\t(property \"Reference\" \"R1\" (at 50.8 45 0)))\n"

            // From the lower pin, straight down: its end sits exactly on the pin, which is what joins them.
            + $"\t({(bus ? "bus" : "wire")} (pts (xy 50.8 63.5) (xy 50.8 54.61)) (stroke (width {strokeWidthMm.ToString(System.Globalization.CultureInfo.InvariantCulture)}) (type default))\n"
            + "\t\t(uuid \"1a1b2c3d-0000-4000-8000-000000000001\"))\n"
            + beyond
            + "\t(embedded_fonts no))\n");

        return new SchematicEditor(SchematicSceneBuilder.Build(sheet));
    }

    /// <summary>Three rectangles of different sizes, none of them lined up with another.</summary>
    private static SchematicEditor Scattered(out Schematic sheet)
    {
        sheet = Schematic.Parse(
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + Box(1, 20.1, 30.3, 40, 45)
            + Box(2, 55.7, 60.9, 70, 80)
            + Box(3, 90.2, 25.4, 120, 35)
            + "\t(embedded_fonts no))\n");

        return new SchematicEditor(SchematicSceneBuilder.Build(sheet));

        static string Box(int n, double x1, double y1, double x2, double y2) =>
            $"\t(rectangle (start {x1} {y1}) (end {x2} {y2}) (stroke (width 0.1524) (type solid)) (fill (type none))"
            + $" (uuid \"0a1b2c3d-0000-4000-8000-00000000000{n}\"))\n";
    }
}
