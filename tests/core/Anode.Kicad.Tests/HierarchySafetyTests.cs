using System.Text;
using Anode.Sexpr;

namespace Anode.Kicad.Tests;

public sealed class HierarchySafetyTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("anode-hierarchy-").FullName;

    private string Write(string name, params string[] children)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "(kicad_sch (uuid root) " + string.Join(" ", children.Select((file, i) =>
            $"(sheet (uuid s{i}) (property \"Sheetfile\" {SEscape.Quote(file)}))")) + ")");
        return path;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Self_references_are_reported_without_expanding(int references)
    {
        string root = Write("root.kicad_sch", Enumerable.Repeat("./root.kicad_sch", references).ToArray());
        var issues = new List<HierarchyDiagnostic>();
        Assert.Single(SchHierarchy.Walk(root, report: issues.Add, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(references, issues.Count);
        Assert.All(issues, i => Assert.Equal(HierarchyProblem.Cycle, i.Problem));
    }

    [Fact]
    public void An_indirect_cycle_stops_at_the_ancestor()
    {
        string root = Write("a.kicad_sch", "b.kicad_sch");
        Write("b.kicad_sch", "a.kicad_sch");
        var issues = new List<HierarchyDiagnostic>();
        Assert.Equal(2, SchHierarchy.Walk(root, report: issues.Add, cancellationToken: TestContext.Current.CancellationToken).Count);
        Assert.Equal(HierarchyProblem.Cycle, Assert.Single(issues).Problem);
    }

    [Fact]
    public void Reusing_a_sheet_in_sibling_branches_is_not_a_cycle()
    {
        string root = Write("root.kicad_sch", "child.kicad_sch", "child.kicad_sch");
        Write("child.kicad_sch");
        var issues = new List<HierarchyDiagnostic>();
        var places = SchHierarchy.Walk(root, report: issues.Add, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, places.Count);
        Assert.NotEqual(places[1].Path, places[2].Path);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unreadable_child_keeps_the_root_and_healthy_sibling(bool invalidUtf8)
    {
        string root = Write("root.kicad_sch", "bad.kicad_sch", "good.kicad_sch");
        string bad = Path.Combine(_directory, "bad.kicad_sch");
        File.WriteAllBytes(bad, invalidUtf8 ? [0xff] : Encoding.UTF8.GetBytes("(kicad_sch"));
        string good = Write("good.kicad_sch");
        var issues = new List<HierarchyDiagnostic>();
        Assert.Equal(new[] { root, good }, SchHierarchy.Walk(root, report: issues.Add, cancellationToken: TestContext.Current.CancellationToken).Select(p => p.File));
        var issue = Assert.Single(issues);
        Assert.Equal(bad, issue.File);
        Assert.Equal(HierarchyProblem.Unreadable, issue.Problem);
        Assert.NotEmpty(issue.Detail!);
        Assert.ThrowsAny<Exception>(() => Schematic.Load(bad));
    }

    [Fact]
    public void A_wide_acyclic_design_has_a_total_instance_budget()
    {
        for (int i = 0; i < 16; i++)
        {
            Write($"{i}.kicad_sch", $"{i + 1}.kicad_sch", $"{i + 1}.kicad_sch");
        }

        Write("16.kicad_sch");
        var issues = new List<HierarchyDiagnostic>();
        var places = SchHierarchy.Walk(Path.Combine(_directory, "0.kicad_sch"), report: issues.Add, cancellationToken: TestContext.Current.CancellationToken);
        Assert.InRange(places.Count, 1, 10_000);
        Assert.Equal(HierarchyProblem.InstanceLimit, Assert.Single(issues).Problem);
    }

    [Fact]
    public void A_walk_can_be_cancelled()
    {
        string root = Write("root.kicad_sch");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => SchHierarchy.Walk(root, cancellationToken: cancelled.Token));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
