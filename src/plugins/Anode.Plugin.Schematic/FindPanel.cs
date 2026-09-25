using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// What is being looked for, kept apart from the panel so that the keys can step through it without the panel
/// being on screen — F3 and Shift+F3, as in KiCad — and so that it outlives the panel being closed and opened.
/// </summary>
internal static class FindSession
{
    public const string PanelId = "sch.find";

    public static string Text { get; set; } = string.Empty;

    public static string With { get; set; } = string.Empty;

    public static bool MatchCase { get; set; }

    public static SchFindMode Mode { get; set; }

    public static bool HiddenFields { get; set; }

    public static bool Pins { get; set; }

    public static bool ReplaceReferences { get; set; }

    /// <summary>
    /// What was selected when the search was narrowed to the selection. Taken once rather than read live: every
    /// step selects the place it lands on, and a live selection would narrow the search to that one place.
    /// </summary>
    public static IReadOnlyList<SchItem>? Within { get; set; }

    /// <summary>The place the last step landed on, so the next one goes on from it rather than from the start.</summary>
    public static SchFindHit? Current { get; set; }

    public static SchFindOptions Options => new(Text)
    {
        MatchCase = MatchCase,
        Mode = Mode,
        HiddenFields = HiddenFields,
        Pins = Pins,
        ReplaceReferences = ReplaceReferences,
    };

    /// <summary>The panel is asked to put the caret in its find box — or in the replace box, for Find and Replace.</summary>
    public static event Action<bool>? FocusRequested;

    /// <summary>Something the panel shows has changed from outside it: a step taken with a key.</summary>
    public static event Action? Changed;

    public static void RequestFocus(bool replace) => FocusRequested?.Invoke(replace);

    /// <summary>
    /// Goes to the next place (or the one before), from wherever the last step landed, round to the start at the
    /// end as KiCad does. Answers the places there are to go to.
    /// </summary>
    public static IReadOnlyList<SchFindHit> Step(SchematicDocument document, int direction)
    {
        var hits = document.Find(Options, Within);
        if (hits.Count == 0)
        {
            Current = null;
            Changed?.Invoke();
            return hits;
        }

        int at = IndexOf(hits, Current);
        int next = at >= 0
            ? (at + direction + hits.Count) % hits.Count
            : Beyond(hits, Current, direction);

        Go(document, hits[next]);
        return hits;
    }

    /// <summary>
    /// Where to go on from a place that is no longer among those found — it was just replaced — which is the first
    /// place past where it stood, in the order the places come in. With no last place, the first or the last.
    /// </summary>
    private static int Beyond(IReadOnlyList<SchFindHit> hits, SchFindHit? last, int direction)
    {
        if (last is null)
        {
            return direction > 0 ? 0 : hits.Count - 1;
        }

        static int Compare(SchFindHit a, SchFindHit b) =>
            a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X) : a.Position.Y.CompareTo(b.Position.Y);

