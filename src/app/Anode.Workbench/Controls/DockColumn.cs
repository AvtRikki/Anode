using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Anode.Sdk;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Controls;

/// <summary>Vertical column of dock stacks separated by lines; collapsed stacks shrink to their header.</summary>
public sealed class DockColumn : Grid
{
    public static readonly StyledProperty<IList<DockStackViewModel>?> StacksProperty =
        AvaloniaProperty.Register<DockColumn, IList<DockStackViewModel>?>(nameof(Stacks));

    private readonly List<DockStackViewModel> _observed = [];

    public IList<DockStackViewModel>? Stacks
    {
        get => GetValue(StacksProperty);
        set => SetValue(StacksProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StacksProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnCollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnCollectionChanged;
            }

            Rebuild();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        foreach (var stack in _observed)
        {
            stack.PropertyChanged -= OnStackChanged;
        }

        _observed.Clear();
        Children.Clear();
        RowDefinitions.Clear();

        if (Stacks is null)
        {
            return;
        }

        int row = 0;
        for (int i = 0; i < Stacks.Count; i++)
        {
            var stack = Stacks[i];
            _observed.Add(stack);
            stack.PropertyChanged += OnStackChanged;

            if (i > 0)
            {
                RowDefinitions.Add(new RowDefinition(1, GridUnitType.Pixel));
                var line = new Border { Classes = { "line" } };
                SetRow(line, row++);
                Children.Add(line);
            }

            RowDefinitions.Add(new RowDefinition(stack.IsCollapsed ? GridLength.Auto : GridLength.Star));
            var view = new DockStackView { DataContext = stack, VerticalAlignment = VerticalAlignment.Stretch };
            SetRow(view, row++);
            Children.Add(view);
        }
    }

    private void OnStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DockStackViewModel.IsCollapsed) || sender is not DockStackViewModel stack)
        {
            return;
        }

        int index = _observed.IndexOf(stack);
        int row = index * 2;
        if (row < RowDefinitions.Count)
        {
            RowDefinitions[row].Height = stack.IsCollapsed ? GridLength.Auto : GridLength.Star;
        }
    }
}

/// <summary>Palette row title with the matched characters coloured.</summary>
public sealed class HighlightedText : TextBlock
{
    public static readonly StyledProperty<Services.CommandMatch?> MatchProperty =
        AvaloniaProperty.Register<HighlightedText, Services.CommandMatch?>(nameof(Match));

    public Services.CommandMatch? Match
    {
        get => GetValue(MatchProperty);
        set => SetValue(MatchProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MatchProperty || change.Property == ForegroundProperty)
        {
            Render();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Render();
    }

    private void Render()
    {
        if (Match is not { } match)
        {
            return;
        }

        var highlight = this.TryFindResource(ThemeKeys.AccentText, ActualThemeVariant, out var brush) && brush is Avalonia.Media.IBrush b
            ? b
            : Avalonia.Media.Brushes.SteelBlue;
        Ui.SetHighlighted(this, match.Command.Title, match.Ranges, highlight);
    }
}
