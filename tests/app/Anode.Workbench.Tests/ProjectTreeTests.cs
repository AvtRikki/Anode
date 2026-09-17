using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The project tree shows a project two ways: what it is made of, and what is on disk. These open a real KiCad demo
/// through the plugins and read the rows the panel built.
/// </summary>
public class ProjectTreeTests
{
    [Fact]
    public Task The_tree_lists_the_design_by_section()
    {
        if (TestData.AnyBoard() is not { } board)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            var (shell, window, panel) = OpenProject(board);

            // Sections name what the project is made of; the board and its schematic are under their own headings.
            var texts = Texts(panel);
            Assert.Contains("BOARD", texts);
            Assert.Contains(Path.GetFileNameWithoutExtension(board), texts);
            if (Directory.EnumerateFiles(Path.GetDirectoryName(board)!, "*.kicad_sch").Any())
            {
                Assert.Contains("SCHEMATIC", texts);
            }

            // The file the board came from is the selected row.
            Assert.Contains(Rows(panel), r => r.Classes.Contains("selected"));

            window.Close();
        });
    }

    [Fact]
    public Task The_files_view_shows_the_folder_as_it_is()
    {
        if (TestData.AnyBoard() is not { } board)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            var (shell, window, panel) = OpenProject(board);

            // The switch in the panel header changes the view; section headings give way to plain file names.
            var files = Rows(panel).First(r => r.Content is string text && text == "Files");
            files.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var texts = Texts(panel);
            // On disk a name is the whole name, extension and all.
            Assert.DoesNotContain("BOARD", texts);
            Assert.Contains(Path.GetFileName(board), texts);

            window.Close();
        });
    }

    private static (ShellViewModel Shell, MainWindow Window, Control Panel) OpenProject(string board)
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
        var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
        var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
        window.Show();

        var open = shell.OpenAsync(board);
        for (int i = 0; i < 2000 && !open.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.NotNull(open.Result);
        Dispatcher.UIThread.RunJobs();

        var descriptor = Assert.Single(shell.Panels.Panels, p => p.Id == "shell.project");
        var panel = descriptor.CreateContent(shell);
        var host = new ContentControl { Content = panel, Width = 240, Height = 600 };
        window.Content = host;
        Dispatcher.UIThread.RunJobs();

        return (shell, window, panel);
    }

    private static IReadOnlyList<Button> Rows(Control panel) => [.. panel.GetVisualDescendants().OfType<Button>()];

    private static IReadOnlyList<string> Texts(Control panel) =>
    [
        .. panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).Where(t => t.Length > 0),
    ];
}
