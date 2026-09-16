using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The hierarchy of the open schematic (mockup 2c, "Sheets"): the sheet in front of you and the child sheets it
/// refers to. Clicking a child opens it as its own tab.
/// </summary>
public sealed class SheetsPanel : ContentControl
{
    private readonly IWorkbench _workbench;

    public SheetsPanel(IWorkbench workbench)
    {
        _workbench = workbench;
        workbench.ActiveDocumentChanged += Render;
        Tr.Changed += Render;
        Render();
    }

    private void Render()
    {
        if (_workbench.ActiveDocument is not SchematicDocument document)
        {
            Content = Ui.Text(Tr.T("sch.sheets.empty"), "dim");
            return;
        }

        var root = new StackPanel { Spacing = 12 };

        var header = new StackPanel { Spacing = 1 };
        header.Children.Add(Ui.Text(document.Title, "heading"));
        if (document.Sheet.TitleBlock.Title is { Length: > 0 } title)
        {
            var subtitle = Ui.Text(title, "dim");
            subtitle.TextWrapping = TextWrapping.Wrap;
            header.Children.Add(subtitle);
        }

        root.Children.Add(header);

        var list = new StackPanel { Spacing = 2 };
        list.Children.Add(Ui.Overline(Tr.T("sch.sheets.children")));

        if (document.Sheet.Sheets.Count == 0)
        {
            list.Children.Add(Ui.Text(Tr.T("sch.sheets.root"), "dim"));
        }

        string? directory = document.FilePath is { } path ? Path.GetDirectoryName(path) : null;
        foreach (var sheet in document.Sheet.Sheets)
        {
            string? file = sheet.SheetFile is { Length: > 0 } name && directory is not null
                ? Path.Combine(directory, name)
                : null;
            bool exists = file is not null && File.Exists(file);

            var line = new StackPanel();
            line.Children.Add(Ui.Text(sheet.SheetName ?? sheet.SheetFile ?? "—", exists ? string.Empty : "faint"));
            line.Children.Add(Ui.Mono(exists ? sheet.SheetFile! : Tr.T("sch.sheets.missing"), "dim"));

            var row = new Button
            {
                Classes = { "row" },
                Padding = new Thickness(6, 4),
                Content = line,
                IsEnabled = exists,
            };

            if (exists)
            {
                string target = file!;
                row.Click += (_, _) => _ = _workbench.OpenAsync(target);
            }

            list.Children.Add(row);
        }

        root.Children.Add(list);
        Content = new ScrollViewer { Content = root };
    }
}
