using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// A tool with kinds keeps one button: press and hold shows the kinds, a plain press just takes the tool. The bar
/// carries no chevrons, so this is the only way in — which makes it worth driving with a real pointer.
/// </summary>
public class ToolHoldTests
{
    [Fact]
    public Task Holding_a_tool_shows_its_kinds()
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

            Assert.NotNull(Pump(shell.OpenAsync(sheet)));
            Dispatcher.UIThread.RunJobs();

            // The bar is rebuilt as the document settles, so a frame is drawn before anything is measured on it.
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            // The label is the tool that has kinds; its button is the only one that carries them.
            Button button = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.DataContext is ToolButtonViewModel { HasVariants: true });

            // A button with no size would turn "the middle of it" into a corner of something else.
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0, $"the tool button has no size: {button.Bounds}");

            FlyoutBase flyout = FlyoutBase.GetAttachedFlyout(button)!;
            Assert.NotNull(flyout);
            Assert.False(flyout.IsOpen);

            Point? middle = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(middle);

            // Where the press actually lands decides everything here, so it is recorded rather than assumed.
            string? landedOn = null;
            string? ancestor = null;
            window.AddHandler(
                InputElement.PointerPressedEvent,
                (_, e) =>
                {
                    landedOn ??= (e.Source as Visual)?.GetType().Name;

                    // What the workbench itself computes from this press: the tool button behind the hit, if any.
                    ancestor ??= (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext?.GetType().Name
                        ?? "none";
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            // The bar keeps its buttons now, but the press must go to whatever is live at this moment.
            button = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.DataContext is ToolButtonViewModel { HasVariants: true });
            flyout = FlyoutBase.GetAttachedFlyout(button)!;
            middle = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(middle);

            // Headless follows a pointer that has been moved somewhere first.
            window.MouseMove(middle!.Value);
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(middle.Value, MouseButton.Left);

            // The hold is a real wait, so the test waits with it.
            for (int i = 0; i < 100 && !flyout.IsOpen; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            // If this ever fails again, the message says where the press actually went.
            Assert.True(
                flyout.IsOpen,
                $"the press landed on {landedOn ?? "nothing"}, and the tool button behind it carried {ancestor ?? "nothing"}");

            var items = Assert.IsType<MenuFlyout>(flyout).ItemsSource?.Cast<ToolButtonViewModel>().ToList();
            Assert.NotNull(items);
            Assert.Equal(3, items!.Count);

            window.MouseUp(middle.Value, MouseButton.Left);
            window.Close();
        });
    }

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The sheet did not open within 2 s.");
        return task.GetAwaiter().GetResult();
    }
}
