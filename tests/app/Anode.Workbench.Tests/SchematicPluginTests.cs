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

/// <summary>The schematic plugin through the same path as the PCB one: loaded from disk, opening a real sheet.</summary>
public class SchematicPluginTests
{
    internal static string PluginRoot => Path.Combine(TestData.RepoRoot, "src", "plugins", "Anode.Plugin.Schematic", "bin",
#if DEBUG
        "Debug",
#else
        "Release",
#endif
        "net10.0");

    [Fact]
    public Task Workbench_opens_a_schematic_sheet()
    {
        if (TestData.AnySchematic() is not { } sheet)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = App.CreateWorkbench(PluginRoot, recents);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            var type = Assert.Single(shell.DocumentTypes.Types);
            Assert.Equal("anode.schematic", type.Id);
            Assert.Equal("KiCad schematic", type.Label);

            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);
            Assert.Equal(Path.GetFileName(sheet), document.Title);
            Assert.False(document.CanSave);
            Assert.StartsWith("schematic · ", document.Summary, StringComparison.Ordinal);
            Assert.NotEmpty(document.StatusFields);

            // The sheets panel docks on the left next to the project panel.
            var sheets = Assert.Single(shell.LeftStacks.SelectMany(s => s.Tabs), t => t.Descriptor.Id == "sch.sheets");
            Assert.Equal("Sheets", sheets.Title);
            Assert.NotNull(shell.Commands.Find("sch.fit"));

            ShellWindowTests.Snapshot(window, directory, "schematic-sheet");
            window.Close();
        });
    }

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The sheet did not open within 10 s.");
        return task.GetAwaiter().GetResult();
    }
}
