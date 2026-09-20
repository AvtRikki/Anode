namespace Anode.Kicad.Tests;

/// <summary>
/// KiCad's escapes, put back: a label may not carry the characters its own syntax claims, so the file spells them
/// out in braces. The text's own markup — an overbar, a subscript, a variable — keeps its braces.
/// </summary>
public class KicadTextTests
{
    [Theory]
    [InlineData("VPP{slash}MCLR", "VPP/MCLR")]
    [InlineData("A{colon}B{space}C", "A:B C")]
    [InlineData("{brace}x{brace}", "{x{")]
    [InlineData("{dblquote}q{quote}", "\"q'")]
    [InlineData("{lt}{gt}{bar}{comma}{dollar}{backslash}", "<>|,$\\")]
    [InlineData("plain", "plain")]
    [InlineData("{", "{")]
    [InlineData("no{such}token", "no{such}token")]
    [InlineData("unterminated{slash", "unterminated{slash")]
    public void An_escape_is_the_character_it_stands_for(string written, string shown)
    {
        Assert.Equal(shown, KicadText.Unescape(written));
    }

    [Theory]
    [InlineData("~{RESET}")]
    [InlineData("V_{CC}")]
    [InlineData("3V3^{+5%}")]
    [InlineData("${VAR}")]
    public void The_texts_own_markup_keeps_its_braces(string markup)
    {
        Assert.Equal(markup, KicadText.Unescape(markup));
    }

    [Fact]
    public void An_escape_inside_markup_is_still_undone()
    {
        Assert.Equal("~{A/B}", KicadText.Unescape("~{A{slash}B}"));
    }
}
