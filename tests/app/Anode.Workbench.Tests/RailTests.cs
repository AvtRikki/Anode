using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
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
/// The rail is clicked, not read: every icon in it must bring its panel up and survive the layout pass that follows.
/// A panel view is one control that moves between a dock stack and the slide-over, so a click that leaves it hanging
/// off two hosts takes the window down with it — these press every icon and render a frame after each press, through
/// the states a session actually goes through.
/// </summary>
public class RailTests
{
    [Fact]
    public Task Every_rail_icon_shows_its_panel_and_the_window_still_renders() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);

        ClickEveryIcon(shell, window);

        window.Close();
    });

    [Fact]
    public Task The_rail_survives_the_states_a_session_goes_through()
    {
        if (TestData.AnyBoard() is not { } board)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
            var shell = App.CreateWorkbench(PcbPluginTests.PluginRoot, recents);
            ShellContributions.ShowStartPage(shell);

            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();
            Frame(window, shell);

            var open = shell.OpenAsync(board);
            for (int i = 0; i < 2000 && !open.IsCompleted; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            Assert.NotNull(open.Result);
            Frame(window, shell);

            // The board brings its own panels, so the rail is not the one the start page had.
            Assert.Contains(shell.Rail, r => r.Descriptor.Id == "pcb.layers");
            ClickEveryIcon(shell, window);

            // Every document switch replans the docks and moves the panel views between stacks.
            foreach (var tab in shell.ActivePane.Tabs.ToList())
            {
                shell.ActivateTab(tab);
                Frame(window, shell);
                ClickEveryIcon(shell, window);
            }

            // A collapsed stack, and a hidden dock: the icon is still there and still has to answer.
            foreach (var stack in shell.LeftStacks.ToList())
            {
                stack.ToggleCollapsedCommand.Execute(null);
                Frame(window, shell);
            }

            ClickEveryIcon(shell, window);

            shell.IsLeftDockVisible = false;
            Frame(window, shell);
            ClickEveryIcon(shell, window);
            shell.IsLeftDockVisible = true;
            Frame(window, shell);

            // Out of the dock and over the canvas, then back into a dock: the same view changes host twice.
            shell.SendToRail("pcb.layers");
            Frame(window, shell);
            ClickEveryIcon(shell, window);
            shell.PinFromRail("pcb.layers");
            Frame(window, shell);
            ClickEveryIcon(shell, window);

            // A language switch rebuilds every panel's contents underneath all of that.
            shell.SetLanguage(new CultureInfo("ru"));
            Frame(window, shell);
            ClickEveryIcon(shell, window);

            ShellWindowTests.Snapshot(window, directory, "rail-board");
            window.Close();
        });
    }

    /// <summary>Presses every icon in the rail, rendering in between: the crash is in the layout pass, not the click.</summary>
    private static void ClickEveryIcon(ShellViewModel shell, Window window)
    {
        foreach (var item in shell.Rail.ToList())
        {
            item.ToggleCommand.Execute(null);
            Frame(window, shell);
        }
    }

    private static void Frame(Window window, ShellViewModel shell)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
    }
}
