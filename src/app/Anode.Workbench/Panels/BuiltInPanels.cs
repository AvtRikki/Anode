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

    public InspectorPanel(IWorkbench workbench)
    {
        _workbench = workbench;
        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        Tr.Changed += Render;
        Render();
    }

    private void Render()
    {
        if (_workbench.ActiveDocument?.Selection is not { } selection)
        {
            Content = Ui.Text(Tr.T("shell.inspector.empty"), "dim");
            return;
        }

        var root = new StackPanel { Spacing = 12 };
        var header = new DockPanel { LastChildFill = false };
        var title = Ui.Text(selection.Title, "title");
        title.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(title);
        if (selection.Subtitle is { } subtitle)
        {
            var sub = Ui.Text(subtitle, "dim");
            sub.FontSize = 12;
            header.Children.Add(sub);
        }

        if (selection.Tag is { } tag)
        {
            var tagView = Ui.Tag(tag, "accent");
            DockPanel.SetDock(tagView, Dock.Right);
            header.Children.Add(tagView);
        }

        root.Children.Add(header);
        root.Children.Add(Ui.PropertyGrid(selection.Properties));
        Content = new ScrollViewer { Content = root };
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
