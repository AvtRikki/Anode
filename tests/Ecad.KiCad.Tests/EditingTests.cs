using Ecad.Geometry;
using Ecad.KiCad.Editing;
using Ecad.Sexpr;
using Ecad.Tests;

namespace Ecad.KiCad.Tests;

public class EditingTests
{
    private const string Legacy = """
        (kicad_pcb
        	(version 20241229)
        	(generator "pcbnew")
        	(layers
        		(0 "F.Cu" signal)
        		(2 "B.Cu" signal)
        	)
        	(net 0 "")
        	(net 1 "GND")
        	(footprint "Lib:Part"
        		(layer "F.Cu")
        		(at 10 20)
        		(property "Reference" "R1"
        			(at 0 -1.5 0)
        			(layer "F.SilkS")
        		)
        		(pad "1" smd rect
        			(at 1 0)
        			(size 0.8 0.9)
        			(layers "F.Cu")
        			(net 1 "GND")
        		)
        	)
        	(segment
        		(start 0 0)
        		(end 5 0)
        		(width 0.25)
        		(layer "F.Cu")
        		(net 1)
        	)
        	(arc
        		(start 0 2)
        		(mid 1 3)
        		(end 2 2)
        		(width 0.2)
        		(layer "F.Cu")
        		(net 1)
        	)
        	(via
        		(at 5 0)
        		(size 0.6)
        		(drill 0.3)
        		(layers "F.Cu" "B.Cu")
        		(net 1)
        	)
        	(gr_poly
        		(pts
        			(xy 0 0) (xy 4 0) (xy 4 3)
        		)
        		(stroke
        			(width 0.1)
        			(type solid)
        		)
        		(fill yes)
        		(layer "F.SilkS")
        	)
        	(gr_text "Hello"
        		(at 3 4 0)
        		(layer "F.SilkS")
        	)
        	(zone
        		(net 1)
        		(layer "F.Cu")
        		(polygon
        			(pts
        				(xy 0 0) (xy 10 0) (xy 10 10)
        			)
        		)
        		(filled_polygon
        			(layer "F.Cu")
        			(pts
        				(xy 1 1) (xy 9 1) (xy 9 9)
        			)
        		)
        	)
        )

        """;

    private const string Affine = """
        (kicad_pcb
        	(version 20260901)
        	(generator "pcbnew")
        	(footprint "Lib:Part"
        		(layer "F.Cu")
        		(transform
        			(translate 10 20)
        			(rotate 0)
        			(scale 1 1)
        		)
        		(pad "1" smd rect
        			(at 1 0)
        			(size 1 1)
        			(layers "F.Cu")
        		)
        	)
        )

        """;

    private static Vector2L Mm(double x, double y) => new(Units.MmToNm(x), Units.MmToNm(y));

