using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// A symbol library opened in the window: it is a tab of its own, its symbols are listed beside it, a click there
/// brings a symbol onto the canvas, and its units are switched from the bar above.
/// </summary>
public class SymbolLibraryWindowTests
{
    [Fact]
    public Task A_library_opens_as_a_tab_and_a_symbol_is_chosen_from_the_panel() => ShellWindowTests.Dispatch(directory =>
    {
        string library = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "libs", "4xxx.kicad_sym");
        Assert.SkipUnless(File.Exists(library), TestData.SkipReason);

        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        string folder = Directory.CreateTempSubdirectory("anode-symlib-").FullName;
        try
        {
            string copy = Path.Combine(folder, "4xxx.kicad_sym");
            File.Copy(library, copy);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
            window.Show();

            var document = Pump(shell.OpenAsync(copy));
            Assert.NotNull(document);
            Assert.Equal("anode.symlib", document!.DocumentTypeId);
            Dispatcher.UIThread.RunJobs();

            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            // The library's symbols are listed; a click on one brings it up.
            var panel = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "SymbolListPanel");
            Assert.True(panel is not null, "the library's symbols were not listed");

            var row = panel!.GetVisualDescendants().OfType<Button>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "4001"));
            row.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
            Assert.NotNull(at);
            window.MouseDown(at!.Value, MouseButton.Left);
            window.MouseUp(at.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(document.StatusFields, f => f.Text.Contains("4001", StringComparison.Ordinal));

            // Its second gate, from the bar over the canvas.
            var view = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolLibraryView");
            var units = view.GetVisualDescendants().OfType<ComboBox>().First();
            Assert.Equal(5, units.ItemCount);
            units.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(document.StatusFields, f => f.Text.Contains("4001", StringComparison.Ordinal) && f.Text.EndsWith('B'));

            ShellWindowTests.Snapshot(window, directory, "symbol-library");

            // Nothing was changed, so nothing is dirty and nothing is lost.
            Assert.False(document.IsDirty);
            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 5000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 5 s.");
        return task.GetAwaiter().GetResult();
    }
}
