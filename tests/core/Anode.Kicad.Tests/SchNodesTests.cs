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
}
