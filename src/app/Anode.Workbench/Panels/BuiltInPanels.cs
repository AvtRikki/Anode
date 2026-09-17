using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Panels;

/// <summary>Selection inspector: the content follows the selection, the place does not.</summary>
public sealed class InspectorPanel : ContentControl
{
    private readonly IWorkbench _workbench;

    /// <summary>The boxes of the current selection, in the order they are drawn: E puts the caret in the first.</summary>
    private readonly List<TextBox> _editable = [];

    public InspectorPanel(IWorkbench workbench)
    {
        _workbench = workbench;
        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        Tr.Changed += Render;
        InspectorFocus.Requested += FocusFirstEditable;
        Render();
    }

    private void FocusFirstEditable()
    {
        if (_editable.FirstOrDefault() is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void Render()
    {
        _editable.Clear();
        if (_workbench.ActiveDocument?.Selection is not { } selection)
        {
            Content = Ui.Text(Tr.T("shell.inspector.empty"), "dim");
            return;
        }

        var root = new StackPanel();
        root.Children.Add(Header(selection));

        if (selection.Blocks.Count > 0)
        {
            bool first = true;
            foreach (var block in selection.Blocks)
            {
                root.Children.Add(Block(block, first));
                first = false;
            }
        }
        else
        {
            // A document that has not been taught the blocks yet still shows what it knows.
            root.Children.Add(new Border { Padding = new Thickness(11, 10), Child = Ui.PropertyGrid(selection.Properties) });
        }

        if (selection.Actions.Count > 0)
        {
            root.Children.Add(Footer(selection.Actions));
        }

        Content = new ScrollViewer { Content = root };
    }

    /// <summary>Three lines on the raised fill: what it is called, what kind it is, and where it lives.</summary>
    private static Control Header(SelectionInfo selection)
    {
        var lines = new StackPanel { Spacing = 5 };

        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(Ui.Text(selection.Title, "title"));
        if (selection.Tag is { } tag)
        {
            name.Children.Add(new Border
            {
                Padding = new Thickness(6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Mono(tag, "dim"),
            }.WithResource(Border.BackgroundProperty, ThemeKeys.ChromeBg));
        }

        lines.Children.Add(name);

        if (selection.Subtitle is { } subtitle)
        {
            var where = Ui.Text(subtitle, "dim");
            where.FontSize = 11.5;
            lines.Children.Add(where);
        }

        return new Border { Padding = new Thickness(11, 9), Child = lines }
            .WithResource(Border.BackgroundProperty, ThemeKeys.ChromeRaised);
    }

    /// <summary>One block. A line divides it from the one above — never a frame, and never a card.</summary>
    private Control Block(InspectorBlock block, bool first)
    {
        var rows = new StackPanel { Spacing = 7 };

        // Every block names itself in the quiet ink; only a block that reports a broken rule takes the second accent.
        var title = Ui.Overline(block.Title, block.IsAlert);
        if (!block.IsAlert)
        {
            title.Classes.Add("quiet");
        }

        rows.Children.Add(title);

        if (block.IsAlert)
        {
            foreach (var row in block.Rows)
            {
                rows.Children.Add(Complaint(row));
            }
        }
        else if (block.IsConnections)
        {
            var list = new StackPanel { Spacing = 3 };
            foreach (var row in block.Rows)
            {
                list.Children.Add(Ui.ConnectionRow(row.Name, row.Value, row.Trailing, row.IsUnresolved));
            }

            rows.Children.Add(list);
        }
        else
        {
            rows.Children.Add(Values(block.Rows));
        }

        var border = new Border
        {
            Padding = new Thickness(11, 10),
            BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            Child = rows,
        };

        return first ? border : border.WithResource(Border.BorderBrushProperty, ThemeKeys.ChromeLine);
    }

    /// <summary>Names in a fixed column, values beside them; a value that can be written wears the field fill.</summary>
    private Grid Values(IReadOnlyList<InspectorRow> rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), RowSpacing = 5, ColumnSpacing = 10 };
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = Ui.Text(rows[i].Name, "dim");
            label.FontSize = 12.5;
            Grid.SetRow(label, i);
            grid.Children.Add(label);

            Control value;
            if (rows[i].Commit is { } commit)
            {
                var box = Ui.EditableField(rows[i].Value, commit);
                _editable.Add(box);
                value = box;
            }
            else
            {
                value = Ui.Mono(rows[i].Value, rows[i].IsUnresolved ? "accentText" : "value");
            }

            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        return grid;
    }

    /// <summary>A broken rule, in the same words the bottom dock uses.</summary>
    private static Control Complaint(InspectorRow row)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            Margin = new Thickness(0, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Classes = { "issueMarker", "error" },
        });

