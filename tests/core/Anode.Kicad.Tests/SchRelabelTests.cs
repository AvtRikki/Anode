using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Writing the same words as another kind of thing: a local name made global, a hierarchical one made into a note.
/// Only what the words mean changes — where they are, which way they face and how they look all travel with them.
/// </summary>
public class SchRelabelTests
{
    [Theory]
    [InlineData(SchLabelKind.Local, "label")]
    [InlineData(SchLabelKind.Global, "global_label")]
    [InlineData(SchLabelKind.Hierarchical, "hierarchical_label")]
    public void A_label_can_be_written_as_any_other_kind(SchLabelKind to, string head)
    {
        var fresh = SchNodes.Relabel(Label("label", "SDA"), to);

        Assert.Equal(head, fresh.Node.Head);
        Assert.Equal("SDA", Assert.IsType<SchLabel>(fresh).Shown);
        Assert.Equal(to, ((SchLabel)fresh).Kind);
    }

    [Fact]
    public void A_label_can_be_written_as_a_note_and_a_note_as_a_label()
    {
        var note = SchNodes.Relabel(Label("global_label", "RESET"), null);
        Assert.Equal("text", note.Node.Head);
        Assert.Equal("RESET", Assert.IsType<SchText>(note).Shown);

        var back = SchNodes.Relabel(note, SchLabelKind.Local);
        Assert.Equal("label", back.Node.Head);
        Assert.Equal("RESET", ((SchLabel)back).Shown);
    }

    [Fact]
    public void Where_it_is_and_which_way_it_faces_travel_with_it()
    {
        var label = Label("label", "CLK", at: new Vector2L(101_600_000, 50_800_000), angle: 90);

        var fresh = SchNodes.Relabel(label, SchLabelKind.Global);

        Assert.Equal(label.Position, fresh.Position);
        Assert.Equal(90, fresh.Angle);
    }

    [Fact]
    public void How_it_looks_travels_with_it()
    {
        // A label written large, italic and justified right keeps all of that when its kind changes.
        var label = new SchLabel(Anode.Sexpr.SDocument.Parse(
            "(label \"BUS\" (at 10 20 0) (effects (font (size 2.54 2.54) (italic yes)) (justify right))"
            + " (uuid \"0a1b2c3d-0000-4000-8000-000000000001\"))").Root);

        var fresh = SchNodes.Relabel(label, SchLabelKind.Hierarchical);

        Assert.Equal(label.TextHeight, ((SchLabel)fresh).TextHeight);
        Assert.Equal(label.Font.Italic, ((SchLabel)fresh).Font.Italic);
        Assert.Equal(label.Alignment, ((SchLabel)fresh).Alignment);
    }

    [Fact]
    public void A_label_that_leaves_the_sheet_keeps_the_direction_it_had()
    {
        var output = new SchLabel(Anode.Sexpr.SDocument.Parse(
            "(hierarchical_label \"DONE\" (shape output) (at 10 20 0) (effects (font (size 1.27 1.27)))"
            + " (uuid \"0a1b2c3d-0000-4000-8000-000000000002\"))").Root);

        var fresh = SchNodes.Relabel(output, SchLabelKind.Global);

        Assert.Equal("output", ((SchLabel)fresh).Shape);
    }

    [Fact]
    public void A_local_name_becoming_one_that_leaves_the_sheet_is_given_the_plainest_direction()
    {
        var fresh = SchNodes.Relabel(Label("label", "SPARE"), SchLabelKind.Hierarchical);

        Assert.Equal("input", ((SchLabel)fresh).Shape);
    }

    [Fact]
    public void There_is_nothing_to_carry_over_from_something_with_no_words()
    {
        var empty = new SchLabel(Anode.Sexpr.SDocument.Parse(
            "(label \"\" (at 10 20 0) (uuid \"0a1b2c3d-0000-4000-8000-000000000003\"))").Root);

        Assert.Throws<InvalidOperationException>(() => SchNodes.Relabel(empty, SchLabelKind.Global));
    }

    [Fact]
    public void Something_that_is_not_words_at_all_cannot_be_rewritten_as_words()
    {
        var wire = SchNodes.Wire([new Vector2L(0, 0), new Vector2L(10, 0)]);

        Assert.Throws<NotSupportedException>(() => SchNodes.Relabel(wire, SchLabelKind.Local));
    }

    private static SchLabel Label(string head, string text, Vector2L at = default, double angle = 0) =>
        new(Anode.Sexpr.SDocument.Parse(
            $"({head} \"{text}\" (at {at.X / 1_000_000.0} {at.Y / 1_000_000.0} {angle})"
            + " (effects (font (size 1.27 1.27))) (uuid \"0a1b2c3d-0000-4000-8000-000000000004\"))").Root);
}
