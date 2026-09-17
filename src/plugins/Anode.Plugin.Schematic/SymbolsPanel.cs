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
    private readonly DisabledSources _disabled;
    private readonly TextBox _filter;
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly TextBlock _summary;
    private readonly Button _sources;
    private readonly TextBlock _armed;
    private readonly Dictionary<string, Button> _byLibId = [];
    private SymbolChooser? _chooser;
    private SymbolIndex? _index;
    private string? _shownFor;

    public SymbolsPanel(IWorkbench workbench, SymbolLibraryList remembered, DisabledSources disabled)
    {
        _workbench = workbench;
        _remembered = remembered;
        _disabled = disabled;

        _filter = new TextBox { Classes = { "filter" }, PlaceholderText = Tr.T("sch.symbols.filter") };
        _filter.TextChanged += (_, _) => Filter(_filter.Text ?? string.Empty);

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;

        _sources = Ui.TagButton(Tr.T("sch.symbols.sources"), "outline", ShowSources);
        _sources.HorizontalAlignment = HorizontalAlignment.Right;

        // Nothing told the reader that a part was on the pointer, so a click on a row looked like nothing at all.
        _armed = Ui.Text(string.Empty, "accentText");
        _armed.FontSize = 11.5;
        _armed.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _armed.TextTrimming = Avalonia.Media.TextTrimming.None;
        _armed.IsVisible = false;

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
                    Children =
                    {
                        _filter,
                        _armed,
                        new DockPanel
                        {
                            LastChildFill = false,
                            Children =
                            {
                                new Panel { [DockPanel.DockProperty] = Dock.Right, Children = { _sources } },
                                _summary,
                            },
                        },
                    },
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

        // Cheap: only the marks and the hint, never the list — a rebuild here would undo the caret in the filter.
        workbench.ActiveDocumentStateChanged += MarkChosen;
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

        _index = ProjectLibraries.For(path, _remembered.Load());
        foreach (string nickname in _disabled.For(Project))
        {
            _index.SetEnabled(nickname, false);
        }

        _chooser = new SymbolChooser(_index);
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

        _byLibId.Clear();
        foreach (var choice in chooser.Results)
        {
            var row = Row(choice);
            _byLibId[choice.LibId] = row;
            _rows.Children.Add(row);
        }

        MarkChosen();

        // The limit is ours, not the library's: saying "5 of 200" would read as though 200 parts existed.
        _summary.Text = chooser.Results.Count >= chooser.Limit
            ? Tr.T("sch.symbols.capped", chooser.Limit)
            : Tr.T("sch.symbols.count", chooser.Results.Count);

        UpdateSources();
    }

    /// <summary>The project this sheet belongs to, which is what a switched-off source is remembered against.</summary>
    private string? Project => ProjectLibraries.ProjectFolder(Sheet?.FilePath);

    /// <summary>The button says how many of the libraries found are actually being offered.</summary>
    private void UpdateSources()
    {
        var libraries = _index?.Libraries ?? [];
        _sources.Content = libraries.Count == 0
            ? Tr.T("sch.symbols.sources")
            : Tr.T("sch.symbols.sourcesOf", libraries.Count(l => l.IsEnabled), libraries.Count);
        _sources.IsEnabled = libraries.Count > 0;
    }

    /// <summary>
    /// Every library this project can reach, with a tick against the ones being offered and a word for where each
    /// came from. Turning one off leaves its row here, so it can be turned back on.
    /// </summary>
    private void ShowSources()
    {
        if (_index is not { } index || index.Libraries.Count == 0)
        {
            return;
        }

        var remembered = _remembered.Load();
        var flyout = new MenuFlyout();

        foreach (var library in index.Libraries)
        {
            string origin = library.IsProject
                ? Tr.T("sch.symbols.sourceProject")
                : remembered.Any(r => string.Equals(Path.GetFullPath(r), library.Path, PathComparison))
                    ? Tr.T("sch.symbols.sourceAdded")
                    : Tr.T("sch.symbols.sourceInstalled");

            if (library.Problem is not null)
            {
                origin = Tr.T("sch.symbols.sourceBroken");
            }

            // The tick belongs in the icon column, not in the text: a menu reserves that column whether it is
            // filled or not, so a library that is off keeps its name in line with one that is on.
            var item = new MenuItem
            {
                Header = $"{library.Nickname}  ·  {origin}",
                Icon = library.IsEnabled ? Ui.Text("✓") : null,
                IsEnabled = library.Problem is null,
            };

            var row = library;
            item.Click += (_, _) => Toggle(row.Nickname, !row.IsEnabled);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(_sources);
    }

    private void Toggle(string nickname, bool enabled)
    {
        _index?.SetEnabled(nickname, enabled);
        _disabled.Set(Project, nickname, enabled);
        _chooser?.Refresh();
        Show();
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Shows which part is on the pointer: the row wears it, and a line says what to do next.</summary>
    private void MarkChosen()
    {
        string? chosen = Sheet?.ChosenPart;

        foreach (var (libId, row) in _byLibId)
        {
            row.Classes.Set("selected", string.Equals(libId, chosen, StringComparison.Ordinal));
        }

        _armed.IsVisible = chosen is not null;
        if (chosen is not null)
        {
            _armed.Text = Tr.T("sch.symbols.armed", chosen[(chosen.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        }
    }

    private Button Row(SymbolChoice choice)
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

        var place = new MenuItem { Header = Tr.T("sch.symbols.place") };
        place.Click += (_, _) => Sheet?.ChoosePart(choice.LibId, choice.Symbol);
        button.ContextMenu = new ContextMenu { ItemsSource = new[] { place } };

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
