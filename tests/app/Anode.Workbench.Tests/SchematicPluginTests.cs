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
            // The sheet is editable now: it can be saved, and it starts clean.
            Assert.True(document.CanSave);
            Assert.False(document.IsDirty);
            Assert.StartsWith("schematic · ", document.Summary, StringComparison.Ordinal);
            Assert.NotEmpty(document.StatusFields);

            // The hierarchy is not a panel of its own any more: the plugin describes the file and the project tree
            // shows it, so the schematic domain contributes structure rather than a second navigation panel.
            Assert.DoesNotContain(shell.Panels.Panels, p => p.Id == "sch.sheets");
            Assert.NotEmpty(shell.ProjectStructure.Contributors);
            Assert.NotNull(shell.Commands.Find("sch.fit"));

            // Editing commands are what the title bar's history buttons are driven by.
            Assert.NotNull(shell.Commands.Find("edit.undo"));
            Assert.NotNull(shell.Commands.Find("edit.redo"));
            Assert.Equal("schematic", shell.Commands.Find("edit.undo")?.Scope);
            Assert.True(shell.HasHistory);
            Assert.False(shell.CanUndo);

            // A hierarchical sheet reports its children as instances — the same file may appear under two names.
            string hierarchy = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
            if (File.Exists(hierarchy))
            {
                var children = shell.ProjectStructure.Describe(hierarchy);
                Assert.Equal(2, children.Count);
                Assert.Equal(["ampli_ht_horizontal", "ampli_ht_vertical"], children.Select(c => c.Title).Order(StringComparer.Ordinal));
                Assert.All(children, c => Assert.Equal(Icons.Sheets, c.IconKey));
            }

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
