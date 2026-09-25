using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// KiCad's Symbol Fields Table as a panel at the foot of the window: the parts of the sheet on screen as lines, every
/// field any of them carries as a column, and a value written into a cell written into every part of its line.
///
/// Unlike KiCad's dialog there is no Apply: each value written is a step of its own on the undo list, as any other
/// edit on the sheet is. The table follows the sheet, and is rebuilt only when what it would show differs from
/// what it shows; the cell the caret was in when it had to be rebuilt gets the caret back.
/// </summary>
internal sealed class FieldsPanel : ContentControl
{
    private const double ReferenceWidth = 120;
    private const double NarrowWidth = 44;
    private const double FieldWidth = 150;
    private const double WideWidth = 210;

    private readonly IWorkbench _workbench;
    private readonly TextBox _filter;
    private readonly CheckBox _group;
    private readonly CheckBox _excluded;
    private readonly TextBlock _summary;
    private readonly ScrollViewer _table = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private readonly Dictionary<(string Line, string Column), TextBox> _cells = [];
    private readonly Control _body;
    private string _shown = "\u0000";

    public FieldsPanel(IWorkbench workbench)
    {
        _workbench = workbench;

        _filter = new TextBox { Classes = { "filter" }, Width = 180 };
        _filter.TextChanged += (_, _) => Fill();

        _group = Toggle(true);
        _excluded = Toggle(false);

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;
        _summary.VerticalAlignment = VerticalAlignment.Center;

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(_filter);
        head.Children.Add(_group);
        head.Children.Add(_excluded);
        head.Children.Add(_summary);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(_table);
        _body = body;

        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        Tr.Changed += Retranslate;
        Retranslate();
    }

    private SchematicDocument? Document => _workbench.ActiveDocument as SchematicDocument;

    private CheckBox Toggle(bool on)
    {
        var box = new CheckBox { IsChecked = on, MinHeight = 0, Padding = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        box.IsCheckedChanged += (_, _) => Fill();
        return box;
    }

    private void Retranslate()
    {
        _filter.PlaceholderText = Tr.T("sch.fields.filter");
        _group.Content = Ui.Text(Tr.T("sch.fields.group"), "value");
        _excluded.Content = Ui.Text(Tr.T("sch.fields.excluded"), "value");
        _shown = "\u0000";
        Render();
    }

    private void Render()
    {
        if (Document is null)
        {
            Content = Ui.Text(Tr.T("sch.fields.empty"), "dim");
            _shown = "\u0000";
            return;
        }

        if (!ReferenceEquals(Content, _body))
        {
            Content = _body;
            _shown = "\u0000";
        }

        Fill();
    }

    private void Fill()
    {
        if (Document is not { } document)
        {
            return;
        }

        var parts = document.FieldParts(_excluded.IsChecked == true);
        var columns = SchFieldsTable.Columns(parts);
        var rows = SchFieldsTable.Rows(parts, document.Instance, _group.IsChecked == true);

        // KiCad's filter looks at the designators only.
        string filter = _filter.Text?.Trim() ?? string.Empty;
        if (filter.Length > 0)
        {
            rows = [.. rows.Where(r => r.References.Any(reference => reference.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
        }

        string state = Signature(columns, rows);
        if (state == _shown)
        {
            return;
        }

        // The caret is in a cell that is about to be thrown away: remember which, and give it back afterwards.
        var focused = _cells.FirstOrDefault(c => c.Value.IsFocused).Key;

        _shown = state;
        _cells.Clear();
        _table.Content = Table(document, columns, rows);
        _summary.Text = Tr.T("sch.fields.count", rows.Count, rows.Sum(r => r.Quantity));

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

    private static string Signature(IReadOnlyList<string> columns, IReadOnlyList<SchFieldsRow> rows)
    {
        var text = new StringBuilder(string.Join('\u001f', columns));
        foreach (var row in rows)
        {
            text.Append('\u001e').Append(row.Shorthand).Append('|').Append(row.Dnp);
            foreach (var column in columns)
            {
                text.Append('\u001f').Append(row.Value(column) ?? "\u0001");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The table: KiCad's columns in its order — designators, value, datasheet, footprint, how many, do-not-place —
    /// then every other field. The designators are not written from here; a click on them shows the parts.
    /// </summary>
    private Grid Table(SchematicDocument document, IReadOnlyList<string> columns, IReadOnlyList<SchFieldsRow> rows)
    {
        var fields = columns.Where(c => !string.Equals(c, "Reference", StringComparison.Ordinal)).ToList();
        var order = new List<(string Key, string Title, double Width)> { ("Reference", Tr.T("sch.fields.reference"), ReferenceWidth) };
        foreach (var field in fields.Take(3))
        {
            order.Add((field, field, field is "Datasheet" or "Footprint" ? WideWidth : FieldWidth));
        }

        order.Add(("${QUANTITY}", Tr.T("sch.fields.quantity"), NarrowWidth));
        order.Add(("${DNP}", Tr.T("sch.fields.dnp"), NarrowWidth));
        foreach (var field in fields.Skip(3))
        {
            order.Add((field, field, FieldWidth));
        }

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 2 };
        foreach (var column in order)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(column.Width, GridUnitType.Pixel));
        }

        for (int r = 0; r <= rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int c = 0; c < order.Count; c++)
        {
            var title = Ui.Text(order[c].Title, "dim");
            title.FontSize = 11.5;
            title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(title, order[c].Title);
            Place(grid, title, 0, c);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < order.Count; c++)
            {
                Place(grid, Cell(document, row, order[c].Key), r + 1, c);
            }
        }

        return grid;
    }

    private Control Cell(SchematicDocument document, SchFieldsRow row, string column)
    {
        switch (column)
        {
            case "Reference":
            {
                var text = Ui.Mono(row.Shorthand);
                text.FontSize = 11.5;
                text.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(text, row.Shorthand);
                var button = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = text };
                button.Click += (_, _) => document.ShowParts(row);
                return button;
            }

            case "${QUANTITY}":
                return Quiet(row.Quantity.ToString(CultureInfo.InvariantCulture));

            case "${DNP}":
                return Quiet(row.Dnp ? Tr.T("sch.fields.dnp") : string.Empty);

            default:
            {
                // A line whose parts disagree shows it as KiCad does, and keeps the box empty: leaving it alone
                // changes nothing, and whatever is written goes to every part of the line.
                string? value = row.Value(column);
                var box = Ui.EditableField(value ?? string.Empty, written => document.WriteField(row, column, written));
                box.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
                if (value is null)
                {
                    box.PlaceholderText = Tr.T("sch.fields.mixed");
                    ToolTip.SetTip(box, row.Mixed(column));
                }

                _cells[(row.References.FirstOrDefault() ?? string.Empty, column)] = box;
                return box;
            }
        }
    }

    private static TextBlock Quiet(string text)
    {
        var block = Ui.Mono(text, "dim");
        block.FontSize = 11.5;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