        if (direction > 0)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                if (Compare(hits[i], last) >= 0)
                {
                    return i;
                }
            }

            return 0;
        }

        for (int i = hits.Count - 1; i >= 0; i--)
        {
            if (Compare(hits[i], last) <= 0)
            {
                return i;
            }
        }

        return hits.Count - 1;
    }

    public static void Go(SchematicDocument document, SchFindHit hit)
    {
        Current = hit;
        document.Show(hit);
        Changed?.Invoke();
    }

    /// <summary>
    /// Where a place found earlier stands among the places found now. Places are rebuilt on every search, so they
    /// are recognised by the text they came from rather than by the object.
    /// </summary>
    public static int IndexOf(IReadOnlyList<SchFindHit> hits, SchFindHit? hit)
    {
        if (hit is null)
        {
            return -1;
        }

        for (int i = 0; i < hits.Count; i++)
        {
            if (ReferenceEquals(hits[i].Node, hit.Node) && hits[i].Place == hit.Place && hits[i].Name == hit.Name)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// Find and Replace, as a panel at the foot of the window: the words to look for and to put instead on the left,
/// what they were found in on the right. A list rather than KiCad's one-at-a-time dialog, since every place is
/// found anyway to be stepped through, and a list shows at a glance how many there are and where.
///
/// The list follows the sheet — an edit, an undo, another sheet brought up — and is rebuilt only when what it would
/// show differs from what it shows, as the nets panel is, so the caret is never thrown out of the boxes.
/// </summary>
internal sealed class FindPanel : ContentControl
{
    private readonly IWorkbench _workbench;
    private readonly TextBox _find;
    private readonly TextBox _with;
    private readonly TextBlock _count;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ComboBox _mode;
    private readonly CheckBox _case;
    private readonly CheckBox _hidden;
    private readonly CheckBox _pins;
    private readonly CheckBox _references;
    private readonly CheckBox _selection;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _replace;
    private readonly Button _replaceAll;
    private readonly Control _body;
    private IReadOnlyList<SchFindHit> _hits = [];
    private string _shown = "\u0000";

    public FindPanel(IWorkbench workbench)
    {
        _workbench = workbench;

        _find = new TextBox { Classes = { "filter" }, Text = FindSession.Text };
        _find.TextChanged += (_, _) =>
        {
            FindSession.Text = _find.Text ?? string.Empty;
            FindSession.Current = null;
            Fill();
        };
        _find.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Step(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        };

        _with = new TextBox { Classes = { "filter" }, Text = FindSession.With };
        _with.TextChanged += (_, _) => FindSession.With = _with.Text ?? string.Empty;
        _with.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ReplaceOne();
                e.Handled = true;
            }
        };

        _count = Ui.Text(string.Empty, "dim");
        _count.FontSize = 11.5;
        _count.VerticalAlignment = VerticalAlignment.Center;

        _previous = Ui.TagButton("↑", "neutral", () => Step(-1));
        _next = Ui.TagButton("↓", "neutral", () => Step(1));
        _replace = Ui.TagButton(string.Empty, "neutral", ReplaceOne);
        _replaceAll = Ui.TagButton(string.Empty, "neutral", ReplaceAll);

        _mode = new ComboBox { Classes = { "choice" }, MinWidth = 120 };
        _mode.SelectionChanged += (_, _) =>
        {
            if (_mode.SelectedIndex >= 0 && (SchFindMode)_mode.SelectedIndex != FindSession.Mode)
            {
                FindSession.Mode = (SchFindMode)_mode.SelectedIndex;
                Changed();
            }
        };

        _case = Toggle(FindSession.MatchCase, on => FindSession.MatchCase = on);
        _hidden = Toggle(FindSession.HiddenFields, on => FindSession.HiddenFields = on);
        _pins = Toggle(FindSession.Pins, on => FindSession.Pins = on);
        _references = Toggle(FindSession.ReplaceReferences, on => FindSession.ReplaceReferences = on);
        _selection = Toggle(FindSession.Within is not null, on =>
            FindSession.Within = on && Document is { } document ? document.SelectedItems : null);

        _body = Layout();

        workbench.ActiveDocumentChanged += Render;
        workbench.ActiveDocumentStateChanged += Render;
        FindSession.FocusRequested += FocusBox;
        FindSession.Changed += () =>
        {
            _shown = "\u0000";
            Fill();
        };
        Tr.Changed += Retranslate;
        Retranslate();
    }

    private SchematicDocument? Document => _workbench.ActiveDocument as SchematicDocument;

    private Control Layout()
    {
        var findLine = new DockPanel { LastChildFill = true };
        foreach (var control in new Control[] { _count, _next, _previous })
        {
            DockPanel.SetDock(control, Dock.Right);
            control.Margin = new Thickness(6, 0, 0, 0);
            findLine.Children.Add(control);
        }

        findLine.Children.Add(_find);

        var withLine = new DockPanel { LastChildFill = true };
        foreach (var control in new Control[] { _replaceAll, _replace })
        {
            DockPanel.SetDock(control, Dock.Right);
            control.Margin = new Thickness(6, 0, 0, 0);
            withLine.Children.Add(control);
        }

        withLine.Children.Add(_with);

        var switches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { _mode, _case, _hidden, _pins, _references, _selection })
        {
            control.Margin = new Thickness(0, 0, 12, 4);
            control.VerticalAlignment = VerticalAlignment.Center;
            switches.Children.Add(control);
        }

        var controls = new StackPanel { Spacing = 6, Width = 380 };
        controls.Children.Add(findLine);
        controls.Children.Add(withLine);
        controls.Children.Add(switches);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(controls, Dock.Left);
        body.Children.Add(controls);
        body.Children.Add(new ScrollViewer { Content = _rows, Margin = new Thickness(14, 0, 0, 0) });
        return body;
    }

    private static CheckBox Toggle(bool on, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = on, MinHeight = 0, Padding = new Thickness(6, 0, 0, 0) };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>A setting changed: the list is found again, and stepping starts over from the first place.</summary>
    private void Changed()
    {
        FindSession.Current = null;
        _shown = "\u0000";
        Fill();
    }

    private void Retranslate()
    {
        _find.PlaceholderText = Tr.T("sch.find.find");
        _with.PlaceholderText = Tr.T("sch.find.with");
        _replace.Content = Tr.T("sch.find.replace");
        _replaceAll.Content = Tr.T("sch.find.replaceAll");
        ToolTip.SetTip(_previous, Tr.T("sch.find.previous"));
        ToolTip.SetTip(_next, Tr.T("sch.find.next"));
        _case.Content = Label("sch.find.matchCase");
        _hidden.Content = Label("sch.find.hiddenFields");
        _pins.Content = Label("sch.find.pins");
        _references.Content = Label("sch.find.references");
        _selection.Content = Label("sch.find.selection");

        var modes = new[] { "sch.find.plain", "sch.find.wholeWord", "sch.find.wildcard", "sch.find.regex" }.Select(Tr.T).ToList();
        _mode.ItemsSource = modes;
        _mode.SelectedIndex = (int)FindSession.Mode;

        _shown = "\u0000";
        Render();

        static TextBlock Label(string key) => Ui.Text(Tr.T(key), "value");
    }

    private void Render()
    {
        if (Document is null)
        {
            Content = Ui.Text(Tr.T("sch.find.empty"), "dim");
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

        var options = FindSession.Options;
        _hits = document.Find(options, FindSession.Within);
        int current = FindSession.IndexOf(_hits, FindSession.Current);

        bool anyWritable = _hits.Any(h => h.CanReplace(options));
        _replace.IsEnabled = current >= 0 && _hits[current].CanReplace(options);
        _replaceAll.IsEnabled = anyWritable;
        _previous.IsEnabled = _next.IsEnabled = _hits.Count > 0;

        _count.Text = options.Text.Length == 0
            ? string.Empty
            : _hits.Count == 0
                ? Tr.T("sch.find.none")
                : current >= 0 ? Tr.T("sch.find.of", current + 1, _hits.Count) : Tr.T("sch.find.count", _hits.Count);

        // Rebuilt only when what it shows would differ: the document announces a change for every frame drawn.
        var described = _hits.Select(h => Describe(document, h)).ToList();
        string state = current + "\u001e" + string.Join('\u001f', described.Select(d => $"{d.Owner}|{d.Where}|{d.Text}"));
        if (state == _shown)
        {
            return;
        }

        _shown = state;
        _rows.Children.Clear();
        for (int i = 0; i < _hits.Count; i++)
        {
            _rows.Children.Add(Row(document, _hits[i], described[i], i == current));
        }
    }

    private static Control Row(SchematicDocument document, SchFindHit hit, (string Owner, string Where, string Text) described, bool current)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var owner = Ui.Mono(described.Owner, current ? "accentText" : string.Empty);
        owner.FontSize = 11.5;
        owner.MinWidth = 70;
        line.Children.Add(owner);

        var where = Ui.Text(described.Where, "dim");
        where.FontSize = 11.5;
        where.MinWidth = 90;
        line.Children.Add(where);

        var text = Ui.Mono(described.Text, "faint");
        text.FontSize = 11.5;
        text.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        line.Children.Add(text);

        var row = new Button { Classes = { "row" }, Padding = new Thickness(4, 2), Content = line };
        row.Click += (_, _) => FindSession.Go(document, hit);
        return row;
    }

    /// <summary>What the place belongs to, which of its words were looked in, and what they say.</summary>
    private static (string Owner, string Where, string Text) Describe(SchematicDocument document, SchFindHit hit)
    {
        string owner = hit.Item switch
        {
            SymbolInstance symbol => symbol.ReferenceAt(document.Instance) ?? symbol.Reference ?? "?",
            SchSheet sheet => sheet.SheetName ?? Tr.T("sch.find.sheet"),
            SchLabel label => Tr.T("sch.find.label." + label.Kind.ToString().ToLowerInvariant()),
            SchText text => Tr.T(text.IsBox ? "sch.find.textBox" : "sch.find.text"),
            _ => hit.Item.GetType().Name,
        };

        string where = hit.Place switch
        {
            SchFindPlace.Reference => Tr.T("sch.find.reference"),
            SchFindPlace.Pin => Tr.T(hit.Name == "number" ? "sch.find.pinNumber" : "sch.find.pinName"),
            SchFindPlace.SheetPin => Tr.T("sch.find.sheetPin"),
            SchFindPlace.Field => hit.Name ?? string.Empty,
            _ => string.Empty,
        };

        return (owner, where, hit.Text.ReplaceLineEndings(" "));
    }

    private void Step(int direction)
    {
        if (Document is { } document)
        {
            FindSession.Step(document, direction);
        }
    }

    /// <summary>
    /// Replaces the place on show and goes on to the next, as KiCad's Replace does. With nothing on show yet, the
    /// first press only goes to the first place, so nothing is changed that has not been looked at.
    /// </summary>
    private void ReplaceOne()
    {
        if (Document is not { } document)
        {
            return;
        }

        int current = FindSession.IndexOf(_hits, FindSession.Current);
        if (current < 0)
        {
            FindSession.Step(document, 1);
            return;
        }

        // KiCad goes on from the place just replaced. If what it says now still matches, that is the place after it;
        // if not, it has left the list, and the next is the first one past where it stood.
        document.Replace(_hits[current], FindSession.Options, FindSession.With);
        FindSession.Step(document, 1);
    }

    private void ReplaceAll()
    {
        if (Document is not { } document)
        {
            return;
        }

        int changed = document.ReplaceAll(_hits, FindSession.Options, FindSession.With);
        FindSession.Current = null;
        _shown = "\u0000";
        Fill();
        _count.Text = Tr.T("sch.find.replaced", changed);
    }

    private void FocusBox(bool replace)
    {
        var box = replace ? _with : _find;

        // Ctrl+F with words selected on the sheet is not KiCad's; a find box that keeps what was last looked for, is.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (box.IsAttachedToVisualTree())
                {
                    box.Focus();
                    box.SelectAll();
                }
            },
            DispatcherPriority.Loaded);
    }
}
