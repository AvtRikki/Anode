using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Tests;

/// <summary>
/// Undo and redo live in the title bar, and the active document decides whether they are there at all: the frame
/// reads the command registry, so a document that registers no history has no buttons.
/// </summary>
public class HeaderHistoryTests
{
    [Fact]
    public Task A_document_without_history_has_no_buttons() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);

        // The start page cannot be edited, so it offers no undo.
        Assert.False(shell.HasHistory);
        Assert.False(shell.CanUndo);
        Assert.False(shell.CanRedo);

        var undo = Button(window, "UndoButton");
        var redo = Button(window, "RedoButton");
        Assert.False(undo.IsVisible);
        Assert.False(redo.IsVisible);

        window.Close();
    });

    [Fact]
    public Task The_buttons_follow_the_commands_a_document_registers() => ShellWindowTests.Dispatch(_ =>
    {
        var (shell, window) = ShellWindowTests.Open(ThemeVariant.Dark);
        var undo = Button(window, "UndoButton");

        // A document that registers undo puts the buttons up; they stay disabled until there is something to undo.
        using var registration = shell.Commands.Register(new Anode.Sdk.CommandDescriptor("edit.undo", "sch.command.undo")
        {
            CanExecute = () => false,
            Execute = () => { },
        });

        Assert.True(shell.HasHistory);
        Assert.False(shell.CanUndo);
        Assert.True(undo.IsVisible);
        Assert.False(undo.IsEnabled);

        // The tip is the command's own title, so it says what would be undone in the document's own words.
        Assert.Equal(shell.Commands.Find("edit.undo")?.Title, shell.UndoTip);

        window.Close();
    });

    private static Button Button(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);
}
