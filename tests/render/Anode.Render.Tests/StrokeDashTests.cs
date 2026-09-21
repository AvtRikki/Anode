using System.Numerics;
using Anode.Render;

namespace Anode.Render.Tests;

/// <summary>
/// Lines that are not simply drawn: KiCad's dashes, dots and the mixtures of them. The lengths are ISO 128-2's, as
/// KiCad reads them — eleven widths of dash, a fifth of a width of dot, four widths of gap.
/// </summary>
public class StrokeDashTests
{
    [Theory]
    [InlineData("dash", 2)]
    [InlineData("dot", 2)]
    [InlineData("dash_dot", 4)]
    [InlineData("dash_dot_dot", 6)]
    public void Each_style_asks_for_its_own_pattern(string style, int lengths)
    {
        var pattern = StrokeDashes.Pattern(style, 1);

        Assert.NotNull(pattern);
        Assert.Equal(lengths, pattern.Length);

        // Drawn, skipped, drawn, skipped: the gap is every other length, and always the same one.
        for (int i = 1; i < pattern.Length; i += 2)
        {
            Assert.Equal(4, pattern[i], 3);
        }
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("default")]
    [InlineData(null)]
    public void A_line_that_is_simply_drawn_has_no_pattern(string? style) =>
        Assert.Null(StrokeDashes.Pattern(style, 1));

    [Fact]
    public void The_lengths_are_the_ones_KiCad_reads_from_the_standard()
    {
        var pattern = StrokeDashes.Pattern("dash_dot", 0.5f)!;

        Assert.Equal(11 * 0.5f, pattern[0], 4);
        Assert.Equal(4 * 0.5f, pattern[1], 4);
        Assert.Equal(0.2f * 0.5f, pattern[2], 4);
    }

    [Fact]
    public void A_line_of_no_width_is_not_cut_into_nothing() => Assert.Null(StrokeDashes.Pattern("dash", 0));

    [Fact]
    public void The_pieces_drawn_lie_along_the_line_and_leave_the_gaps_out()
    {
        var pattern = StrokeDashes.Pattern("dash", 1)!;
        var pieces = StrokeDashes.Cut(new Vector2(0, 0), new Vector2(100, 0), pattern).ToList();

        // Eleven drawn, four skipped, over a hundred: seven pieces, the last one cut short at the end.
        Assert.Equal(7, pieces.Count);
        Assert.All(pieces, piece => Assert.Equal(0, piece.From.Y, 4));
        Assert.Equal(0, pieces[0].From.X, 4);
        Assert.Equal(11, pieces[0].To.X, 4);
        Assert.Equal(15, pieces[1].From.X, 4);
        Assert.True(pieces[^1].To.X <= 100, "a piece ran past the end of the line");

        // What is drawn is shorter than the line, and by the gaps rather than by chance.
        float drawn = pieces.Sum(p => Vector2.Distance(p.From, p.To));
        Assert.InRange(drawn, 70, 80);
    }

    [Fact]
    public void A_line_too_short_to_show_a_dash_is_drawn_whole()
    {
        var pattern = StrokeDashes.Pattern("dash", 1)!;
        var piece = Assert.Single(StrokeDashes.Cut(new Vector2(0, 0), new Vector2(3, 0), pattern));

        Assert.Equal(new Vector2(0, 0), piece.From);
        Assert.Equal(new Vector2(3, 0), piece.To);
    }

    [Fact]
    public void The_pieces_of_a_slanted_line_stay_on_it()
    {
        var pattern = StrokeDashes.Pattern("dash", 1)!;
        var pieces = StrokeDashes.Cut(new Vector2(0, 0), new Vector2(60, 80), pattern).ToList();

        // The line runs three across for four down; every piece of it has to as well.
        Assert.All(pieces, piece =>
        {
            Assert.Equal(piece.From.X * 4 / 3, piece.From.Y, 3);
            Assert.Equal(piece.To.X * 4 / 3, piece.To.Y, 3);
        });
    }

    [Fact]
    public void A_step_that_does_not_move_along_the_line_ends_the_cutting()
    {
        // A pattern whose lengths are lost in the rounding would otherwise go round for ever, drawing pieces on top
        // of each other until the memory ran out. What is left of the line is drawn as it stands instead.
        var piece = Assert.Single(StrokeDashes.Cut(new Vector2(0, 0), new Vector2(10, 0), [0f, 0f]));

        Assert.Equal(new Vector2(0, 0), piece.From);
        Assert.Equal(new Vector2(10, 0), piece.To);
    }

    [Fact]
    public void The_pieces_of_any_pattern_are_few_enough_to_draw_and_stay_on_the_line()
    {
        // Widths from a hair to a hand, over a line long enough to be worth cutting.
        foreach (float width in new[] { 0.0001f, 0.01f, 0.1524f, 1f, 10f })
        {
            var pattern = StrokeDashes.Pattern("dash_dot_dot", width)!;
            var pieces = StrokeDashes.Cut(new Vector2(0, 0), new Vector2(500, 0), pattern).Take(20_000).ToList();

            // Cutting a hairline into a hundred thousand pieces would cost a great deal to draw something that
            // looks exactly like the line it started as, so there is an end to how finely a line is cut.
            Assert.InRange(pieces.Count, 1, 4_000);
            Assert.All(pieces, piece => Assert.InRange(piece.To.X, 0, 500));
        }
    }

    [Fact]
    public void A_line_whose_dashes_are_too_fine_to_tell_apart_is_drawn_whole()
    {
        var pattern = StrokeDashes.Pattern("dash", 0.0001f)!;
        var piece = Assert.Single(StrokeDashes.Cut(new Vector2(0, 0), new Vector2(500, 0), pattern));

        Assert.Equal(new Vector2(0, 0), piece.From);
        Assert.Equal(new Vector2(500, 0), piece.To);
    }
}
