using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// A sheet placed twice in a design is one file and two places. The project tree offers each place as its own node,
/// and opening either lands in the same tab, turned to the place that was asked for — through the plugin's own
/// structure and document, loaded the way they ship.
/// </summary>
public class SheetAppearanceTests
{
    [Fact]
    public Task Each_place_of_a_reused_sheet_opens_the_one_tab_at_that_place()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        string amplifier = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "ampli_ht.kicad_sch");
        if (!File.Exists(root) || !File.Exists(amplifier))
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

            // The tree's level under the root: the amplifier twice, as two places with two paths.
            var places = shell.ProjectStructure.Describe(root)
                .Where(n => n.Path is { } p && Path.GetFullPath(p) == Path.GetFullPath(amplifier))
                .ToList();
            Assert.Equal(2, places.Count);
            Assert.All(places, p => Assert.NotNull(p.Instance));
            Assert.NotEqual(places[0].Instance, places[1].Instance);

            var first = Pump(shell.OpenAsync(places[0].Path!, places[0].Instance));
            Assert.NotNull(first);
            Assert.Equal(places[0].Instance, first!.Instance);
            Assert.Contains(places[0].Title, first.Summary, StringComparison.Ordinal);

            // The other place is the same file: the same tab, turned.
            var second = Pump(shell.OpenAsync(places[1].Path!, places[1].Instance));
            Assert.Same(first, second);
            Assert.Equal(places[1].Instance, second!.Instance);
            Assert.Contains(places[1].Title, second.Summary, StringComparison.Ordinal);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        });
    }

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 2 s.");
        return task.GetAwaiter().GetResult();
    }
}