    private static ModifyItemsCommand Transform(IReadOnlyList<BoardItem> items, Vector2L pivot, double degrees, Vector2L delta) =>
        new("Transform", items, () =>
        {
            foreach (var item in items)
            {
                BoardEdits.Transform(item, pivot, degrees, delta);
            }
        });

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1_000_000L, "1")]
    [InlineData(1_500_000L, "1.5")]
    [InlineData(-500L, "-0.0005")]
    [InlineData(123_456_789L, "123.456789")]
    [InlineData(-20_000_000L, "-20")]
    public void Millimetres_are_formatted_like_kicad(long nm, string expected)
    {
        Assert.Equal(expected, KiCadNumber.FormatMm(nm));
    }

    [Fact]
    public void Moving_a_footprint_changes_only_its_position_and_undo_restores_the_file()
    {
        var board = Board.Parse(Legacy);
        var footprint = board.Footprints[0];
        var history = new UndoStack();

        history.Execute(Transform([footprint], footprint.Position, 0, Mm(1.5, -2)));

        string moved = Legacy.Replace("(at 10 20)", "(at 11.5 18)");
        Assert.Equal(moved, board.Document.ToString());
        Assert.Equal(Mm(11.5, 18), footprint.Position);
        Assert.Equal(Mm(12.5, 18), footprint.Pads[0].BoardPosition);

        history.Undo();
        Assert.Equal(Legacy, board.Document.ToString());
        Assert.Equal(Mm(10, 20), footprint.Position);

        history.Redo();
        Assert.Equal(moved, board.Document.ToString());
    }

    [Fact]
    public void Rotating_a_footprint_turns_pad_and_text_angles_and_back_again()
    {
        var board = Board.Parse(Legacy);
        var footprint = board.Footprints[0];

        Transform([footprint], footprint.Position, 90, default).Apply();

        string text = board.Document.ToString();
        Assert.Contains("(at 10 20 90)", text);
        Assert.Contains("(at 1 0 90)", text);
        Assert.Contains("(at 0 -1.5 90)", text);
        Assert.Equal(Mm(10, 19), footprint.Pads[0].BoardPosition);

        Transform([footprint], footprint.Position, -90, default).Apply();

        // Zero angles are dropped again for the footprint and pad, kept for the text.
        Assert.Equal(Legacy, board.Document.ToString());
    }

    [Fact]
    public void Affine_footprints_update_translate_and_rotate()
    {
        var board = Board.Parse(Affine);
        var footprint = board.Footprints[0];

        Transform([footprint], footprint.Position, 90, Mm(1, 0)).Apply();

        string text = board.Document.ToString();
        Assert.Contains("(translate 11 20)", text);
        Assert.Contains("(rotate 90)", text);
        Assert.Contains("(at 1 0 90)", text);
        Assert.Equal(Mm(11, 19), footprint.Pads[0].BoardPosition);
    }

    [Fact]
    public void Board_items_move_by_delta()
    {
        var board = Board.Parse(Legacy);
        IReadOnlyList<BoardItem> items = [.. board.Items.Where(i => i is not Footprint)];
        var command = Transform(items, default, 0, Mm(1, 1));

        command.Apply();

        string text = board.Document.ToString();
        Assert.Equal(Mm(1, 1), board.Segments[0].Start);
        Assert.Equal(Mm(6, 1), board.Segments[0].End);
        Assert.Equal(Mm(2, 4), board.Arcs[0].Mid);
        Assert.Equal(Mm(6, 1), board.Vias[0].Position);
        Assert.Contains("(xy 1 1) (xy 5 1) (xy 5 4)", text);
        Assert.Contains("(at 4 5 0)", text);
        Assert.Contains("(xy 1 1) (xy 11 1) (xy 11 11)", text);
        Assert.Contains("(xy 2 2) (xy 10 2) (xy 10 10)", text);

        command.Revert();
        Assert.Equal(Legacy, board.Document.ToString());
    }

    [Fact]
    public void Board_items_rotate_counter_clockwise_about_the_pivot()
    {
        var board = Board.Parse(Legacy);
        Transform([board.Segments[0], board.Texts[0]], default, 90, default).Apply();

        Assert.Equal(Mm(0, -5), board.Segments[0].End);
        Assert.Contains("(at 4 -3 90)", board.Document.ToString());
    }

    [Fact]
    public void Delete_and_undo_restore_items_in_place()
    {
        var board = Board.Parse(Legacy);
        var history = new UndoStack();

        history.Execute(new DeleteItemsCommand(board, [board.Segments[0], board.Vias[0], board.Footprints[0]]));

        Assert.Empty(board.Segments);
        Assert.Empty(board.Vias);
        Assert.Empty(board.Footprints);
        Assert.DoesNotContain("(segment", board.Document.ToString());

        history.Undo();
        Assert.Equal(Legacy, board.Document.ToString());
        Assert.Single(board.Segments);
        Assert.Single(board.Vias);
        Assert.Single(board.Footprints);
    }

    [Fact]
    public void Footprint_children_report_the_footprint_as_top_level()
    {
        var board = Board.Parse(Legacy);
        var footprint = board.Footprints[0];

        Assert.Same(footprint, footprint.Pads[0].TopLevel);
        Assert.Same(footprint, footprint.Texts[0].TopLevel);
        Assert.Same(board.Segments[0], board.Segments[0].TopLevel);
        Assert.False(BoardEdits.CanTransform(footprint.Pads[0]));
    }

    [Fact]
    public void Undo_stack_tracks_the_saved_state()
    {
        var board = Board.Parse(Legacy);
        var history = new UndoStack();
        Assert.False(history.IsDirty);

        history.Execute(Transform([board.Vias[0]], default, 0, Mm(1, 0)));
        Assert.True(history.IsDirty);

        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);

        history.Undo();
        history.Execute(Transform([board.Vias[0]], default, 0, Mm(0, 1)));
        Assert.True(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void Edited_kicad_file_keeps_canonical_layout_and_round_trips()
    {
        string path = TestData.FullPath("demos/pic_programmer/pic_programmer.kicad_pcb");
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        byte[] original = File.ReadAllBytes(path);
        var board = Board.FromDocument(SDocument.FromBytes(original));
        var footprint = board.Footprints.First(f => f.Orientation == 0 && f.Pads.Count > 1);

        Transform([footprint], footprint.Position, 90, Mm(2.54, 0)).Apply();

        string edited = board.Document.ToString();
        Assert.NotEqual(System.Text.Encoding.UTF8.GetString(original), edited);
        Assert.Equal(SWriter.WritePrettified(board.Document), edited);

        Transform([footprint], footprint.Position, -90, Mm(-2.54, 0)).Apply();
        Assert.True(original.AsSpan().SequenceEqual(board.Document.ToBytes()));
    }
}
