using Ecad.Geometry;
using Ecad.Rendering.Fonts;

namespace Ecad.Rendering.Tests;

public class StrokeTextTests
{
    private static List<(Vector2D A, Vector2D B)> Layout(string text, StrokeTextStyle style, Vector2D? anchor = null)
    {
        var segments = new List<(Vector2D, Vector2D)>();
        StrokeTextLayout.Layout(StrokeFont.Default, text, anchor ?? Vector2D.Zero, style, (a, b) => segments.Add((a, b)));
        return segments;
    }

    private static RectD Bounds(IEnumerable<(Vector2D A, Vector2D B)> segments)
    {
        var r = RectD.Empty;
        foreach (var (a, b) in segments)
        {
            r = r.Union(a.X, a.Y).Union(b.X, b.Y);
        }

        return r;
    }

    [Fact]
    public void Embedded_font_covers_latin_and_cyrillic()
    {
        var font = StrokeFont.Default;

        Assert.True(font.GlyphCount > 60_000);
        Assert.Equal(2, font.GetGlyph('A').Strokes.Length);
        Assert.NotEmpty(font.GetGlyph('Ж').Strokes);
        Assert.Empty(font.GetGlyph(' ').Strokes);
        Assert.Same(font.GetGlyph('?'), font.GetGlyph(0x10FFFF));
    }

    [Fact]
    public void Capital_letters_sit_on_the_baseline_one_height_tall()
    {
        var glyph = StrokeFont.Default.GetGlyph('H');
        var ys = glyph.Strokes.SelectMany(s => s).Select(p => p.Y).ToList();

        // Font units: caps span roughly from -1 (top) to 0 (baseline), Y down.
        Assert.InRange(ys.Min(), -1.0, -0.85);
        Assert.InRange(ys.Max(), -0.05, 0.1);
    }

    [Fact]
    public void Centered_text_is_centered_on_the_anchor()
    {
        var bounds = Bounds(Layout("HELLO", new StrokeTextStyle(1, 1, 0.15)));

        Assert.Equal(0, (bounds.MinX + bounds.MaxX) / 2, 0.15);
        Assert.Equal(0, (bounds.MinY + bounds.MaxY) / 2, 0.15);
        Assert.InRange(bounds.Height, 0.9, 1.1);
    }

    [Fact]
    public void Left_and_right_justification_put_the_text_on_either_side()
    {
        var left = Bounds(Layout("ABC", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left)));
        var right = Bounds(Layout("ABC", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Right)));

        Assert.True(left.MinX >= -0.01);
        Assert.True(right.MaxX <= 0.01);
    }

    [Fact]
    public void Rotation_by_90_degrees_turns_width_into_height()
    {
        var horizontal = Bounds(Layout("WIDE TEXT", new StrokeTextStyle(1, 1, 0.1)));
        var vertical = Bounds(Layout("WIDE TEXT", new StrokeTextStyle(1, 1, 0.1, AngleDegrees: 90)));

        Assert.Equal(horizontal.Width, vertical.Height, 1e-6);
        Assert.Equal(horizontal.Height, vertical.Width, 1e-6);
    }

    [Fact]
    public void Counter_clockwise_rotation_sends_left_justified_text_upwards()
    {
        // Y grows downwards, so text rotated +90° (counter-clockwise on screen) extends to negative Y.
        var bounds = Bounds(Layout("TEXT", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left, AngleDegrees: 90)));
        Assert.True(bounds.MaxY <= 0.5);
        Assert.True(bounds.MinY < -2);
    }

    [Fact]
    public void Mirroring_reflects_about_the_anchor()
    {
        var normal = Bounds(Layout("R", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left)));
        var mirrored = Bounds(Layout("R", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left, Mirrored: true)));

        Assert.Equal(-normal.MinX, mirrored.MaxX, 1e-9);
        Assert.Equal(-normal.MaxX, mirrored.MinX, 1e-9);
    }

    [Fact]
    public void Multiple_lines_stack_downwards()
    {
        var one = Bounds(Layout("A", new StrokeTextStyle(1, 1, 0.1, VAlign: TextVAlign.Top)));
        var two = Bounds(Layout("A\nA", new StrokeTextStyle(1, 1, 0.1, VAlign: TextVAlign.Top)));

        Assert.Equal(one.MinY, two.MinY, 1e-9);
        Assert.InRange(two.Height - one.Height, 1.5, 1.7);
    }

    [Fact]
    public void Overbar_markup_adds_a_bar_and_is_not_drawn_literally()
    {
        var plain = Layout("RESET", new StrokeTextStyle(1, 1, 0.1));
        var barred = Layout("~{RESET}", new StrokeTextStyle(1, 1, 0.1));

        Assert.Equal(plain.Count + 1, barred.Count);
        Assert.True(Bounds(barred).MinY < Bounds(plain).MinY);
    }

    [Fact]
    public void Italic_leans_to_the_right()
    {
        var upright = Bounds(Layout("I", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left, VAlign: TextVAlign.Bottom)));
        var italic = Bounds(Layout("I", new StrokeTextStyle(1, 1, 0.1, TextHAlign.Left, VAlign: TextVAlign.Bottom, Italic: true)));

        Assert.True(italic.MaxX > upright.MaxX);
    }
}
