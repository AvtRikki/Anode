using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The symbols of the library on screen, to choose which one is on the canvas — what KiCad's symbol editor keeps in
/// its tree. A filter narrows it by name, description and keywords; a derived symbol says what it is derived from.
/// </summary>
internal sealed class SymbolListPanel : ContentControl
{
    public const string PanelId = "sch.library";

    private readonly IWorkbench _workbench;
    private readonly TextBox _filter;
    private readonly TextBox _newName;
    private readonly Button _add;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _summary;
    private readonly Control _body;
    private SymbolLibraryDocument? _watched;
    private string _shown = "\u0000";

    public SymbolListPanel(IWorkbench workbench)
    {
        _workbench = workbench;

        _filter = new TextBox { Classes = { "filter" } };
        _filter.TextChanged += (_, _) => Fill();

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;
        _summary.Margin = new Thickness(0, 6, 0, 0);

        // A new symbol: the + opens a box for its name, Enter makes it, Esc thinks better of it.
        _newName = new TextBox { Classes = { "filter" }, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _newName.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CreateSymbol();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _newName.IsVisible = false;
                e.Handled = true;
            }
        };

        _add = Ui.TagButton("+", "neutral", () => ShowNewName());
        _add.Margin = new Thickness(6, 0, 0, 0);

        var search = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_add, Dock.Right);
        search.Children.Add(_add);
        search.Children.Add(_filter);

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(search);
        head.Children.Add(_newName);
        head.Children.Add(_summary);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(new ScrollViewer { Content = _rows });
        _body = body;

        workbench.ActiveDocumentChanged += Render;
        NewSymbolRequested += ShowNewName;
        Tr.Changed += Retranslate;
        Retranslate();
    }

    /// <summary>Asked for by the New Symbol command: the panel opens its name box, wherever it is docked.</summary>
    private static event Action? NewSymbolRequested;

    public static void RequestNewSymbol() => NewSymbolRequested?.Invoke();

    private SymbolLibraryDocument? Document => _workbench.ActiveDocument as SymbolLibraryDocument;

    private void ShowNewName()
    {
        if (Document is null)
        {
            return;
        }

        _newName.Text = string.Empty;
        _newName.IsVisible = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_newName.IsAttachedToVisualTree())
                {
                    _newName.Focus();
                }
            },
            DispatcherPriority.Loaded);
    }

    private void CreateSymbol()
    {
        if (Document is not { } document)
        {
            return;
        }

        if (document.AddSymbol(_newName.Text ?? string.Empty) is { } problem)
        {
            _workbench.ShowBanner(new Banner(problem));
            return;
        }

        _newName.IsVisible = false;
        _filter.Text = string.Empty;
    }

    private void Retranslate()
    {
        _filter.PlaceholderText = Tr.T("sch.lib.filter");
        _newName.PlaceholderText = Tr.T("sch.lib.newName");
        ToolTip.SetTip(_add, Tr.T("sch.command.newSymbol"));
        _shown = "\u0000";
        Render();
    }

    private void Render()
    {
        if (!ReferenceEquals(_watched, Document))
        {
            if (_watched is { } old)
            {
                old.PropertyChanged -= OnDocumentChanged;
            }

            _watched = Document;
            if (_watched is { } now)
            {
                now.PropertyChanged += OnDocumentChanged;
            }

            _shown = "\u0000";
        }

        if (Document is null)
        {
            Content = Ui.Text(Tr.T("sch.lib.noLibrary"), "dim");
            return;
        }

        Content = _body;
        Fill();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SymbolLibraryDocument.Current) or nameof(SymbolLibraryDocument.Library))
        {
            Fill();
        }
    }

    private void Fill()
    {
        if (Document is not { } document)
        {
            return;
        }

        string filter = _filter.Text?.Trim() ?? string.Empty;
        var symbols = document.Library.Symbols.Where(s => filter.Length == 0 || Matches(s, filter)).ToList();

        string state = document.Current?.Name + "\u001e" + string.Join('\u001f', symbols.Select(s => s.Name));
        if (state == _shown)
        {
            return;
        }

        _shown = state;
        _rows.Children.Clear();
        foreach (var symbol in symbols)
        {
            _rows.Children.Add(Row(document, symbol, ReferenceEquals(symbol, document.Current)));
        }

        _summary.Text = filter.Length == 0
            ? Tr.T("sch.lib.count", document.Library.Symbols.Count)
            : Tr.T("sch.lib.found", symbols.Count, document.Library.Symbols.Count);
    }

    private static bool Matches(LibSymbol symbol, string filter) =>
        symbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || symbol.Fields.Any(f => f.Name is "Description" or "ki_keywords" && f.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static Control Row(SymbolLibraryDocument document, LibSymbol symbol, bool current)
    {
        var line = new StackPanel { Spacing = 1 };

        var name = Ui.Mono(symbol.Name, current ? "accentText" : string.Empty);
        name.FontSize = 11.5;
        name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        line.Children.Add(name);

        string? description = symbol.Fields.FirstOrDefault(f => f.Name == "Description")?.Value;
        string under = symbol.Extends is { Length: > 0 } parent ? Tr.T("sch.lib.derived", parent) : description ?? string.Empty;
        if (under.Length > 0)
        {
            var note = Ui.Text(under, "faint");
            note.FontSize = 10.5;
            note.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            line.Children.Add(note);
        }

        ToolTip.SetTip(line, description);
        var row = new Button { Classes = { "row" }, Padding = new Thickness(4, 3), Content = line, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.Click += (_, _) => document.Show(symbol.Name);
        return row;
    }
}
