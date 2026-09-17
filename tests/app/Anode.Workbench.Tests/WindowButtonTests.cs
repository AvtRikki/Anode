using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Anode.Workbench.Tests;

/// <summary>
/// The window buttons are ours on macOS: the system keeps its own in a band of its own height, which cannot be
/// centred in a taller bar. These check the three of them do what their colours promise.
/// </summary>
public class WindowButtonTests
{
    private static (MainWindow Window, IReadOnlyList<Button> Buttons) Open()
    {
        var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
        var window = new MainWindow { DataContext = ShellWindowTests.Workbench(pluginsRoot: null, recents), Width = 1240, Height = 772 };
        window.Show();

        var group = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "WindowButtons");
        return (window, [.. group.Children.OfType<Button>()]);
    }

    [Fact]
    public Task The_bar_carries_three_buttons() => ShellWindowTests.Dispatch(_ =>
    {
        var (window, buttons) = Open();

        Assert.Equal(3, buttons.Count);
        Assert.Equal(["close", "minimise", "zoom"], buttons.Select(b => b.Classes.First(c => c != "windowButton")));
        Assert.All(buttons, b => Assert.Equal(12, b.Width));

        window.Close();
    });

    [Fact]
    public Task Each_light_carries_the_mark_the_system_draws() => ShellWindowTests.Dispatch(_ =>
    {
        var (window, buttons) = Open();
        var group = (StackPanel)buttons[0].Parent!;
        var glyphs = buttons.Select(b => Assert.IsType<Avalonia.Controls.Shapes.Path>(b.Content)).ToList();

        Assert.All(glyphs, glyph => Assert.NotNull(glyph.Data));

        // Hidden until the pointer is over the group, then all three at once — as macOS does it.
        Assert.All(glyphs, glyph => Assert.Equal(0, glyph.Opacity));

        group.Classes.Set("hover", true);
        Assert.All(glyphs, glyph => Assert.Equal(1, glyph.Opacity));

        group.Classes.Set("hover", false);
        Assert.All(glyphs, glyph => Assert.Equal(0, glyph.Opacity));

        window.Close();
    });

    [Fact]
    public Task Zoom_maximises_and_restores() => ShellWindowTests.Dispatch(_ =>
    {
        var (window, buttons) = Open();
        var zoom = buttons[2];

        Assert.Equal(WindowState.Normal, window.WindowState);

        zoom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);

        zoom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.Close();
    });

    [Fact]
    public Task Minimise_sends_the_window_down() => ShellWindowTests.Dispatch(_ =>
    {
        var (window, buttons) = Open();

        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        window.Close();
    });

    [Fact]
    public Task Close_closes_the_window() => ShellWindowTests.Dispatch(_ =>
    {
        var (window, buttons) = Open();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
    });
}
