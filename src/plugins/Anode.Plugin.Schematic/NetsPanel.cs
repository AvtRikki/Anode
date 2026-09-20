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
    private readonly Button _scope;
    private readonly Control _body;
    private string _shown = string.Empty;
    private bool _design;

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
        // The sheet on screen, or the design it belongs to: the same nets, joined through the sheets' own pins.
        _scope = Ui.TagButton(Tr.T("sch.nets.sheet"), "neutral", () =>
        {
            _design = !_design;
            _shown = string.Empty;
            Render();
        });

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_summary, Dock.Bottom);
        DockPanel.SetDock(_scope, Dock.Right);
        _scope.Margin = new Thickness(6, 0, 0, 0);
        head.Children.Add(_summary);
        head.Children.Add(_scope);
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
        _scope.Content = Tr.T(_design ? "sch.nets.design" : "sch.nets.sheet");
        _shown = string.Empty;
        Render();
    }

    private void Fill()
    {
        if (Document is not { } document)
        {
            return;
        }

        _scope.Content = Tr.T(_design ? "sch.nets.design" : "sch.nets.sheet");
        string filter = _filter.Text?.Trim() ?? string.Empty;
        bool Matches(string name) => filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var lit = document.HighlightedNet;
        var rows = _design
            ? [.. document.DesignNets.Where(n => Matches(n.Name))
                .OrderByDescending(n => n.IsNamed)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(n => (n.Name, n.IsNamed, Count: n.Pins.Count, Local: document.OnThisSheet(n)))]
            : document.Nets.Where(n => Matches(n.Name))
                .OrderByDescending(n => n.IsNamed)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(n => (n.Name, n.IsNamed, Count: n.Connections, Local: (SchNet?)n))
                .ToList();

        // Rebuilding the rows throws away nothing the user is typing into, but it is still work: only when the
        // list, or which net is lit, actually differs from what is drawn.
        string state = string.Join('\u001f', rows.Select(r => $"{r.Name}|{r.Count}|{ReferenceEquals(r.Local, lit)}|{r.Local is null}"));
        if (state == _shown)
        {
            return;
        }

        _shown = state;
        _rows.Children.Clear();
        foreach (var row in rows)
        {
            _rows.Children.Add(Row(document, row.Name, row.IsNamed, row.Count, row.Local, ReferenceEquals(row.Local, lit)));
        }

        int total = _design ? document.DesignNets.Count : document.Nets.Count;
        _summary.Text = filter.Length == 0
            ? Tr.T("sch.nets.count", total, Tr.Plural("sch.net", total))
            : Tr.T("sch.nets.found", rows.Count, total);
    }

    /// <summary>
    /// One net: its name, and how many pins it reaches. A design net that does not touch the sheet on screen has
    /// nothing here to light, and reads faint.
    /// </summary>
    private static Control Row(SchematicDocument document, string name, bool named, int count, SchNet? local, bool lit)
    {
        var line = new DockPanel { LastChildFill = true };

        var pins = Ui.Mono(count.ToString(System.Globalization.CultureInfo.InvariantCulture), "faint");
        pins.FontSize = 11.5;
        pins.MinWidth = 18;
        pins.Margin = new Thickness(8, 0, 0, 0);
        pins.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(pins, Dock.Right);
        line.Children.Add(pins);

        // A net named on a sheet below carries that sheet's path; the panel is narrow, so the name itself is shown
        // and the whole of it waits in the tooltip.
        int cut = name.LastIndexOf('/');
        var label = Ui.Mono(cut > 0 ? name[(cut + 1)..] : name, lit ? "accentText" : named && local is not null ? string.Empty : "faint");
        label.FontSize = 11.5;
        label.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, name);
        line.Children.Add(label);

        // What is lit is asked for at the click, not remembered from when the row was drawn: the list is redrawn
        // whenever the sheet changes, and a row that remembered would put out a net it had never lit.
        var row = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = line };
        row.Click += (_, _) =>
        {
            if (local is not null)
            {
                document.LightNet(ReferenceEquals(document.HighlightedNet, local) ? null : local);
            }
        };
        return row;
    }
}
