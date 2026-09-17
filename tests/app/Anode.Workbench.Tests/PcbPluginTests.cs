using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;
using Anode.Tests;

namespace Anode.Workbench.Tests;

/// <summary>
/// End to end through the real plugin mechanism: the workbench loads <c>plugins/pcb</c> from disk into its own load
/// context, and a KiCad board opens as a document tab with its layers panel and status fields.
/// </summary>
public class PcbPluginTests
{
    internal static string PluginRoot => Path.Combine(TestData.RepoRoot, "src", "plugins", "Anode.Plugin.Pcb", "bin",
#if DEBUG
        "Debug",
#else
        "Release",
#endif
        "net10.0");

    [Fact]
    public Task Workbench_loads_the_pcb_plugin_and_opens_a_board()
    {
        if (TestData.AnyBoard() is not { } board)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            // Headless renders through Skia; the GL surface needs a real context.
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PluginRoot, recents);

            // If the plugin failed to load, its reason is in the log; show it instead of an empty-collection failure.
            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);

            var type = Assert.Single(shell.DocumentTypes.Types);
            Assert.Equal("anode.pcb", type.Id);
            Assert.Equal("KiCad board", type.Label);
            Assert.Contains(shell.Panels.Panels, p => p.Id == "pcb.layers");

            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(board));
            Assert.NotNull(document);
            Assert.Equal(Path.GetFileName(board), document.Title);
            Assert.True(document.CanSave);
            Assert.False(document.IsDirty);
            Assert.NotEmpty(document.StatusFields);
            Assert.StartsWith("board · ", document.Summary, StringComparison.Ordinal);

            // The layers panel is a left dock stack next to the project panel, filled from the open board.
            var layers = Assert.Single(shell.LeftStacks.SelectMany(s => s.Tabs), t => t.Descriptor.Id == "pcb.layers");
            Assert.Equal("Layers", layers.Title);

            // Document commands replace the shell's placeholders while the board is active.
            Assert.NotNull(shell.Commands.Find("pcb.fit"));
            Assert.Equal("board", shell.Commands.Find("edit.undo")?.Scope);

            ShellWindowTests.Snapshot(window, directory, "pcb-board");
            window.Close();
        });
    }

    /// <summary>Runs the dispatcher until the task finishes: the open path hops back to the UI thread.</summary>
    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The board did not open within 10 s.");
        return task.GetAwaiter().GetResult();
    }
}
