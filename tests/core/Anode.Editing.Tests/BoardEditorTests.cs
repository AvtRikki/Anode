using Anode.Geometry;
using Anode.Kicad;
using Anode.Render;

namespace Anode.Editing.Tests;

public class BoardEditorTests
{
    private const string Text = """
        (kicad_pcb
        	(version 20241229)
        	(generator "pcbnew")
        	(layers
        		(0 "F.Cu" signal)
        		(2 "B.Cu" signal)
        		(25 "Edge.Cuts" user)
        	)
        	(net 0 "")
        	(net 1 "VCC")
        	(footprint "Lib:A"
        		(layer "F.Cu")
        		(at 10 20)
        		(pad "1" thru_hole circle
        			(at 1 0)
        			(size 1 1)
        			(drill 0.5)
        			(layers "*.Cu")
        			(net 1 "VCC")
        		)
        	)
        	(footprint "Lib:B"
        		(layer "F.Cu")
        		(at 30 20)
        		(pad "1" smd rect
        			(at 0 0)
        			(size 1 2)
        			(layers "F.Cu")
        		)
        	)
        	(segment
        		(start 0 0)
        		(end 5 0)
        		(width 0.25)
        		(layer "F.Cu")
        		(net 1)
        	)
        	(gr_rect
        		(start -5 -5)
        		(end 50 40)
        		(stroke
        			(width 0.1)
        			(type default)
        		)
        		(fill no)
        		(layer "Edge.Cuts")
        	)
        )
        """;

    private static BoardEditor Open() => new(SceneBuilder.Build(Board.Parse(Text)));

    private static Vector2L Mm(double x, double y) => new(Units.MmToNm(x), Units.MmToNm(y));

    private static Vector2D Scene(BoardEditor editor, double xMm, double yMm) => editor.Scene.ToSceneMm(Mm(xMm, yMm).ToDouble());

    private static RectD Box(BoardEditor editor, double x0, double y0, double x1, double y1)
    {
        var a = Scene(editor, x0, y0);
        var b = Scene(editor, x1, y1);
        return new RectD(a.X, a.Y, b.X, b.Y);
    }

    private static int PadOwner(BoardEditor editor, Footprint footprint) =>
        editor.Scene.OwnersOf(footprint).First(id => editor.Scene.Owner(id) is Pad);

    [Fact]
    public void Clicking_a_pad_selects_its_footprint_and_focuses_the_net()
    {
        var editor = Open();
        var footprint = editor.Board.Footprints[0];

        editor.Click(PadOwner(editor, footprint), toggle: false);

        Assert.Same(footprint, Assert.Single(editor.Selection));
        Assert.Equal(editor.Scene.OwnersOf(footprint).ToHashSet(), editor.SelectedOwners.ToHashSet());
        Assert.Equal("VCC", editor.FocusNet?.Name);
    }

