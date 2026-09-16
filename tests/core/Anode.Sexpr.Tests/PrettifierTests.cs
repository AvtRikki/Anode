namespace Anode.Sexpr.Tests;

public class PrettifierTests
{
    [Fact]
    public void Nested_lists_are_indented_with_tabs()
    {
        const string input = "(first (second (third list) (another list)) (fifth) (sixth thing with lots of tokens (and a sub list)))";
        const string expected =
            "(first\n" +
            "\t(second\n" +
            "\t\t(third list)\n" +
            "\t\t(another list)\n" +
            "\t)\n" +
            "\t(fifth)\n" +
            "\t(sixth thing with lots of tokens\n" +
            "\t\t(and a sub list)\n" +
            "\t)\n" +
            ")\n";

        Assert.Equal(expected, KicadPrettifier.Prettify(input));
    }

    [Fact]
    public void Xy_points_stay_on_one_line_until_column_99()
    {
        var points = string.Join(' ', Enumerable.Range(0, 12).Select(i => $"(xy {i}.123 {i}.456)"));
        string result = KicadPrettifier.Prettify($"(gr_poly (pts {points}))");

        string[] lines = result.Split('\n');
        Assert.Equal("(gr_poly", lines[0]);
        Assert.Equal("\t(pts", lines[1]);
        Assert.StartsWith("\t\t(xy 0.123 0.456) (xy 1.123 1.456)", lines[2]);
        Assert.All(lines.Where(l => l.Contains("(xy")), l => Assert.True(l.Length <= 99 + 20));
        Assert.True(lines.Count(l => l.Contains("(xy")) > 1);
    }

    [Fact]
    public void Compact_text_mode_keeps_font_on_one_line()
    {
        const string input = "(property \"Reference\" \"R1\" (at 1 2 0) (effects (font (size 1.27 1.27))))";
        const string expected =
            "(property \"Reference\" \"R1\"\n" +
            "\t(at 1 2 0)\n" +
            "\t(effects\n" +
            "\t\t(font (size 1.27 1.27))\n" +
            "\t)\n" +
            ")\n";

        Assert.Equal(expected, KicadPrettifier.Prettify(input, KicadFormatMode.CompactTextProperties));
    }

    [Fact]
    public void Quoted_parens_are_not_structure()
    {
        Assert.Equal("(a \"(x) y\"\n\t(b)\n)\n", KicadPrettifier.Prettify("(a \"(x) y\" (b))"));
    }
}
