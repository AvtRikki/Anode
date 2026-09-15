using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Ecad.Rendering;

namespace Ecad.App.ViewModels;

public partial class LayerItemViewModel : ObservableObject
{
    private readonly LayerGeometry _layer;
    private readonly Action _changed;

    public LayerItemViewModel(LayerGeometry layer, string displayName, Action changed)
    {
        _layer = layer;
        _changed = changed;
        DisplayName = displayName;
        IsVisible = layer.IsVisible;
        var c = layer.Color;
        Brush = new SolidColorBrush(Color.FromArgb(255, c.R, c.G, c.B));
    }

    public string Name => _layer.Name;

    public string DisplayName { get; }

    public IBrush Brush { get; }

    public string PrimitiveCount => _layer.PrimitiveCount.ToString("N0");

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    partial void OnIsVisibleChanged(bool value)
    {
        _layer.IsVisible = value;
        _changed();
    }
}

public sealed record PropertyRow(string Name, string Value);
