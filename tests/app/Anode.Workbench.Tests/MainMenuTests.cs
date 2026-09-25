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
/// The main menu is not a fixed list: it is built from the commands that exist right now, so it follows the document
/// in front. A sheet brings its drawing into Place; with no document there is nothing to place.
/// </summary>
public class MainMenuTests
{
    [Fact]
    public Task Without_a_document_there_is_nothing_to_place() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);
        var menu = MainMenu.Build(shell.Commands);

        // The shell's own menus are there, named from the catalog.
        Assert.Equal(["File", "View"], Titles(menu));
        Assert.Contains("Open…", Items(menu, "File"));
        Assert.DoesNotContain("Place", Titles(menu));

        window.Close();
    });

    [Fact]
    public Task Filling_the_same_menu_again_replaces_it_rather_than_growing_it() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);

        // macOS keeps the menu it was handed, so the workbench refills that one instead of making another. Filling
        // it twice must leave exactly one menu bar — this is what took the window down when it was done the other way.
        var menu = new NativeMenu();
        MainMenu.Fill(menu, shell.Commands);
        var first = Titles(menu);

        MainMenu.Fill(menu, shell.Commands);

        Assert.Equal(first, Titles(menu));
        Assert.Equal(["Open…"], Items(menu, "File").Where(i => i == "Open…"));

        window.Close();
    });

    [Fact]
    public Task A_sheet_brings_its_drawing_into_the_menu()
    {
        if (TestData.AnySchematic() is not { } sheet)
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

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);

            var menu = MainMenu.Build(shell.Commands);
            Assert.Equal(["File", "Edit", "View", "Place"], Titles(menu));
            // Place holds everything the sheet can draw, in the order the document asked for.
            Assert.Equal(
                [
                    "Draw a wire", "Draw a bus", "Unfold from bus", "Place a label", "Place a global label",
                    "Place a hierarchical label", "Place a no-connect", "Place a junction",
                    "Place a bus entry", "Place text", "Place a text box", "Cut a wire", "Place a child sheet", "Place a sheet pin",
                    "Draw a line", "Draw a rectangle", "Draw a circle", "Draw an arc", "Draw a curve",
                ],
                Items(menu, "Place"));

            // Edit opens with the history, then the clipboard, in the order the document asked for.
            Assert.Equal(
                ["Undo", "Redo", "Cut", "Copy", "Paste", "Duplicate", "Delete selection"],
                Items(menu, "Edit").Take(7));

            // Closing the sheet takes its menus with it.
            Pump(shell.CloseTabAsync(shell.ActivePane.Tabs[0]).ContinueWith(t => true, TaskScheduler.Default));
            Assert.DoesNotContain("Place", Titles(MainMenu.Build(shell.Commands)));

            window.Close();
        });
    }

    private static IReadOnlyList<string> Titles(NativeMenu menu) =>
        [.. menu.Items.OfType<NativeMenuItem>().Select(i => i.Header ?? string.Empty)];

    private static IReadOnlyList<string> Items(NativeMenu menu, string title) =>
    [
        .. menu.Items.OfType<NativeMenuItem>().First(i => i.Header == title).Menu!.Items
            .OfType<NativeMenuItem>().Select(i => i.Header ?? string.Empty),
    ];

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
