using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anode.Sdk;

/// <summary>
/// Building blocks of the Kicad·One kit for code-built views, so plugin panels look like the workbench. They rely on
/// the style classes of the workbench theme: <c>mono</c>, <c>dim</c>, <c>faint</c>, <c>overline</c>, <c>field</c>,
/// <c>tag</c> with <c>accent</c>/<c>alert</c>/<c>neutral</c>/<c>outline</c>.
/// </summary>
public static class Ui
{
    public static TextBlock Text(string text, params string[] classes)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        AddClasses(block, classes);
        return block;
    }

    /// <summary>Monospaced: coordinates, values, part numbers, times — anything compared down a column.</summary>
    public static TextBlock Mono(string text, params string[] classes) => Text(text, ["mono", .. classes]);

    /// <summary>Section overline: 10 px caps in accent (or alert).</summary>
    public static TextBlock Overline(string text, bool alert = false) =>
        Text(text.ToUpperInvariant(), alert ? ["overline", "alert"] : ["overline"]);

    /// <summary>A value in a field: mono text on the field fill.</summary>
    public static Border Field(string text) => new() { Classes = { "field" }, Child = Mono(text, "value") };

    /// <param name="variant">accent (all good), alert (decision needed), neutral (fact), outline (action on the row).</param>
    public static Border Tag(string text, string variant = "neutral") =>
        new() { Classes = { "tag", variant }, Child = new TextBlock { Text = text }, VerticalAlignment = VerticalAlignment.Center };

    public static Button TagButton(string text, string variant, Action onClick)
    {
        var button = new Button { Classes = { "tagButton", variant }, Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>12×12 ink swatch; hollow means the layer is hidden.</summary>
    public static Border Swatch(IBrush ink, bool hollow = false) => new()
    {
        Width = 12,
        Height = 12,
        Background = hollow ? Brushes.Transparent : ink,
        BorderThickness = new Thickness(hollow ? 1 : 0),
        Classes = { "swatch" },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Square issue marker: alert for errors, neutral-500 for warnings.</summary>
    public static Border IssueMarker(IssueSeverity severity) => new()
    {
        Width = 7,
        Height = 7,
        Classes = { "issueMarker", severity == IssueSeverity.Error ? "error" : "warning" },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Serif labels on the left (fixed width), mono values in fields; section items become overlines.</summary>
    public static Grid PropertyGrid(IEnumerable<PropertyItem> items, double labelWidth = 78)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"), RowSpacing = 7, ColumnSpacing = 10 };
        int row = 0;
        foreach (var item in items)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (item.IsSection)
            {
                var overline = Overline(item.Name);
                overline.Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 0);
                Grid.SetRow(overline, row);
                Grid.SetColumnSpan(overline, 2);
                grid.Children.Add(overline);
            }
            else
            {
                var label = Text(item.Name, "dim");
                label.FontSize = 12.5;
                Grid.SetRow(label, row);
                grid.Children.Add(label);

                var value = Field(item.Value);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
            }

            row++;
        }

        return grid;
    }

    /// <summary>Text with some character ranges emphasised in the accent text colour (palette matches).</summary>
    public static void SetHighlighted(TextBlock block, string text, IEnumerable<(int Start, int Length)> ranges, IBrush highlight)
    {
        block.Inlines ??= new InlineCollection();
        block.Inlines.Clear();
        int at = 0;
        foreach (var (start, length) in ranges.Where(r => r.Length > 0).OrderBy(r => r.Start))
        {
            if (start < at || start + length > text.Length)
            {
                continue;
            }

            if (start > at)
            {
                block.Inlines.Add(new Run(text[at..start]));
            }

            block.Inlines.Add(new Run(text.Substring(start, length)) { Foreground = highlight });
            at = start + length;
        }

        if (at < text.Length)
        {
            block.Inlines.Add(new Run(text[at..]));
        }
    }

    private static void AddClasses(StyledElement element, IEnumerable<string> classes)
    {
        foreach (string name in classes)
        {
            if (!string.IsNullOrEmpty(name))
            {
                element.Classes.Add(name);
            }
        }
    }
}
