using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Tools belong to the document and float over its canvas. The frame shows what the document offers and marks the
/// one in use; a document with no tools gets no bar.
/// </summary>
public class ToolBarTests
{
    [Fact]
    public Task A_document_without_tools_gets_no_bar() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);

        Assert.Empty(shell.ActivePane.Tools);
        Assert.False(shell.ActivePane.HasTools);

        window.Close();
    });

    [Fact]
    public Task A_sheet_offers_its_tools_and_marks_the_one_in_use()
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
            var shell = App.CreateWorkbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);

            // Opening must not be derailed by anything the frame does around it — the main menu included.
            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            Assert.Null(shell.CurrentBanner);

            // The document names its tools; the pane turns them into buttons.
            Assert.Equal(["sch.tool.select", "sch.tool.wire", "sch.tool.bus"], document!.Tools.Select(t => t.Id));
            Assert.True(shell.ActivePane.HasTools);
            Assert.Equal(3, shell.ActivePane.Tools.Count);
            Assert.All(shell.ActivePane.Tools, t => Assert.NotNull(t.Icon));

            // Nothing chosen means the pointer selects, and that is the button that reads as pressed.
            Assert.Null(document.ActiveToolId);
            Assert.True(shell.ActivePane.Tools[0].IsActive);
            Assert.False(shell.ActivePane.Tools[1].IsActive);

            // Choosing the wire marks it instead — from the tool button and from the command alike.
            shell.ActivePane.Tools[1].UseCommand.Execute(null);
            Assert.Equal("sch.tool.wire", document.ActiveToolId);
            Assert.True(shell.ActivePane.Tools[1].IsActive);
            Assert.False(shell.ActivePane.Tools[0].IsActive);

            Assert.True(shell.Commands.TryExecute("sch.tool.bus"));
            Assert.Equal("sch.tool.bus", document.ActiveToolId);

            // Back to the pointer.
            shell.ActivePane.Tools[0].UseCommand.Execute(null);
            Assert.Null(document.ActiveToolId);

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
