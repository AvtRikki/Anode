namespace Ecad.Sexpr.Tests;

public class ParserWriterTests
{
    [Theory]
    [InlineData("(a)")]
    [InlineData("(kicad_pcb\n\t(version 20241229)\n\t(generator \"pcbnew\")\n)\n")]
    [InlineData("  ( a   b\t\"c d\"  ( e ) )  \r\n")]
    [InlineData("(a \"esc \\\" \\\\ \\n\")")]
    [InlineData("(a)(b)\n(c)")]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("(xy 1.5 -2)(xy 3 4)")]
    public void Unmodified_document_round_trips_exactly(string text)
    {
        Assert.Equal(text, SDocument.Parse(text).ToString());
    }

    [Fact]
    public void Utf8_bom_is_preserved()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "(a \"µ\")\n"u8];
        var doc = SDocument.FromBytes(bytes);
        Assert.True(doc.HasUtf8Bom);
        Assert.Equal(bytes, doc.ToBytes());
    }

    [Fact]
    public void Atoms_expose_kind_and_value()
    {
        var root = SDocument.Parse("(footprint \"R_0603 \\\"x\\\"\" locked (at 1.5 -2 90))").Root;

        Assert.Equal("footprint", root.Head);
        var name = root.AtomAt(1)!;
        Assert.Equal(SAtomKind.String, name.Kind);
        Assert.Equal("R_0603 \"x\"", name.Value);
        Assert.True(root.HasSymbol("locked"));

        var at = root.Find("at")!;
        Assert.Equal(1.5, at.AtomAt(1)!.AsDouble());
        Assert.Equal(-2, at.AtomAt(2)!.AsDouble());
        Assert.Equal(root, at.Parent);
    }

    [Fact]
    public void Editing_an_atom_keeps_surrounding_whitespace()
    {
        var doc = SDocument.Parse("(a\n\t(at  1   2)\n)");
        doc.Root.Find("at")!.AtomAt(2)!.SetNumber(-0.25);
        Assert.Equal("(a\n\t(at  1   -0.25)\n)", doc.ToString());
    }

    [Fact]
    public void Inserted_list_gets_kicad_layout()
    {
        var doc = SDocument.Parse("(kicad_pcb\n\t(version 1)\n)\n");
        doc.Root.Add(new SList("net", SAtom.Number(1), SAtom.String("GND")));
        Assert.Equal("(kicad_pcb\n\t(version 1)\n\t(net 1 \"GND\")\n)\n", doc.ToString());
    }

    [Fact]
    public void Inline_list_becomes_multiline_when_it_gains_a_sublist()
    {
        var doc = SDocument.Parse("(root\n\t(stroke\n\t\t(width 0.1)\n\t)\n\t(font)\n)");
        doc.Root.Find("font")!.Add(new SList("size", SAtom.Number(1), SAtom.Number(1)));
        Assert.Equal("(root\n\t(stroke\n\t\t(width 0.1)\n\t)\n\t(font\n\t\t(size 1 1)\n\t)\n)", doc.ToString());
    }

    [Fact]
    public void Removing_a_node_detaches_it()
    {
        var doc = SDocument.Parse("(a (b) (c))");
        var b = doc.Root.Find("b")!;
        b.Remove();
        Assert.Null(b.Parent);
        Assert.Equal("(a (c))", doc.ToString());
    }

    [Theory]
    [InlineData("(a", 1, 1, "Unclosed")]
    [InlineData("(a\n  \"oops)", 2, 3, "Unterminated")]
    [InlineData("(a))", 1, 4, "Unexpected")]
    public void Errors_report_position(string text, int line, int column, string message)
    {
        var ex = Assert.Throws<SexprParseException>(() => SDocument.Parse(text));
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
        Assert.Contains(message, ex.Message);
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(-0.0, "0")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(-12.345678, "-12.345678")]
    [InlineData(1e-7, "0.0000001")]
    public void Numbers_format_like_kicad(double value, string expected)
    {
        Assert.Equal(expected, SNumber.Format(value));
    }

    [Fact]
    public void Escapes_round_trip()
    {
        const string value = "line1\nq\"b\\s";
        var atom = SAtom.String(value);
        Assert.Equal("\"line1\\nq\\\"b\\\\s\"", atom.Raw);
        Assert.Equal(value, atom.Value);
    }
}
