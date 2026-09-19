using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The design-wide designator check as the checks dock sees it: reported on a sheet that carries a clash, silent on a
/// reused sheet whose parts only look alike, and its "show" landing on the part.
/// </summary>
public class DuplicateIssueTests
{
    private static string Clash => Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "netlists",
        "test_multiunit_reannotate_5", "test_multiunit_reannotate_5.kicad_sch");

    private static string Amplifier => Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "ampli_ht.kicad_sch");

    [Fact]
    public Task A_clash_is_reported_and_shown_and_a_reused_sheet_is_not_one()
    {
        if (!File.Exists(Clash) || !File.Exists(Amplifier))
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            string title = Tr.T("sch.issue.duplicate.title");

            var clash = Pump(shell.OpenAsync(Clash));
            Assert.NotNull(clash);
            var issue = Assert.Single(clash!.Issues, i => i.Title == title);
            Assert.Equal(IssueSeverity.Error, issue.Severity);
            Assert.Contains("U2", issue.Detail, StringComparison.Ordinal);

            // "Show" selects the part, so the inspector is already on it.
            Assert.NotNull(issue.Action);
            issue.Action!();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("U2", clash.Selection?.Title);

            // Both places of the amplifier name the same parts, each with its own designators: no clash.
            var amplifier = Pump(shell.OpenAsync(Amplifier));
            Assert.NotNull(amplifier);
            Assert.DoesNotContain(amplifier!.Issues, i => i.Title == title);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        });
    }

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 4000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 4 s.");
        return task.GetAwaiter().GetResult();
    }
}
