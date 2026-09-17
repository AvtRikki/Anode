using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The parts a sheet can draw from, and the way to put one on it. Choosing a row arms the pointer; the next click
/// on the sheet drops the part, and the pointer stays armed so a row of them can be laid down.
///
/// The panel rebuilds when the document changes, not when it merely announces something: the canvas announces once
/// per drawn frame, and a list rebuilt that often would throw away the caret in its own filter box.
/// </summary>
internal sealed class SymbolsPanel : ContentControl
{
    private readonly IWorkbench _workbench;
    private readonly SymbolLibraryList _remembered;
    private readonly TextBox _filter;
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly TextBlock _summary;
    private SymbolChooser? _chooser;
    private string? _shownFor;

    public SymbolsPanel(IWorkbench workbench, SymbolLibraryList remembered)
    {
        _workbench = workbench;
        _remembered = remembered;

        _filter = new TextBox { Classes = { "filter" }, PlaceholderText = Tr.T("sch.symbols.filter") };
        _filter.TextChanged += (_, _) => Filter(_filter.Text ?? string.Empty);

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;

        var add = Ui.TagButton(Tr.T("sch.symbols.add"), "outline", () => _ = AddLibraryAsync());
        add.HorizontalAlignment = HorizontalAlignment.Left;

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                new StackPanel
                {
                    Spacing = 7,
                    Margin = new Thickness(11, 10),
                    [DockPanel.DockProperty] = Dock.Top,
                    Children = { _filter, _summary },
                },
                new Border
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Padding = new Thickness(11, 10),
                    Child = add,
                },
                new ScrollViewer { Content = _rows },
            },
        };

        workbench.ActiveDocumentChanged += Rebuild;
        Tr.Changed += Retranslate;
        Rebuild();
    }

    private SchematicDocument? Sheet => _workbench.ActiveDocument as SchematicDocument;

    private void Retranslate()
    {
        _filter.PlaceholderText = Tr.T("sch.symbols.filter");
        _shownFor = null;
        Rebuild();
    }

    /// <summary>Reads the libraries of whatever sheet is in front, unless that is the one already listed.</summary>
    private void Rebuild()
    {
        string? path = Sheet?.FilePath;
        if (path == _shownFor && _chooser is not null)
        {
            return;
        }

        _shownFor = path;
        if (Sheet is null)
        {
            _chooser = null;
            _rows.Children.Clear();
            _summary.Text = string.Empty;
            return;
        }

        _chooser = new SymbolChooser(ProjectLibraries.For(path, _remembered.Load()));
        _chooser.Query = _filter.Text ?? string.Empty;
        Show();
    }

    private void Filter(string query)
    {
        if (_chooser is null)
        {
            return;
        }

        _chooser.Query = query;
        Show();
    }

    private void Show()
    {
        _rows.Children.Clear();
        if (_chooser is not { } chooser)
        {
            return;
        }

        if (chooser.Results.Count == 0)
        {
            bool anyLibrary = chooser.Query.Length > 0;
            var empty = Ui.Text(Tr.T(anyLibrary ? "sch.symbols.empty" : "sch.symbols.none"), "dim");
            empty.Margin = new Thickness(11, 4);
            _rows.Children.Add(empty);

            if (!anyLibrary)
            {
                var hint = Ui.Text(Tr.T("sch.symbols.hint"), "faint");
                hint.FontSize = 11.5;
                hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                hint.TextTrimming = Avalonia.Media.TextTrimming.None;
                hint.Margin = new Thickness(11, 2, 11, 4);
                _rows.Children.Add(hint);
            }

            _summary.Text = string.Empty;
            return;
        }

        foreach (var choice in chooser.Results)
        {
            _rows.Children.Add(Row(choice));
        }

        _summary.Text = Tr.T("sch.symbols.count", chooser.Results.Count, chooser.Limit);
    }

    private Control Row(SymbolChoice choice)
    {
        var lines = new StackPanel { Spacing = 1 };
        lines.Children.Add(Ui.Text(choice.Name, "strong"));

        var where = Ui.Mono(choice.Library, "dim");
        lines.Children.Add(where);

        if (choice.Description is { Length: > 0 } description)
        {
            var text = Ui.Text(description, "faint");
            text.FontSize = 11.5;
            lines.Children.Add(text);
        }

        var button = new Button { Classes = { "row" }, Content = lines, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(11, 6);
        button.Click += (_, _) => Sheet?.ChoosePart(choice.LibId, choice.Symbol);
        ToolTip.SetTip(button, choice.LibId);
        return button;
    }

    /// <summary>
    /// Adds a library to the project's own table — the file KiCad itself reads — and remembers it for every other
    /// project besides.
    /// </summary>
    private async Task AddLibraryAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || Sheet?.FilePath is not { } sheet)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.T("sch.symbols.add"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("KiCad") { Patterns = ["*.kicad_sym"] }],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        _remembered.Add(path);

        if (ProjectLibraries.ProjectFolder(sheet) is { } project)
        {
            string? nickname = ProjectLibraries.AddToProjectTable(project, path);
            _workbench.Log.Info(Tr.T(nickname is null ? "sch.symbols.known" : "sch.symbols.added", nickname ?? path));
        }

        _shownFor = null;
        Rebuild();
    }
}
