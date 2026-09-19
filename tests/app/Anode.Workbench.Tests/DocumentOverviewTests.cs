using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// With nothing selected the inspector describes the document itself rather than asking for a selection — a sheet
/// its paper, contents and nets, a board its size, layers and routing. Frames are saved for comparison by eye.
/// </summary>
public class DocumentOverviewTests
{
    [Fact]
    public Task With_nothing_selected_the_inspector_describes_the_document()
    {
        if (TestData.AnySchematic() is not { } sheet || TestData.AnyBoard() is not { } board)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            foreach (var (file, name) in new[] { (sheet, "overview-sheet"), (board, "overview-board") })
            {
                var document = Pump(shell.OpenAsync(file));
                Assert.NotNull(document);
                Assert.Null(document!.Selection);

                var overview = document.Overview;
                Assert.NotNull(overview);
                Assert.True(overview!.Blocks.Count >= 2, $"{name}: {overview.Blocks.Count} blocks");
                Assert.All(overview.Blocks.SelectMany(b => b.Rows), r => Assert.False(r.Name.Contains('.'), $"untranslated row {r.Name}"));

                // Shown in the panel itself, not only offered by the document.
                shell.Commands.TryExecute("shell.inspector.focus");
                Dispatcher.UIThread.RunJobs();
                ShellWindowTests.Snapshot(window, directory, name);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == overview.Title);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Tr.T("shell.inspector.empty"));
            }

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
