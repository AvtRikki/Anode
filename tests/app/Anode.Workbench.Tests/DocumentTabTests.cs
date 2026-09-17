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
        var shell = ShellWindowTests.Workbench(pluginsRoot: null, recents);
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
    public Task Each_rail_lists_the_panels_of_its_own_side() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();

        // The panels are docked, and their icons are in the rail all the same: a rail is the list of the panels of
        // that side, not a bin for the ones that did not fit.
        Assert.Equal(["shell.project"], shell.LeftRailTop.Select(r => r.Descriptor.Id));
        Assert.Equal(["shell.inspector"], shell.RightRailTop.Select(r => r.Descriptor.Id));
        Assert.True(shell.HasRightRail);

        var project = shell.LeftRailTop[0];
        Assert.True(project.IsDocked);
        Assert.True(project.HasIcon);
        Assert.NotNull(project.Icon);
        Assert.Equal("Project", project.Title);
        Assert.True(project.IsActive);

        // The bottom dock is run from the same rail: its icons stand at the foot of the left one.
        Assert.Equal(["shell.issues", "shell.console"], shell.BottomRail.Select(r => r.Descriptor.Id));
        Assert.True(shell.HasRailDivider || shell.LeftRailBottom.Count == 0);
    });

    [Fact]
    public Task A_second_press_on_the_active_tab_closes_the_bottom_dock() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();
        var bottom = shell.BottomStack!;

        Assert.True(shell.IsBottomDockShown);
        Assert.Equal(["Checks", "Console"], bottom.Tabs.Select(t => t.Title));

        // The bottom dock has no rail icons; its own tabs are the switch.
        bottom.ActiveTab!.ActivateCommand.Execute(null);
        Assert.True(bottom.IsCollapsed);
        Assert.False(shell.IsBottomDockShown);

        // Another tab of the same section opens it again, on its own panel.
        bottom.Tabs[1].ActivateCommand.Execute(null);
        Assert.False(bottom.IsCollapsed);
        Assert.True(shell.IsBottomDockShown);
        Assert.Equal("Console", bottom.ActiveTab?.Title);

        // The rail icons run the bottom dock the same way: one brings its panel up, a second closes the dock.
        var checks = Assert.Single(shell.BottomRail, r => r.Descriptor.Id == "shell.issues");
        checks.ToggleCommand.Execute(null);
        Assert.Equal("Checks", bottom.ActiveTab?.Title);
        Assert.True(checks.IsActive);

        checks.ToggleCommand.Execute(null);
        Assert.False(shell.IsBottomDockShown);
        Assert.False(checks.IsActive);
    });

    [Fact]
    public Task A_second_press_on_the_icon_closes_the_section() => ShellWindowTests.Dispatch(_ =>
    {
        var shell = Workbench();
        var project = Assert.Single(shell.Rail, r => r.Descriptor.Id == "shell.project");
        var inspector = Assert.Single(shell.Rail, r => r.Descriptor.Id == "shell.inspector");

        Assert.True(shell.IsLeftDockShown);
        Assert.True(project.IsActive);

        // The panel on show is already there, so pressing its icon closes the section and the dock goes with it.
        project.ToggleCommand.Execute(null);
        Assert.True(shell.LeftStacks[0].IsCollapsed);
        Assert.False(shell.IsLeftDockShown);
        Assert.False(project.IsActive);
        Assert.Null(shell.SlideOver);

        // The same icon brings it back.
        project.ToggleCommand.Execute(null);
        Assert.True(shell.IsLeftDockShown);
        Assert.True(project.IsActive);

        // The right side answers the same way.
        inspector.ToggleCommand.Execute(null);
        Assert.False(shell.IsRightDockShown);
        inspector.ToggleCommand.Execute(null);
        Assert.True(shell.IsRightDockShown);
        Assert.True(inspector.IsActive);
    });
}
