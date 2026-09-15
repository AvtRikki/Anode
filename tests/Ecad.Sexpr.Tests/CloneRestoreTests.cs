namespace Ecad.Sexpr.Tests;

public class CloneRestoreTests
{
    private const string Text = "(kicad_pcb\n\t(footprint \"X\"\n\t\t(at 1 2)\n\t\t(pad \"1\" smd rect\n\t\t\t(at 0 0)\n\t\t)\n\t)\n)\n";

    [Fact]
    public void Clone_is_detached_and_identical()
    {
        var doc = SDocument.Parse(Text);
        var clone = doc.Root.CloneList();

        Assert.Null(clone.Parent);
        Assert.Equal(SWriter.WriteNode(doc.Root), SWriter.WriteNode(clone));
        Assert.NotSame(doc.Root.Find("footprint"), clone.Find("footprint"));
    }

    [Fact]
    public void Restore_after_value_edits_is_exact_and_keeps_child_nodes()
    {
        var doc = SDocument.Parse(Text);
        var footprint = doc.Root.Find("footprint")!;
        var pad = footprint.Find("pad")!;
        var snapshot = footprint.CloneList();

        footprint.Find("at")!.AtomAt(1)!.SetNumber(5);
        pad.Find("at")!.AtomAt(2)!.SetNumber(-3);
        Assert.NotEqual(Text, doc.ToString());

        footprint.RestoreFrom(snapshot);

        Assert.Equal(Text, doc.ToString());
        Assert.Same(pad, footprint.Find("pad"));
    }

    [Fact]
    public void Restore_after_structural_edits_is_exact()
    {
        var doc = SDocument.Parse(Text);
        var footprint = doc.Root.Find("footprint")!;
        var snapshot = footprint.CloneList();

        footprint.Find("at")!.Add(SAtom.Number(90));
        footprint.Add(new SList("locked"));
        Assert.Contains("(at 1 2 90)", doc.ToString());

        footprint.RestoreFrom(snapshot);

        Assert.Equal(Text, doc.ToString());
    }

    [Fact]
    public void Removed_node_can_be_reinserted_with_its_whitespace()
    {
        var doc = SDocument.Parse(Text);
        var footprint = doc.Root.Find("footprint")!;
        int index = footprint.IndexInParent;

        footprint.Remove();
        Assert.Equal("(kicad_pcb\n)\n", doc.ToString());

        doc.Root.Insert(index, footprint);
        Assert.Equal(Text, doc.ToString());
    }
}