    [Fact]
    public void Toggle_click_adds_and_removes()
    {
        var editor = Open();
        var (a, b) = (editor.Board.Footprints[0], editor.Board.Footprints[1]);

        editor.Click(PadOwner(editor, a), toggle: false);
        editor.Click(PadOwner(editor, b), toggle: true);
        Assert.Equal(2, editor.Selection.Count);

        editor.Click(PadOwner(editor, a), toggle: true);
        Assert.Same(b, Assert.Single(editor.Selection));

        editor.Click(-1, toggle: false);
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void Box_selection_encloses_or_crosses()
    {
        var editor = Open();

        editor.SelectInBox(Box(editor, 9, 18, 13, 22), crossing: false, toggle: false);
        Assert.Same(editor.Board.Footprints[0], Assert.Single(editor.Selection));

        editor.SelectInBox(Box(editor, 2, -0.3, 3, 0.3), crossing: false, toggle: false);
        Assert.Empty(editor.Selection);

        editor.SelectInBox(Box(editor, 2, -0.3, 3, 0.3), crossing: true, toggle: false);
        Assert.Same(editor.Board.Segments[0], Assert.Single(editor.Selection));
    }

    [Fact]
    public void Move_snaps_the_anchor_to_the_grid_and_is_undoable()
    {
        var editor = Open();
        var footprint = editor.Board.Footprints[0];
        int primitives = editor.Scene.PrimitiveCount;
        editor.GridNm = 1_000_000;
        editor.SetSelection([footprint]);

        Assert.True(editor.BeginMove(footprint, Scene(editor, 11, 20)));
        Assert.Empty(editor.Scene.OwnersOf(footprint));
        Assert.NotEmpty(editor.Move!.Preview);

        editor.UpdateMove(Scene(editor, 13.3, 20.6));
        Assert.Equal(Mm(2, 1), editor.Move.Delta);
        var moved = editor.Move.PreviewTransform.Apply(Scene(editor, 10, 20));
        Assert.Equal(Scene(editor, 12, 21).X, moved.X, 6);

        editor.CommitMove();

        Assert.Null(editor.Move);
        Assert.Equal(Mm(12, 21), footprint.Position);
        Assert.Contains("(at 12 21)", editor.Board.Document.ToString());
        Assert.Equal(primitives, editor.Scene.PrimitiveCount);
        Assert.True(editor.History.IsDirty);
        Assert.Contains(editor.Scene.OwnersOf(footprint)[0], editor.SelectedOwners);

        editor.Undo();
        Assert.Equal(Text, editor.Board.Document.ToString());
        Assert.Equal(primitives, editor.Scene.PrimitiveCount);
        Assert.Equal(Mm(10, 20), footprint.Position);
    }

    [Fact]
    public void Cancelled_move_restores_the_scene_without_history()
    {
        var editor = Open();
        var footprint = editor.Board.Footprints[1];
        int primitives = editor.Scene.PrimitiveCount;
        editor.SetSelection([footprint]);

        editor.BeginMove(footprint, Scene(editor, 30, 20));
        editor.UpdateMove(Scene(editor, 35, 25));
        editor.CancelMove();

        Assert.Equal(primitives, editor.Scene.PrimitiveCount);
        Assert.False(editor.History.CanUndo);
        Assert.Equal(Text, editor.Board.Document.ToString());
    }

    [Fact]
    public void Rotation_during_a_move_is_applied_on_commit()
    {
        var editor = Open();
        var footprint = editor.Board.Footprints[0];
        editor.SetSelection([footprint]);

        editor.BeginMove(footprint, Scene(editor, 10, 20));
        editor.Rotate(90);
        editor.CommitMove();

        Assert.Equal(90, footprint.Orientation);
        Assert.Equal(Mm(10, 19), footprint.Pads[0].BoardPosition);
    }

    [Fact]
    public void Rotate_and_delete_selection_with_undo()
    {
        var editor = Open();
        var footprint = editor.Board.Footprints[0];
        editor.SetSelection([footprint, editor.Board.Segments[0]]);

        editor.Rotate(90);
        Assert.Equal(90, footprint.Orientation);

        editor.DeleteSelection();
        Assert.Empty(editor.Selection);
        Assert.Single(editor.Board.Footprints);
        Assert.Empty(editor.Board.Segments);
        Assert.Equal(SceneBuilder.Build(editor.Board).PrimitiveCount, editor.Scene.PrimitiveCount);

        editor.Undo();
        editor.Undo();
        Assert.Equal(Text, editor.Board.Document.ToString());
        Assert.Equal(SceneBuilder.Build(editor.Board).PrimitiveCount, editor.Scene.PrimitiveCount);

        editor.Redo();
        Assert.Equal(90, footprint.Orientation);
    }

    [Fact]
    public void Zone_fills_and_pads_cannot_move_on_their_own()
    {
        var editor = Open();
        editor.SetSelection([editor.Board.Footprints[0].Pads[0]]);

        Assert.False(editor.BeginMove(null, default));
    }
}
