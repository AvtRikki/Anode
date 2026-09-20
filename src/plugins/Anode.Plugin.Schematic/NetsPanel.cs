using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The nets of the sheet on screen: each with the pins it reaches — of parts, and of child sheets — named ones first. Choosing one lights it on the
/// canvas as the backquote does from a selection, and choosing it again puts it out — so a net can be followed
/// across a sheet without hunting for a wire to click on first.
///
/// The rows are rebuilt only when the list or the lit net actually differs from what is drawn: the panel holds a
/// filter box, and rebuilding it per announced frame would throw the caret out of it.
/// </summary>
internal sealed class NetsPanel : ContentControl
{
    private readonly IWorkbench _workbench;
    private readonly TextBox _filter;
    private readonly StackPanel _rows = new() { Spacing = 3 };
    private readonly TextBlock _summary;
    private readonly Control _body;
    private string _shown = string.Empty;

    public NetsPanel(IWorkbench workbench)
    {
        _workbench = workbench;

        _filter = new TextBox { Classes = { "filter" }, PlaceholderText = Tr.T("sch.nets.filter") };
        _filter.TextChanged += (_, _) => Fill();

        _summary = Ui.Text(string.Empty, "dim");
        _summary.FontSize = 11.5;
        _summary.Margin = new Thickness(0, 6, 0, 0);

        // Built once: a panel rebuilt on every announcement would throw the caret out of its own filter box, and
        // the boxes themselves cannot be moved from one parent to another.
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_summary, Dock.Bottom);
        head.Children.Add(_summary);
        head.Children.Add(_filter);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(new ScrollViewer { Content = _rows });
        _body = body;

        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        Tr.Changed += Retranslate;
        Render();
    }

    private SchematicDocument? Document => _workbench.ActiveDocument as SchematicDocument;

    /// <summary>The list for whatever is active now; a document other than a sheet leaves the panel with a word.</summary>
    private void Render()
    {
        if (Document is null)
        {
            _shown = string.Empty;
            Content = Ui.Text(Tr.T("sch.nets.empty"), "dim");
            return;
        }

        if (!ReferenceEquals(Content, _body))
        {
            Content = _body;
            _shown = string.Empty;
        }

        Fill();
    }

    private void Retranslate()
    {
        _filter.PlaceholderText = Tr.T("sch.nets.filter");
        _shown = string.Empty;
        Render();
    }

    private void Fill()
    {
        if (Document is not { } document)
        {
            return;
        }

        string filter = _filter.Text?.Trim() ?? string.Empty;
        var nets = document.Nets
            .Where(n => filter.Length == 0 || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.IsNamed)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Rebuilding the rows throws away nothing the user is typing into, but it is still work: only when the
        // list, or which net is lit, actually differs from what is drawn.
        var lit = document.HighlightedNet;
        string state = string.Join('\u001f', nets.Select(n => $"{n.Name}|{n.Connections}|{ReferenceEquals(n, lit)}"));
        if (state == _shown)
        {
            return;
        }

        _shown = state;
        _rows.Children.Clear();
        foreach (var net in nets)
        {
            _rows.Children.Add(Row(document, net, ReferenceEquals(net, lit)));
        }

        _summary.Text = filter.Length == 0
            ? Tr.T("sch.nets.count", document.Nets.Count, Tr.Plural("sch.net", document.Nets.Count))
            : Tr.T("sch.nets.found", nets.Count, document.Nets.Count);
    }

    private static Control Row(SchematicDocument document, SchNet net, bool lit)
    {
        var line = new DockPanel { LastChildFill = true };

        // Pins of parts and of child sheets alike: both are things this net reaches on this sheet.
        var pins = Ui.Mono(net.Connections.ToString(System.Globalization.CultureInfo.InvariantCulture), "faint");
        pins.FontSize = 11.5;
        pins.MinWidth = 18;
        pins.Margin = new Thickness(8, 0, 0, 0);
        pins.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(pins, Dock.Right);
        line.Children.Add(pins);

        var name = Ui.Mono(net.Name, lit ? "accentText" : net.IsNamed ? string.Empty : "faint");
        name.FontSize = 11.5;
        name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        line.Children.Add(name);

        // What is lit is asked for at the click, not remembered from when the row was drawn: the list is redrawn
        // whenever the sheet changes, and a row that remembered would put out a net it had never lit.
        var row = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = line };
        row.Click += (_, _) => document.LightNet(ReferenceEquals(document.HighlightedNet, net) ? null : net);
        return row;
    }
}
