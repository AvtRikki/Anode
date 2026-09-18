using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
    /// <summary>How far the pointer must travel before a press becomes a drag rather than a click.</summary>
    private const double DragThreshold = 4;


    private readonly IWorkbench _workbench;
    private readonly SymbolLibraryList _remembered;
    private readonly DisabledSources _disabled;
    private readonly TextBox _filter;
    private readonly TextBlock _summary;
    private readonly Button _sources;
    private readonly TextBlock _armed;
    private readonly ListBox _list;
    private readonly StackPanel _nothing = new() { Spacing = 4, Margin = new Thickness(11, 4) };
    private bool _syncing;
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

        // A ListBox builds only the rows in view, which is what makes a preview per row affordable and lets the
        // list be as long as the libraries are.
        _list = new ListBox { Classes = { "parts" }, ItemTemplate = RowTemplate() };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _list.SelectedItem is SymbolChoice choice)
            {
                Sheet?.ChoosePart(choice.LibId, choice.Symbol);
            }
        };

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
                new Panel { Children = { _list, _nothing } },
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
            _list.ItemsSource = null;
            _nothing.Children.Clear();
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
        _nothing.Children.Clear();
        if (_chooser is not { } chooser)
        {
            _list.ItemsSource = null;
            return;
        }

        if (chooser.Results.Count == 0)
        {
            _list.ItemsSource = null;
            _list.IsVisible = false;
            _nothing.IsVisible = true;

            bool searching = chooser.Query.Length > 0;
            _nothing.Children.Add(Ui.Text(Tr.T(searching ? "sch.symbols.empty" : "sch.symbols.none"), "dim"));

            if (!searching)
            {
                var hint = Ui.Text(Tr.T("sch.symbols.hint"), "faint");
                hint.FontSize = 11.5;
                hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                hint.TextTrimming = Avalonia.Media.TextTrimming.None;
                _nothing.Children.Add(hint);
            }

            _summary.Text = string.Empty;
            return;
        }

        _nothing.IsVisible = false;
        _list.IsVisible = true;
        _list.ItemsSource = chooser.Results;

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

    /// <summary>Shows which part is on the pointer: the row is selected, and a line says what to do next.</summary>
    private void MarkChosen()
    {
        string? chosen = Sheet?.ChosenPart;

        // Set under a guard: selecting a row is also how a part is armed, and this must not arm it again.
        _syncing = true;
        try
        {
            _list.SelectedItem = chosen is null
                ? null
                : (_chooser?.Results.FirstOrDefault(r => string.Equals(r.LibId, chosen, StringComparison.Ordinal)));
        }
        finally
        {
            _syncing = false;
        }

        _armed.IsVisible = chosen is not null;
        if (chosen is not null)
        {
            _armed.Text = Tr.T("sch.symbols.armed", chosen[(chosen.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        }
    }

    /// <summary>
    /// One part: its shape, its name, and where it came from. The preview is what makes the list scannable — a part
    /// is recognised by its silhouette long before its name is read.
    /// </summary>
    private IDataTemplate RowTemplate() => new FuncDataTemplate<SymbolChoice>(
        (choice, _) =>
        {
            if (choice is null)
            {
                return null;
            }

            var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Ui.Text(choice.Name, "strong"));
            lines.Children.Add(Ui.Mono(choice.Library, "dim"));

            if (choice.Description is { Length: > 0 } description)
            {
                var text = Ui.Text(description, "faint");
                text.FontSize = 11.5;
                lines.Children.Add(text);
            }

            var preview = new SymbolPreview
            {
                Symbol = choice.Symbol,
                Width = 38,
                Height = 38,
                VerticalAlignment = VerticalAlignment.Center,
            }.WithResource(SymbolPreview.StrokeProperty, ThemeKeys.ChromeText);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { preview, lines },
            };

            var place = new MenuItem { Header = Tr.T("sch.symbols.place") };
            place.Click += (_, _) => Sheet?.ChoosePart(choice.LibId, choice.Symbol);

            var item = new Border
            {
                Background = Brushes.Transparent,
                Child = row,
                ContextMenu = new ContextMenu { ItemsSource = new[] { place } },
                [ToolTip.TipProperty] = choice.LibId,
            };

            // Dragged onto the sheet, a part lands where it was let go; clicked, it goes on the pointer instead.
            // The drag begins only once the pointer has travelled, or every click would open a drag session.
            Point from = default;
            PointerPressedEventArgs? began = null;

            item.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
                {
                    from = e.GetPosition(item);
                    began = e;
                }
            };

            item.PointerReleased += (_, _) => began = null;

            item.PointerMoved += (_, e) =>
            {
                if (began is not { } press || !e.GetCurrentPoint(item).Properties.IsLeftButtonPressed)
                {
                    began = null;
                    return;
                }

                var now = e.GetPosition(item);
                if (Math.Abs(now.X - from.X) < DragThreshold && Math.Abs(now.Y - from.Y) < DragThreshold)
                {
                    return;
                }

                began = null;

                // The text is for the platform, which will not open a drag session carrying nothing it can
                // represent — on macOS that raises inside AppKit and takes the process with it. The in-process
                // format beside it is what this application actually reads.
                var carried = DataTransferItem.CreateText(choice.LibId);
                carried.Set(SymbolDrag.Format, choice);

                var transfer = new DataTransfer();
                transfer.Add(carried);
                _ = DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
            };

            return item;
        },
        supportsRecycling: true);

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
