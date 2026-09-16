using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>Entry point for the headless session: the real App and theme, rendered through Skia.</summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .With(new FontManagerOptions { DefaultFamilyName = "avares://Anode.Workbench/Assets/Fonts#Source Serif 4" });
}

/// <summary>
/// Builds the workbench window headlessly and renders it. Frames are written to <c>snapshots/</c> next to the test
/// binaries (or <c>ANODE_SNAPSHOT_DIR</c>) for comparison with the mockups.
/// </summary>
public class ShellWindowTests
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    /// <summary>Runs <paramref name="body"/> on the headless UI thread and hands it the snapshot folder.</summary>
    internal static Task Dispatch(Action<string> body) => Session.Value.Dispatch(() =>
    {
        string directory = Environment.GetEnvironmentVariable("ANODE_SNAPSHOT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Tr.SetCulture(Tr.Neutral);
        }
    }, TestContext.Current.CancellationToken);

    internal static (ShellViewModel Shell, MainWindow Window) Open(ThemeVariant theme)
    {
        Application.Current!.RequestedThemeVariant = theme;
        var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
        var shell = App.CreateWorkbench(pluginsRoot: null, recents);
        ShellContributions.ShowStartPage(shell);
        var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (shell, window);
    }

    internal static void Snapshot(Window window, string directory, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [Fact]
    public Task Workbench_renders_start_page_with_docks_in_both_themes() => Dispatch(directory =>
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var (shell, window) = Open(theme);

            Assert.Single(shell.LeftStacks);
            Assert.Single(shell.RightStacks);
            Assert.NotNull(shell.BottomStack);
            Assert.Equal(["Checks", "Console"], shell.BottomStack!.Tabs.Select(t => t.Title));
            Assert.Equal("Start", shell.ActiveDocument?.Title);

            Snapshot(window, directory, $"start-{theme}");
            window.Close();
        }
    });

    [Fact]
    public Task Palette_opens_filters_and_executes() => Dispatch(directory =>
    {
        var (shell, window) = Open(ThemeVariant.Dark);

        shell.Commands.TryExecute("shell.palette");
        Assert.True(shell.IsPaletteOpen);
        shell.Palette.Query = "dock";
        Assert.Equal(["view.bottomDock", "view.leftDock", "view.rightDock"], shell.Palette.Results.Take(3).Select(r => r.Command.Id).Order());
        Snapshot(window, directory, "palette");

        shell.Palette.Query = "bottom dock";
        shell.ExecutePaletteSelection();
        Assert.False(shell.IsPaletteOpen);
        Assert.False(shell.IsBottomDockShown);
        window.Close();
    });

    [Fact]
    public Task Panel_sent_to_rail_opens_over_the_canvas_and_pins_back() => Dispatch(directory =>
    {
        var (shell, window) = Open(ThemeVariant.Dark);

        shell.SendToRail("shell.project");
        Assert.Empty(shell.LeftStacks);
        var item = Assert.Single(shell.Rail);

        item.ToggleCommand.Execute(null);
        Assert.Equal("shell.project", shell.SlideOver?.Id);
        Snapshot(window, directory, "slide-over");

        item.PinCommand.Execute(null);
        Assert.Null(shell.SlideOver);
        Assert.Empty(shell.Rail);
        Assert.Single(shell.LeftStacks);
        window.Close();
    });

    [Fact]
    public Task Language_switch_retitles_the_workbench() => Dispatch(directory =>
    {
        var (shell, window) = Open(ThemeVariant.Dark);
        Assert.Equal("Project", shell.LeftStacks[0].Tabs[0].Title);
        Assert.Equal("No project", shell.ProjectName);

        shell.SetLanguage(CultureInfo.GetCultureInfo("ru"));

        Assert.Equal("Проект", shell.LeftStacks[0].Tabs[0].Title);
        Assert.Equal("Нет проекта", shell.ProjectName);
        Assert.Equal("Начало", shell.ActiveDocument?.Title);
        Assert.Equal("Палитра команд", shell.Commands.Find("shell.palette")?.Title);
        Assert.Equal("Проект, компонент, команда…", shell.Strings.SearchPlaceholder);

        // The palette searches in the language on screen.
        shell.OpenPalette();
        shell.Palette.Query = "док";
        Assert.Equal(["view.bottomDock", "view.leftDock", "view.rightDock"], shell.Palette.Results.Take(3).Select(r => r.Command.Id).Order());
        shell.IsPaletteOpen = false;

        Snapshot(window, directory, "start-ru");
        window.Close();
    });
}
