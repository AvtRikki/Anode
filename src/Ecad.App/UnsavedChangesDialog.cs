using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ecad.App;

public enum UnsavedChoice
{
    Cancel,
    Save,
    Discard,
}

/// <summary>Asks whether to save changes before closing or opening another board.</summary>
internal sealed class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog(string fileName)
    {
        Title = "Unsaved changes";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 90 };
        var discard = new Button { Content = "Don't Save", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        save.Click += (_, _) => Close(UnsavedChoice.Save);
        discard.Click += (_, _) => Close(UnsavedChoice.Discard);
        cancel.Click += (_, _) => Close(UnsavedChoice.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = $"Save changes to “{fileName}”?", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Your changes will be lost if you don't save them.", TextWrapping = TextWrapping.Wrap },
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
