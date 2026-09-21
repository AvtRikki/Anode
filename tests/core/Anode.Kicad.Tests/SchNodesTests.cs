using System.Text;
using Anode.Geometry;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// New items on a sheet: what a drawing tool writes. A wire that is added and undone must leave the file exactly as
/// it was, and one that stays must be laid out the way KiCad lays out the items around it.
/// </summary>
public class SchNodesTests
{
    private static readonly Vector2L A = new(50_800_000, 44_450_000);
    private static readonly Vector2L B = new(63_500_000, 44_450_000);

    [Fact]
    public void A_new_wire_reads_back_as_a_wire()
    {
        var wire = SchNodes.Wire([A, B]);

        Assert.Equal("wire", wire.Node.Head);
        Assert.False(wire.IsBus);
        Assert.Equal([A, B], wire.Points);
        Assert.False(string.IsNullOrEmpty(wire.Uuid));

        // A bus is the same item under another head, which is how KiCad tells them apart.
        Assert.True(SchNodes.Wire([A, B], bus: true).IsBus);
    }

    [Fact]
    public void A_junction_and_a_no_connect_carry_their_point()
    {
        Assert.Equal(A, SchNodes.Junction(A).Position);
        Assert.Equal(A, SchNodes.NoConnect(A).Position);
    }

    [Theory]
    [InlineData(SchLabelKind.Local, "label")]
    [InlineData(SchLabelKind.Global, "global_label")]
    [InlineData(SchLabelKind.Hierarchical, "hierarchical_label")]
    [InlineData(SchLabelKind.NetClassFlag, "netclass_flag")]
    public void A_label_is_written_under_the_head_its_kind_asks_for(SchLabelKind kind, string head)
    {
        var label = SchNodes.Label(kind, "VCC", A);

        Assert.Equal(head, label.Node.Head);
        Assert.Equal(kind, label.Kind);
        Assert.Equal("VCC", label.Text);
        Assert.Equal(A, label.Position);
        Assert.False(string.IsNullOrEmpty(label.Uuid));
    }

    [Fact]
    public void A_label_that_leaves_the_sheet_carries_a_direction()
    {
        Assert.Equal("output", SchNodes.Label(SchLabelKind.Global, "DONE", A, shape: "output").Shape);
        Assert.Equal("input", SchNodes.Label(SchLabelKind.Hierarchical, "CLK", A).Shape);
    }

    [Fact]
    public void A_name_with_quotes_in_it_survives_the_round_trip()
    {
        var label = SchNodes.Label(SchLabelKind.Local, "A\"B", A);

        Assert.Equal("A\"B", label.Text);
    }

    [Fact]
    public void A_bus_entry_carries_its_step()
    {
        var entry = SchNodes.BusEntry(A, new Vector2L(2_540_000, 2_540_000));

        Assert.Equal("bus_entry", entry.Node.Head);
        Assert.Equal(A, entry.Position);
        Assert.Equal(new Vector2L(A.X + 2_540_000, A.Y + 2_540_000), entry.EndPoint);
    }

    [Fact]
    public void Free_text_carries_what_was_written_and_where()
    {
        var text = SchNodes.Text("13V rail", A);

        Assert.Equal("text", text.Node.Head);
        Assert.Equal("13V rail", text.Text);
        Assert.Equal(A, text.Position);
    }

    [Fact]
    public void A_drawn_line_keeps_its_points()
    {
        var line = SchNodes.Polyline([A, B]);

        Assert.Equal("polyline", line.Node.Head);
        Assert.Equal(SchShapeKind.Polyline, line.Kind);
        Assert.Equal([A, B], line.Points);
        Assert.False(line.IsFilled);
    }

    [Fact]
    public void A_rectangle_and_a_circle_keep_their_geometry()
    {
        var rectangle = SchNodes.Rectangle(A, B);

        Assert.Equal(SchShapeKind.Rectangle, rectangle.Kind);
        Assert.Equal(A, rectangle.Start);
        Assert.Equal(B, rectangle.End);

        var circle = SchNodes.Circle(A, 2_540_000);

        Assert.Equal(SchShapeKind.Circle, circle.Kind);
        Assert.Equal(A, circle.Center);
        Assert.Equal(2_540_000, circle.Radius);
    }

    [Fact]
    public void A_line_of_one_point_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SchNodes.Polyline([A]));
    }

    [Fact]
    public void Adding_a_wire_and_undoing_it_gives_the_file_back()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        byte[] original = sheet.Document.ToBytes();
        int wires = sheet.Wires.Count;

        var history = new UndoStack();
        history.Execute(new AddNodesCommand(sheet, [SchNodes.Wire([A, B])]));

        Assert.Equal(wires + 1, sheet.Wires.Count);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        history.Undo();

        Assert.Equal(wires, sheet.Wires.Count);
        Assert.Equal(original, sheet.Document.ToBytes());

        // Redo puts it back, and the second undo is still exact.
        history.Redo();
        Assert.Equal(wires + 1, sheet.Wires.Count);
        history.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void A_written_wire_is_laid_out_like_the_items_around_it()
    {
        string? path = TestData.AnySchematic();
        Assert.SkipWhen(path is null, TestData.SkipReason);

        var sheet = Schematic.Load(path!);
        sheet.Attach(SchNodes.Wire([A, B]), int.MaxValue);

        string text = Encoding.UTF8.GetString(sheet.Document.ToBytes());

        // One tab in, its parts each on their own line: the whitespace a new node has none of is the writer's job.
        Assert.Contains("\n\t(wire\n\t\t(pts\n\t\t\t(xy ", text, StringComparison.Ordinal);

        // And what was written reads back as the same tree.
        Assert.Equal(text, Encoding.UTF8.GetString(Schematic.Parse(text).Document.ToBytes()));
    }

    /// <summary>
    /// An arc is kept by three points it passes through, as KiCad keeps one, so an arc read back is the arc that
    /// was drawn rather than one worked out from angles and rounded on the way.
    /// </summary>
    [Fact]
    public void An_arc_is_written_through_the_three_points_it_was_given()
    {
        var start = new Vector2L(10_000_000, 50_000_000);
        var mid = new Vector2L(30_000_000, 30_000_000);
        var end = new Vector2L(50_000_000, 50_000_000);

        var arc = SchNodes.Arc(start, mid, end);

        Assert.Equal(SchShapeKind.Arc, arc.Kind);
        Assert.Equal(start, arc.Start);
        Assert.Equal(mid, arc.Mid);
        Assert.Equal(end, arc.End);

        // And it reads as an arc: a circle through those three points, curving up over them.
        var geometry = Assert.NotNull(arc.ArcGeometry);
        Assert.Equal(30_000_000, geometry.Center.X, 0);
    }

    [Fact]
    public void A_curve_is_written_as_the_four_points_it_hangs_from()
    {
        Vector2L[] points =
        [
            new(0, 0), new(0, 10_000_000), new(10_000_000, 10_000_000), new(10_000_000, 0),
        ];

        var curve = SchNodes.Bezier(points);

        Assert.Equal(SchShapeKind.Bezier, curve.Kind);
        Assert.Equal(points, curve.Points);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void A_curve_is_four_points_and_not_another_number(int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new Vector2L(i * 1_000_000, 0)).ToArray();

        Assert.Throws<ArgumentException>(() => SchNodes.Bezier(points));
    }
}
