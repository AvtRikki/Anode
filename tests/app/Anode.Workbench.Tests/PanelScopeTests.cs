using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Panels belong to what is on the canvas: a board brings its layers, a sheet brings its hierarchy, and neither
/// shows up while the other is in front. Both plugins are loaded from the workbench's own output, the way they ship.
/// </summary>
public class PanelScopeTests
{
    internal static string PluginsRoot => Path.Combine(TestData.RepoRoot, "src", "app", "Anode.Workbench", "bin",
#if DEBUG
        "Debug",
#else
        "Release",
#endif
        "net10.0", "plugins");

    [Fact]
    public Task Panels_follow_the_active_document()
    {
        if (TestData.AnyBoard() is not { } board || TestData.AnySchematic() is not { } sheet)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = App.CreateWorkbench(PluginsRoot, recents);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            Assert.Equal(["anode.pcb", "anode.schematic"], shell.DocumentTypes.Types.Select(t => t.Id).Order());

            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            // The start page belongs to no domain, so only the workbench's own panels are up.
            ShellContributions.ShowStartPage(shell);
            Assert.Contains("shell.project", Placed(shell));
            Assert.DoesNotContain("pcb.layers", Placed(shell));

            var boardDocument = Pump(shell.OpenAsync(board));
            Assert.NotNull(boardDocument);
            Assert.Contains("pcb.layers", Placed(shell));

            // A sheet in front takes the board's panels away; the schematic brings structure, not panels of its own.
            Pump(shell.OpenAsync(sheet));
            Assert.DoesNotContain("pcb.layers", Placed(shell));

            // Coming back to the board brings its panels back with it.
            shell.Activate(boardDocument!);
            Assert.Contains("pcb.layers", Placed(shell));

            window.Close();
        });
    }

    /// <summary>Panels that have a place right now: docked as a tab, or waiting in the rail.</summary>
    private static IReadOnlyList<string> Placed(ShellViewModel shell) =>
    [
        .. shell.LeftStacks.Concat(shell.RightStacks).Append(shell.BottomStack).OfType<DockStackViewModel>()
            .SelectMany(s => s.Tabs).Select(t => t.Descriptor.Id),
        .. shell.Rail.Select(r => r.Descriptor.Id),
    ];

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The document did not open within 10 s.");
        return task.GetAwaiter().GetResult();
    }
}
