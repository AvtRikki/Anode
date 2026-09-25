using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The bill on screen: a bar with the preset, a filter, the switch for every field and the export, over a table of
/// its lines. The table is rebuilt only when what it would show differs from what it shows, and the cell the caret
/// was in gets the caret back, so writing a value and moving on with Tab is not thrown by the redraw it causes.
/// </summary>
internal sealed class BomView : DockPanel
{
    private const double ReferenceWidth = 140;
    private const double NarrowWidth = 48;
    private const double FlagWidth = 96;
    private const double FieldWidth = 150;
    private const double WideWidth = 220;

    private readonly BomDocument _document;
    private readonly ComboBox _preset;
    private readonly TextBox _filter;
    private readonly CheckBox _all;
    private readonly Button _export;
    private readonly TextBlock _summary;
    private readonly ScrollViewer _table = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(16, 8, 16, 16),
    };

    private readonly Dictionary<(string Line, string Column), TextBox> _cells = [];
    private string _shown = "\u0000";
    private bool _choosing;

    public BomView(BomDocument document)
    {
        _document = document;
        this.WithResource(BackgroundProperty, ThemeKeys.ChromeBg);

        _preset = new ComboBox { Classes = { "choice" }, MinWidth = 200 };
        _preset.SelectionChanged += (_, _) =>
        {
            if (!_choosing && _preset.SelectedIndex >= 0 && _preset.SelectedIndex < _document.Presets.Count)
            {
                _document.Choose(_document.Presets[_preset.SelectedIndex]);
            }
        };

        _filter = new TextBox { Classes = { "filter" }, Width = 200 };
        _filter.TextChanged += (_, _) => _document.SetFilter(_filter.Text ?? string.Empty);

        _all = new CheckBox { MinHeight = 0, Padding = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _all.IsCheckedChanged += (_, _) => _document.ShowAllFields(_all.IsChecked == true);

        _export = Ui.TagButton(string.Empty, "neutral", () => _ = _document.ExportAsync());

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;
        _summary.VerticalAlignment = VerticalAlignment.Center;

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(16, 12, 16, 4) };
        bar.Children.Add(_preset);
        bar.Children.Add(_filter);
        bar.Children.Add(_all);
        bar.Children.Add(_export);
        bar.Children.Add(_summary);

        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(_table);

        document.PropertyChanged += OnDocumentChanged;
        Tr.Changed += Retranslate;
        Retranslate();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BomDocument.Table))
        {
            Fill();
        }
    }

    private void Retranslate()
    {
        _filter.PlaceholderText = Tr.T("sch.bom.filter");
        _all.Content = Ui.Text(Tr.T("sch.bom.allFields"), "value");
        _export.Content = Tr.T("sch.bom.export");
        _shown = "\u0000";
        Fill();
    }

    private void Fill()
    {
        var table = _document.Table;

        _choosing = true;
        var presets = _document.Presets;
        _preset.ItemsSource = presets.Select((p, i) => i > 0 ? p.Name
            : p.Name.Length > 0 ? Tr.T("sch.bom.projectPreset", p.Name) : Tr.T("sch.bom.unnamedPreset")).ToList();
        _preset.SelectedIndex = Math.Max(0, presets.ToList().FindIndex(p => ReferenceEquals(p, _document.Preset)));
        _choosing = false;

        _summary.Text = _document.Summary;

        var columns = table.Columns.Where(c => c.Show || (_document.AllFields && !BomPreset.IsGenerated(c.Name))).ToList();
        string state = Signature(columns, table);
        if (state == _shown)
        {
            return;
        }

        // The caret is in a cell that is about to be thrown away: remember which, and give it back afterwards.
        var focused = _cells.FirstOrDefault(c => c.Value.IsFocused).Key;

        _shown = state;
        _cells.Clear();
        _table.Content = table.Rows.Count == 0
            ? Ui.Text(Tr.T("sch.bom.empty"), "dim")
            : Lines(columns, table.Rows);

        if (focused != default && _cells.TryGetValue(focused, out var again))
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (again.IsAttachedToVisualTree())
                    {
                        again.Focus();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private static string Signature(IReadOnlyList<BomField> columns, BomTable table)
    {
        var text = new StringBuilder(string.Join('\u001f', columns.Select(c => c.Name + "=" + c.Label)));
        foreach (var row in table.Rows)
        {
            text.Append('\u001e');
            foreach (var column in columns)
            {
                text.Append(BomTable.Text(row, column.Name, forExport: false) ?? "\u0001").Append('\u001f');
            }
        }

        return text.ToString();
    }

    private Grid Lines(IReadOnlyList<BomField> columns, IReadOnlyList<BomRow> rows)
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 2 };
        foreach (var column in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(ColumnWidth(column.Name), GridUnitType.Pixel));
        }

        for (int r = 0; r <= rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int c = 0; c < columns.Count; c++)
        {
            var title = Ui.Text(columns[c].Label, "dim");
            title.FontSize = 11.5;
            title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(title, columns[c].Label);
            Place(grid, title, 0, c);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                Place(grid, Cell(rows[r], columns[c].Name), r + 1, c);
            }
        }

        return grid;
    }

    private static double ColumnWidth(string column) => column switch
    {
        "Reference" => ReferenceWidth,
        BomPreset.Quantity or BomPreset.ItemNumber => NarrowWidth,
        _ when BomPreset.IsAttribute(column) => FlagWidth,
        "Datasheet" or "Footprint" => WideWidth,
        _ => FieldWidth,
    };

    private Control Cell(BomRow row, string column)
    {
        string? value = BomTable.Text(row, column, forExport: false);

        if (column == "Reference")
        {
            var text = Ui.Mono(value ?? string.Empty);
            text.FontSize = 11.5;
            text.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(text, value);
            var button = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = text };
            button.Click += (_, _) => _ = _document.ShowAsync(row);
            return button;
        }

        // A flag is a tick, as in KiCad's table: set for every part, for none, or — for a line whose parts disagree —
        // neither, until it is clicked and so decided for all of them.
        if (BomPreset.IsAttribute(column))
        {
            var states = row.Parts.Select(p => SchFields.IsOn(p.Symbol, column)).Distinct().ToList();
            bool? on = states.Count == 1 ? states[0] : null;
            var tick = new CheckBox { IsChecked = on, IsThreeState = false, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
            if (on is null)
            {
                ToolTip.SetTip(tick, Tr.T("sch.bom.mixed"));
            }

            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked is { } wanted && wanted != on)
                {
                    _ = _document.SetFlagAsync(row, column, wanted);
                }
            };
            return tick;
        }

        // Counts are worked out, not written: they read as quiet text.
        if (BomPreset.IsGenerated(column))
        {
            var quiet = Ui.Mono(value ?? Tr.T("sch.bom.mixed"), "dim");
            quiet.FontSize = 11.5;
            quiet.VerticalAlignment = VerticalAlignment.Center;
            return quiet;
        }

        // A line whose parts disagree says so and keeps the box empty: leaving it alone changes nothing, and whatever
        // is written goes to every part of the line.
        var box = Ui.EditableField(value ?? string.Empty, written => _ = _document.WriteAsync(row, column, written));
        box.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
        if (value is null)
        {
            box.PlaceholderText = Tr.T("sch.bom.mixed");
            ToolTip.SetTip(box, BomTable.Text(row, column, forExport: true));
        }

        _cells[(row.References.FirstOrDefault() ?? string.Empty, column)] = box;
        return box;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