        var text = Ui.Text(row.Value);
        text.FontSize = 12.5;
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        line.Children.Add(text);
        return line;
    }

    /// <summary>What can be done to the object. Everything modal leaves the panel through here.</summary>
    private static Control Footer(IReadOnlyList<InspectorAction> actions)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var action in actions)
        {
            row.Children.Add(Ui.TagButton(action.Label, action.IsPrimary ? "accent" : "outline", action.Run));
        }

        return new Border
        {
            Padding = new Thickness(11, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row,
        }.WithResource(Border.BorderBrushProperty, ThemeKeys.ChromeLine);
    }
}

/// <summary>Bottom dock: check results, each with a coordinate and an action.</summary>
public sealed class IssuesPanel : ContentControl
{
    private readonly IWorkbench _workbench;

    public IssuesPanel(IWorkbench workbench)
    {
        _workbench = workbench;
        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        Tr.Changed += Render;
        Render();
    }

    private void Render()
    {
        var issues = _workbench.ActiveDocument?.Issues ?? [];
        if (issues.Count == 0)
        {
            Content = Ui.Text(Tr.T(_workbench.ActiveDocument is null ? "shell.issues.noDocument" : "shell.issues.none"), "dim");
            return;
        }

        var rows = new StackPanel();
        foreach (var issue in issues)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,200,*,150,96"), MinHeight = 28 };
            AddCell(grid, Ui.IssueMarker(issue.Severity), 0);
            AddCell(grid, Ui.Text(issue.Title), 1);
            AddCell(grid, Ui.Text(issue.Detail), 2);
            AddCell(grid, Ui.Mono(issue.Location, "dim"), 3);
            if (issue.Action is { } action)
            {
                var button = Ui.TagButton(issue.ActionLabel, "outline", action);
                button.HorizontalAlignment = HorizontalAlignment.Right;
                AddCell(grid, button, 4);
            }

            rows.Children.Add(new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid }.WithResource(Border.BorderBrushProperty, ThemeKeys.ChromeLine));
        }

        Content = new ScrollViewer { Content = rows };

        static void AddCell(Grid grid, Control cell, int column)
        {
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
    }
}

/// <summary>Bottom dock: the workbench log.</summary>
public sealed class ConsolePanel : ContentControl
{
    private readonly StackPanel _lines = new();
    private readonly ScrollViewer _scroll;
    private readonly LogService _log;

    public ConsolePanel(LogService log)
    {
        _log = log;
        _scroll = new ScrollViewer { Content = _lines };
        Content = _scroll;
        Rebuild();

        log.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (LogEntry entry in e.NewItems)
                {
                    Append(entry);
                }

                Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
            }
        };

        // Level names are translated; the messages keep the language they were written in.
        Tr.Changed += Rebuild;
    }

    private void Rebuild()
    {
        _lines.Children.Clear();
        foreach (var entry in _log.Entries)
        {
            Append(entry);
        }
    }

    private void Append(LogEntry entry)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 1) };
        line.Children.Add(Ui.Mono(entry.Time.ToString("HH:mm:ss"), "faint"));
        var level = Ui.Mono(
            Tr.T(entry.Level switch { LogLevel.Error => "shell.console.error", LogLevel.Warning => "shell.console.warning", _ => "shell.console.info" }),
            entry.Level == LogLevel.Error ? "alertText" : "dim");
        level.Width = 64;
        line.Children.Add(level);
        line.Children.Add(Ui.Mono(entry.Message));
        _lines.Children.Add(line);
    }
}
