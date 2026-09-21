using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// How strictly the electrical rules are applied. KiCad keeps this in the project, so a designer who has decided
/// that something is fine can say so once and stop hearing about it — and we have to read what they said.
/// </summary>
public class ErcRulesTests
{
    [Fact]
    public void Without_a_project_the_rules_are_KiCads_defaults()
    {
        var rules = ErcRules.Default;

        Assert.Equal(ErcSeverity.Error, rules.Severity(ErcKind.NotDriven));
        Assert.Equal(ErcSeverity.Error, rules.Severity(ErcKind.PowerNotDriven));
        Assert.Equal(ErcSeverity.Error, rules.Severity(ErcKind.SheetPinWithoutLabel));

        // Two outputs may not meet; an output and an input may.
        Assert.Equal(ErcSeverity.Error, rules.Conflict("output", "output"));
        Assert.Null(rules.Conflict("output", "input"));

        // A project that is not there leaves them alone.
        Assert.Equal(ErcSeverity.Error, ErcRules.For(null).Severity(ErcKind.NotDriven));
    }

    [Fact]
    public void A_project_can_soften_a_rule_or_silence_it()
    {
        var rules = Project("""
            { "erc": { "rule_severities": {
                "pin_not_driven": "warning",
                "power_pin_not_driven": "ignore",
                "hier_label_mismatch": "ignore" } } }
            """);

        Assert.Equal(ErcSeverity.Warning, rules.Severity(ErcKind.NotDriven));
        Assert.Null(rules.Severity(ErcKind.PowerNotDriven));
        Assert.Null(rules.Severity(ErcKind.SheetPinWithoutLabel));
        Assert.Null(rules.Severity(ErcKind.LabelWithoutSheetPin));

        // What it did not mention keeps KiCad's own answer.
        Assert.Equal(ErcSeverity.Error, rules.Conflict("output", "output"));
    }

    [Fact]
    public void Silencing_pin_conflicts_silences_the_whole_matrix()
    {
        var rules = Project("""{ "erc": { "rule_severities": { "pin_to_pin": "ignore" } } }""");

        Assert.Null(rules.Severity(ErcKind.PinConflict));
        Assert.Null(rules.Conflict("output", "output"));
        Assert.Null(rules.Conflict("unspecified", "passive"));
    }

    [Fact]
    public void The_pin_to_pin_setting_says_whether_to_look_not_how_badly_it_reads()
    {
        // KiCad's own rule: the setting decides ignore-or-not, the matrix cell decides warning-or-error.
        var rules = Project("""{ "erc": { "rule_severities": { "pin_to_pin": "warning" } } }""");

        Assert.Equal(ErcSeverity.Error, rules.Conflict("output", "output"));
        Assert.Equal(ErcSeverity.Warning, rules.Conflict("unspecified", "passive"));
    }

    [Fact]
    public void A_project_can_bring_its_own_pin_matrix()
    {
        // Every cell fine but one: an output meeting an output is only doubtful here.
        var rows = Enumerable.Range(0, 12).Select(_ => new int[12]).ToArray();
        rows[1][1] = 1;
        string map = string.Join(",", rows.Select(r => "[" + string.Join(",", r) + "]"));
        var rules = Project($$"""{ "erc": { "pin_map": [{{map}}] } }""");

        Assert.Equal(ErcSeverity.Warning, rules.Conflict("output", "output"));
        Assert.Null(rules.Conflict("no_connect", "passive"));
    }

    [Fact]
    public void A_matrix_that_is_not_the_right_shape_is_not_read()
    {
        var rules = Project("""{ "erc": { "pin_map": [[0, 0], [0, 0]] } }""");

        // Half a matrix would silently let conflicts through, so the defaults stand instead.
        Assert.Equal(ErcSeverity.Error, rules.Conflict("output", "output"));
    }

    [Fact]
    public void A_project_file_that_will_not_read_is_a_project_without_settings()
    {
        Assert.Equal(ErcSeverity.Error, Project("{ this is not json").Severity(ErcKind.NotDriven));
        Assert.Equal(ErcSeverity.Error, Project("""{ "erc": "nonsense" }""").Severity(ErcKind.NotDriven));
    }

    [Fact]
    public void A_real_projects_settings_are_read_as_KiCad_wrote_them()
    {
        string project = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_pro");
        Assert.SkipUnless(File.Exists(project), TestData.SkipReason);

        var rules = ProjectFile.Load(project).Erc;

        // KiCad writes every rule out; this demo leaves them at their defaults.
        Assert.Equal(ErcSeverity.Error, rules.Severity(ErcKind.PowerNotDriven));
        Assert.Equal(ErcSeverity.Error, rules.Conflict("output", "output"));
        Assert.Equal(ErcSeverity.Warning, rules.Conflict("unspecified", "passive"));
    }

    [Fact]
    public void A_silenced_rule_is_not_reported_at_all()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        Assert.SkipUnless(File.Exists(root), TestData.SkipReason);

        var nets = SchDesignNets.Build(root);

        Assert.Contains(SchErc.Check(nets), f => f.Kind == ErcKind.PowerNotDriven);
        Assert.Contains(SchErc.Check(nets), f => f.Kind == ErcKind.PinConflict);

        var quiet = Project("""
            { "erc": { "rule_severities": { "power_pin_not_driven": "ignore", "pin_to_pin": "ignore" } } }
            """);

        Assert.Empty(SchErc.Check(nets, quiet));
    }

    /// <summary>A project file written to disk and read back, as the program reads one.</summary>
    private static ErcRules Project(string json)
    {
        string folder = Directory.CreateTempSubdirectory("anode-erc-rules-").FullName;
        try
        {
            string path = Path.Combine(folder, "design.kicad_pro");
            File.WriteAllText(path, json);
            return ProjectFile.Load(path).Erc;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
