using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// A library's tab: a bar naming the symbol on the canvas, with its units and body styles to switch between, over
/// the canvas it is drawn on. The switches are only there for a symbol that has something to switch.
/// </summary>
internal sealed class SymbolLibraryView : DockPanel
{
    private readonly SymbolLibraryDocument _document;
    private readonly TextBlock _name;
    private readonly TextBlock _description;
    private readonly ComboBox _unit;
    private readonly CheckBox _deMorgan;
    private readonly ContentControl _body = new();
    private readonly Control _canvas;
    private bool _filling;

    public SymbolLibraryView(SymbolLibraryDocument document, Control canvas)
    {
        _document = document;
        _canvas = canvas;
        this.WithResource(BackgroundProperty, ThemeKeys.ChromeBg);

        _name = Ui.Text(string.Empty, "title");
        _name.VerticalAlignment = VerticalAlignment.Center;

        _description = Ui.Text(string.Empty, "dim");
        _description.FontSize = 11.5;
        _description.VerticalAlignment = VerticalAlignment.Center;
        _description.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;

        _unit = new ComboBox { Classes = { "choice" }, MinWidth = 110 };
        _unit.SelectionChanged += (_, _) =>
        {
            if (!_filling && _unit.SelectedIndex >= 0)
            {
                _document.ShowUnit(_unit.SelectedIndex + 1);
            }
        };

        _deMorgan = new CheckBox { MinHeight = 0, Padding = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _deMorgan.IsCheckedChanged += (_, _) =>
        {
            if (!_filling)
            {
                _document.ShowBodyStyle(_deMorgan.IsChecked == true ? 2 : 1);
            }
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(16, 10, 16, 8) };
        bar.Children.Add(_name);
        bar.Children.Add(_unit);
        bar.Children.Add(_deMorgan);
        bar.Children.Add(_description);

        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(_body);

        document.PropertyChanged += OnDocumentChanged;
        Tr.Changed += Fill;
        Fill();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SymbolLibraryDocument.Current) or nameof(SymbolLibraryDocument.Unit) or nameof(SymbolLibraryDocument.BodyStyle))
        {
            Fill();
        }
    }

    private void Fill()
    {
        _filling = true;
        try
        {
            if (_document.Current is not { } symbol || _document.Body is not { } body)
            {
                _name.Text = string.Empty;
                _description.Text = string.Empty;
                _unit.IsVisible = _deMorgan.IsVisible = false;
                _body.Content = Ui.Text(Tr.T("sch.lib.empty"), "dim");
                return;
            }

            _body.Content = _canvas;
            _name.Text = symbol.Name;
            _description.Text = symbol.Fields.FirstOrDefault(f => f.Name == "Description")?.Value ?? string.Empty;

            _unit.IsVisible = body.UnitCount > 1;
            _unit.ItemsSource = Enumerable.Range(1, body.UnitCount).Select(u => Tr.T("sch.lib.unit", SymbolLibraryDocument.UnitName(u))).ToList();
            _unit.SelectedIndex = _document.Unit - 1;

            _deMorgan.IsVisible = body.HasAlternateBody;
            _deMorgan.Content = Ui.Text(Tr.T("sch.lib.deMorgan"), "value");
            _deMorgan.IsChecked = _document.BodyStyle == 2;
        }
        finally
        {
            _filling = false;
        }
    }
}
