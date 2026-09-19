using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Anode.Sdk;
using Anode.Render;

namespace Anode.Plugin.Pcb;

/// <summary>
/// Layer rows of the active board (mockup 2c): a 12×12 ink swatch, the name in mono, and the shortcut digit on the
/// right. An empty frame means the layer is hidden. By default only the layers the drawing is read by are listed —
/// copper, mask, silkscreen; the rest is one click away.
/// </summary>
public sealed class LayersPanel : ContentControl
{
    /// <summary>Inks are drawn over the board sheet, so translucent layers are premixed for the swatch.</summary>
    private static readonly Color Sheet = Color.FromRgb(0xf8, 0xf4, 0xf4);

    private static readonly string[] MaskLayers = ["F.Mask", "B.Mask"];
    private static readonly string[] SilkLayers = ["F.SilkS", "B.SilkS"];

    private readonly IWorkbench _workbench;
    private bool _showAll;

    public LayersPanel(IWorkbench workbench)
    {
        _workbench = workbench;
        workbench.ActiveDocumentChanged += Render;
        Tr.Changed += Render;
        Render();
    }

    private void Render()
    {
        if (_workbench.ActiveDocument is not PcbDocument document)
        {
            Content = Ui.Text(Tr.T("pcb.layers.empty"), "dim");
            return;
        }

        var rows = new StackPanel { Spacing = 3 };
        int digit = 1;

        foreach (var layer in document.Scene.Layers.Where(l => l.IsCopper).OrderByDescending(l => l.DrawOrder))
        {
            rows.Children.Add(Row(document, [layer], layer.Name, digit <= 9 ? digit++ : null));
        }

        if (!_showAll)
        {
            AddGroup(rows, document, MaskLayers, "Mask", ref digit);
            AddGroup(rows, document, SilkLayers, "Silk", ref digit);
        }
        else
        {
            // A layer the scene split by colour — the page's coloured text — is one row, shown and hidden together.
            foreach (var group in document.Scene.Layers.Where(l => !l.IsCopper).OrderByDescending(l => l.DrawOrder)
                         .GroupBy(l => LayerStyle.BaseOf(l.Name)))
            {
                rows.Children.Add(Row(document, [.. group], Title(group.Key), null));
            }
        }

        var toggle = Ui.TagButton(Tr.T(_showAll ? "pcb.layers.fewer" : "pcb.layers.all"), "neutral", () =>
        {
            _showAll = !_showAll;
            Render();
        });
        toggle.Margin = new Thickness(0, 8, 0, 0);
        rows.Children.Add(toggle);

        Content = new ScrollViewer { Content = rows };
    }

    /// <summary>KiCad layers by their own names; the layers the scene adds by what they are.</summary>
    private static string Title(string name) => name switch
    {
        LayerStyle.PageFrame => Tr.T("pcb.layers.page"),
        LayerStyle.BoardBody => Tr.T("pcb.layers.body"),
        LayerStyle.PlatedHoles => Tr.T("pcb.layers.platedHoles"),
        LayerStyle.NonPlatedHoles => Tr.T("pcb.layers.holes"),
        _ => name,
    };

    private static void AddGroup(StackPanel rows, PcbDocument document, string[] names, string title, ref int digit)
    {
        var layers = document.Scene.Layers.Where(l => names.Contains(l.Name)).ToArray();
        if (layers.Length > 0)
        {
            rows.Children.Add(Row(document, layers, title, digit <= 9 ? digit++ : null));
        }
    }

    private static Control Row(PcbDocument document, IReadOnlyList<LayerGeometry> layers, string title, int? shortcut)
    {
        var line = new DockPanel { LastChildFill = false };
        var swatch = Ui.Swatch(new SolidColorBrush(Premixed(layers[0].Color)), hollow: !layers[0].IsVisible);
        swatch.Margin = new Thickness(0, 0, 7, 0);
        line.Children.Add(swatch);

        var name = Ui.Mono(title, layers[0].IsVisible ? string.Empty : "faint");
        name.FontSize = 11.5;
        line.Children.Add(name);

        if (shortcut is { } digit)
        {
            var hint = Ui.Mono(digit.ToString(System.Globalization.CultureInfo.InvariantCulture), "faint");
            DockPanel.SetDock(hint, Dock.Right);
            line.Children.Add(hint);
        }

        var row = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = line };
        row.Click += (_, _) =>
        {
            bool visible = !layers[0].IsVisible;
            foreach (var layer in layers)
            {
                layer.IsVisible = visible;
            }

            swatch.Background = visible ? new SolidColorBrush(Premixed(layers[0].Color)) : Brushes.Transparent;
            swatch.BorderThickness = new Thickness(visible ? 0 : 1);
            name.Classes.Set("faint", !visible);
            document.Redraw();
        };

        return row;
    }

    private static Color Premixed(ColorRgba ink)
    {
        double a = ink.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((ink.R * a) + (Sheet.R * (1 - a))),
            (byte)Math.Round((ink.G * a) + (Sheet.G * (1 - a))),
            (byte)Math.Round((ink.B * a) + (Sheet.B * (1 - a))));
    }
}
