using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Anode.Workbench.Tests;

/// <summary>Closing and pinning a tab: the two things a document tab must let you do with the mouse.</summary>
public class DocumentTabTests
{
    private sealed class StubDocument(string title) : DocumentBase
    {
        public override string Title => title;

        protected override Control CreateView() => new Border();
    }

    private static ShellViewModel Workbench()
    {
        var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-recents-{Guid.NewGuid():N}.json"));
        var shell = App.CreateWorkbench(pluginsRoot: null, recents);
        return shell;
    }

    [Fact]
    public Task A_tab_closes_from_its_own_button() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();
        shell.AddDocument(new StubDocument("one"));
        shell.AddDocument(new StubDocument("two"));

        var tab = shell.ActivePane.Tabs[1];
        Assert.True(tab.IsActive);
        Assert.Equal("Close tab", tab.CloseLabel);

        tab.CloseCommand.Execute(null);

        Assert.Equal(["one"], shell.ActivePane.Tabs.Select(t => t.Title));
        Assert.Equal("one", shell.ActiveDocument?.Title);
    });

    [Fact]
    public Task A_pinned_tab_moves_to_the_front_and_survives_close_others() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();
        foreach (string title in (string[])["one", "two", "three"])
        {
            shell.AddDocument(new StubDocument(title));
        }

        var third = shell.ActivePane.Tabs[2];
        Assert.Equal("Pin tab", third.PinLabel);

        third.TogglePinCommand.Execute(null);

        Assert.True(third.IsPinned);
        Assert.Equal("Unpin tab", third.PinLabel);
        Assert.Equal(["three", "one", "two"], shell.ActivePane.Tabs.Select(t => t.Title));

        // Closing the others keeps what was pinned, whichever tab the command runs from.
        var first = shell.ActivePane.Tabs.First(t => t.Title == "one");
        shell.ActivateTab(first);
        Assert.True(shell.Commands.TryExecute("doc.closeOthers"));

        Assert.Equal(["three", "one"], shell.ActivePane.Tabs.Select(t => t.Title));

        // Unpinning puts the tab back after the remaining pinned ones.
        third.TogglePinCommand.Execute(null);
        Assert.False(third.IsPinned);
        Assert.Contains(shell.ActivePane.Tabs, t => t.Title == "three");
    });

    [Fact]
    public Task Panels_in_the_rail_carry_an_icon() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();
        shell.SendToRail("shell.project");

        var item = Assert.Single(shell.Rail);
        Assert.True(item.HasIcon);
        Assert.NotNull(item.Icon);
        Assert.Equal("Project", item.Title);
    });
}
