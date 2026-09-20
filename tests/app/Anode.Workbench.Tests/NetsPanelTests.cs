using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The nets of the open sheet, listed in a panel of their own: choosing one lights it on the canvas, as the status
/// bar then says, and choosing it again puts it out. The board's layers sit in the same place, so the panel appears
/// only for a sheet.
/// </summary>
public class NetsPanelTests
{
    [Fact]
    public Task A_net_chosen_in_the_panel_is_lit_on_the_sheet()
    {
        if (TestData.AnySchematic() is not { } fixture)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-nets-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(fixture));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            // The sheet brings the panel; the tab has to be shown for the list to be drawn.
            var stack = shell.LeftStacks.First(s => s.Tabs.Any(t => t.Descriptor.Id == "sch.nets"));
            stack.Select(stack.Tabs.First(t => t.Descriptor.Id == "sch.nets"));
            Dispatcher.UIThread.RunJobs();

            var panel = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.GetType().Name == "NetsPanel");
            Assert.True(panel is not null,
                $"no nets panel among [{string.Join(", ", shell.LeftStacks.SelectMany(s => s.Tabs).Select(t => t.Descriptor.Id))}] "
                + $"/ rail [{string.Join(", ", shell.Rail.Select(r => r.Descriptor.Id))}]");
            var rows = panel.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("row")).ToList();
            Assert.NotEmpty(rows);

            // Choosing a row lights that net, and the status bar names it.
            // The row is "name … pins"; the name is the one that is not docked to the right.
            string Name(Button row) => row.GetVisualDescendants().OfType<TextBlock>()
                .First(t => DockPanel.GetDock(t) != Dock.Right && !string.IsNullOrWhiteSpace(t.Text)).Text!;
            string name = Name(rows[0]);
            rows[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                document!.StatusFields.Any(f => f.Text.Contains(name, StringComparison.Ordinal)),
                $"row '{name}' lit nothing: [{string.Join(" | ", document.StatusFields.Select(f => f.Text))}]");
            ShellWindowTests.Snapshot(window, directory, "nets-panel");

            // The same row again puts it out.
            var again = panel.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("row"));
            string second = Name(again);
            again.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(
                !document.StatusFields.Any(f => f.Text.Contains(name, StringComparison.Ordinal)),
                $"clicking '{second}' did not put '{name}' out: [{string.Join(" | ", document.StatusFields.Select(f => f.Text))}]");

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        });
    }

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 4000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 4 s.");
        return task.GetAwaiter().GetResult();
    }
}
