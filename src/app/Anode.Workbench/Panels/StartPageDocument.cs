using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Panels;

/// <summary>Start screen (mockup 1e): not a "create project" dialog but a page — continue work, what is waiting.</summary>
public sealed class StartPageDocument(ShellViewModel shell) : DocumentBase
{
    public override string Title => Tr.T("shell.start.title");

    protected override Control CreateView() => new StartPageView(shell, this);

    internal void RaiseTitle() => OnPropertyChanged(nameof(Title));
}

internal sealed class StartPageView : ContentControl
{
    private readonly ShellViewModel _shell;

    public StartPageView(ShellViewModel shell, StartPageDocument document)
    {
        _shell = shell;
        this.WithResource(BackgroundProperty, ThemeKeys.ChromeBg);
        shell.RecentProjects.CollectionChanged += (_, _) => Render();
        Tr.Changed += () =>
        {
            document.RaiseTitle();
            Render();
        };

        Render();
    }

    private void Render()
    {
        var page = new StackPanel { Spacing = 22, MaxWidth = 640, Margin = new Thickness(28, 26), HorizontalAlignment = HorizontalAlignment.Left };

        var header = new DockPanel();
        var brand = new TextBlock { FontSize = 26, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
        brand.Inlines = [new Avalonia.Controls.Documents.Run("An"), Accent("o"), new Avalonia.Controls.Documents.Run("de")];
        header.Children.Add(brand);
        var date = Ui.Mono($"0.1 · {DateTime.Now.ToString("dddd, d MMMM", Tr.Culture)}", "dim");
        date.Margin = new Thickness(12, 0, 0, 4);
        date.VerticalAlignment = VerticalAlignment.Bottom;
        header.Children.Add(date);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Ui.TagButton(Tr.T("shell.start.open"), "accent", () => _shell.Commands.TryExecute("file.open")));
        header.Children.Add(actions);
        page.Children.Add(header);

        var recent = new StackPanel { Spacing = 1 };
        recent.Children.Add(WithMargin(Ui.Overline(Tr.T("shell.start.continue")), 0, 0, 0, 8));
        if (_shell.RecentProjects.Count == 0)
        {
            recent.Children.Add(Ui.Text(Tr.T("shell.start.continueEmpty"), "dim"));
        }

        var now = DateTime.Now;
        foreach (var project in _shell.RecentProjects.Take(6))
        {
            var row = new Button { Classes = { "row" }, Padding = new Thickness(10, 9) };
            var line = new DockPanel();
            var thumb = Thumbnail();
            thumb.Margin = new Thickness(0, 0, 12, 0);
            line.Children.Add(thumb);
            var text = new StackPanel();
            text.Children.Add(Ui.Text(project.Name, "strong"));
            text.Children.Add(Ui.Mono($"{Path.GetFileName(project.Path)} · {RecentProjectsStore.Ago(project.OpenedAt, now)}", "dim"));
            line.Children.Add(text);
            row.Content = line;
            row.IsEnabled = File.Exists(project.Path);
            string path = project.Path;
            row.Click += (_, _) => _ = _shell.OpenAsync(path);
            recent.Children.Add(row);
        }

        page.Children.Add(recent);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        var waiting = new StackPanel { Spacing = 7 };
        waiting.Children.Add(WithMargin(Ui.Overline(Tr.T("shell.start.waiting")), 0, 0, 0, 2));
        var waitingText = Ui.Text(Tr.T("shell.start.waitingEmpty"), "dim");
        waitingText.TextWrapping = TextWrapping.Wrap;
        waiting.Children.Add(waitingText);
        columns.Children.Add(waiting);

        var start = new StackPanel { Spacing = 7 };
        start.Children.Add(WithMargin(Ui.Overline(Tr.T("shell.start.startWith")), 0, 0, 0, 2));
        var tags = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        tags.Children.Add(Ui.TagButton(Tr.T("shell.start.tagBoard"), "neutral", () => _shell.Commands.TryExecute("file.open")));
        tags.Children.Add(Ui.TagButton(Tr.T("shell.start.tagPalette"), "neutral", _shell.OpenPalette));
        start.Children.Add(tags);
        Grid.SetColumn(start, 1);
        columns.Children.Add(start);
        page.Children.Add(columns);

        var search = new Button { Classes = { "searchField" }, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(11, 7) };
        var searchLine = new DockPanel();
        var hint = Ui.Mono("⌘K");
        DockPanel.SetDock(hint, Dock.Right);
        searchLine.Children.Add(hint);
        if (Icons.Draw(Icons.Search, 13) is { } searchIcon)
        {
            DockPanel.SetDock(searchIcon, Dock.Left);
            searchIcon.Margin = new Thickness(0, 0, 8, 0);
            searchLine.Children.Add(searchIcon);
        }

        searchLine.Children.Add(Ui.Text(Tr.T("shell.search.placeholder")));
        search.Content = searchLine;
        search.Click += (_, _) => _shell.OpenPalette();
        page.Children.Add(search);

        Content = new ScrollViewer { Content = page };
    }

    private static Avalonia.Controls.Documents.Run Accent(string text)
    {
        var run = new Avalonia.Controls.Documents.Run(text);
        run.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Application.Current!.GetResourceObservable(ThemeKeys.Accent));
        return run;
    }

    private static Border Thumbnail()
    {
        var canvas = new Canvas { Width = 34, Height = 26 };
        canvas.Children.Add(Bar(4, 8, 16, "Ink.CopperFront"));
        canvas.Children.Add(Bar(10, 15, 18, "Ink.CopperBack"));
        return new Border { Width = 34, Height = 26, BorderThickness = new Thickness(1), Child = canvas }
            .WithResource(Border.BackgroundProperty, ThemeKeys.InkSheet)
            .WithResource(Border.BorderBrushProperty, ThemeKeys.ChromeLine);

        static Border Bar(double x, double y, double width, string ink)
        {
            var bar = new Border { Width = width, Height = 3 }.WithResource(Border.BackgroundProperty, ink);
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            return bar;
        }
    }

    private static T WithMargin<T>(T control, double l, double t, double r, double b)
        where T : Control
    {
        control.Margin = new Thickness(l, t, r, b);
        return control;
    }
}
