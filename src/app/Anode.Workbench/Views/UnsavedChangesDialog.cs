using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Anode.Sdk;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Views;

/// <summary>Asks whether to save changes before documents close.</summary>
internal sealed class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog(IReadOnlyList<IDocument> documents)
    {
        Title = Tr.T("shell.unsaved.title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = Ui.TagButton(Tr.T("shell.unsaved.save"), "accent", () => Close(UnsavedChoice.Save));
        save.IsDefault = true;
        var discard = Ui.TagButton(Tr.T("shell.unsaved.discard"), "alert", () => Close(UnsavedChoice.Discard));
        var cancel = Ui.TagButton(Tr.T("shell.unsaved.cancel"), "neutral", () => Close(UnsavedChoice.Cancel));
        cancel.IsCancel = true;

        string question = documents.Count == 1
            ? Tr.T("shell.unsaved.one", documents[0].Title)
            : Tr.T("shell.unsaved.many", documents.Count);

        var details = Ui.Text(documents.Count == 1
            ? Tr.T("shell.unsaved.detail")
            : string.Join(", ", documents.Select(d => d.Title)), "dim");
        details.TextWrapping = Avalonia.Media.TextWrapping.Wrap;

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                Ui.Text(question, "heading"),
                details,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { discard, cancel, save },
                },
            },
        };
    }
}
