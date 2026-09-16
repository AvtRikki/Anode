using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Anode.Sdk;
using Anode.Workbench.Services;

namespace Anode.Workbench.ViewModels;

public enum UnsavedChoice
{
    Cancel,
    Save,
    Discard,
}

/// <summary>
/// The workbench: document panes and tabs, docks and rail, palette, banner and status. Implements the plugin-facing
/// <see cref="IWorkbench"/>.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IWorkbench
{
    public const double MinLeftDock = 170;
    public const double MaxLeftDock = 280;
    public const double MinRightDock = 240;
    public const double MaxRightDock = 360;

    private static readonly PluginManifest ShellManifest = new("anode.workbench", "Kicad·One", "0.1", "Anode.Workbench.dll", typeof(ShellViewModel).FullName!, PlatformContract.Version);

    private readonly Dictionary<string, Control> _panelContent = [];
    private readonly Dictionary<string, (string? Active, bool Collapsed)> _stackState = [];
    private readonly HashSet<string> _sentToRail = [];
    private readonly List<string> _pinned = [];
    private readonly RecentProjectsStore _recents;
    private readonly SettingsStore? _settings;
    private DocumentPaneViewModel _activePane;
    private IDocument? _observed;
    private double _leftWidth = 212;
    private double _rightWidth = 268;
    private double _bottomHeight = 150;

    public ShellViewModel(LogService log, RecentProjectsStore recents, SettingsStore? settings = null)
    {
        Log = log;
        _recents = recents;
        _settings = settings;
        Palette = new PaletteViewModel(Commands);
        Context = new PluginContext(ShellManifest, this);

        _activePane = new DocumentPaneViewModel(this) { IsFocused = true };
        Panes.Add(_activePane);
        Panes.CollectionChanged += (_, _) =>
        {
            foreach (var pane in Panes)
            {
                pane.RaiseIsSecondary();
            }
        };

        Panels.Changed += Relayout;
        Tr.Changed += OnLanguageChanged;
        RecentProjects = new ObservableCollection<RecentProject>(recents.Load());
    }

    public LogService Log { get; }

    ILog IWorkbench.Log => Log;

    public CommandRegistry Commands { get; } = new();

    public PanelRegistry Panels { get; } = new();

    public DocumentRegistry DocumentTypes { get; } = new();

    /// <summary>Context handed to documents and built-in contributions.</summary>
    public IPluginContext Context { get; }

    public PaletteViewModel Palette { get; }

    /// <summary>Texts of the window itself, bound from XAML.</summary>
    public ShellStrings Strings { get; } = new();

    public ObservableCollection<DocumentPaneViewModel> Panes { get; } = [];

    public DocumentPaneViewModel ActivePane
    {
        get => _activePane;
        private set => SetProperty(ref _activePane, value);
    }

    public ObservableCollection<DockStackViewModel> LeftStacks { get; } = [];

    public ObservableCollection<DockStackViewModel> RightStacks { get; } = [];

    [ObservableProperty]
    public partial DockStackViewModel? BottomStack { get; set; }

    public ObservableCollection<RailItemViewModel> Rail { get; } = [];

    /// <summary>The rail is part of the frame (mockup 2c): it stays even when every panel is docked.</summary>
    public bool HasRail => true;

    public ObservableCollection<RecentProject> RecentProjects { get; }

    public ObservableCollection<StatusField> StatusLeft { get; } = [];

    public ObservableCollection<StatusField> StatusRight { get; } = [];

    [ObservableProperty]
    public partial PanelDescriptor? SlideOver { get; set; }

    [ObservableProperty]
    public partial Control? SlideOverContent { get; set; }

    [ObservableProperty]
    public partial Banner? CurrentBanner { get; set; }

    [ObservableProperty]
    public partial bool IsPaletteOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Kicad·One";

    [ObservableProperty]
    public partial string ProjectName { get; set; } = Tr.T("shell.project.none");

    [ObservableProperty]
    public partial string? ProjectDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth), nameof(IsLeftDockShown))]
    public partial bool IsLeftDockVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth), nameof(IsRightDockShown))]
    public partial bool IsRightDockVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BottomRowHeight), nameof(IsBottomDockShown))]
    public partial bool IsBottomDockVisible { get; set; } = true;

    public bool IsLeftDockShown => IsLeftDockVisible && LeftStacks.Count > 0;

    public bool IsRightDockShown => IsRightDockVisible && RightStacks.Count > 0;

    public bool IsBottomDockShown => IsBottomDockVisible && BottomStack is not null;

    public GridLength LeftColumnWidth
    {
        get => IsLeftDockShown ? new GridLength(_leftWidth) : new GridLength(0);
        set => SetDockSize(ref _leftWidth, value, MinLeftDock, MaxLeftDock);
    }

    public GridLength RightColumnWidth
    {
        get => IsRightDockShown ? new GridLength(_rightWidth) : new GridLength(0);
        set => SetDockSize(ref _rightWidth, value, MinRightDock, MaxRightDock);
    }

    public GridLength BottomRowHeight
    {
        get => IsBottomDockShown ? new GridLength(_bottomHeight) : new GridLength(0);
        set => SetDockSize(ref _bottomHeight, value, 96, 420);
    }

    /// <summary>Asks about unsaved documents before closing them; set by the window.</summary>
    public Func<IReadOnlyList<IDocument>, Task<UnsavedChoice>>? ConfirmUnsaved { get; set; }

    public Func<Task<string?>>? PickFileToOpen { get; set; }

    public Func<IDocument, Task<string?>>? PickSavePath { get; set; }

    // ——— IWorkbench ———

    public IReadOnlyList<IDocument> Documents => [.. Panes.SelectMany(p => p.Tabs).Select(t => t.Document)];

    public IDocument? ActiveDocument => ActivePane.ActiveTab?.Document;

    public event Action? ActiveDocumentChanged;

    public event Action? ActiveDocumentStateChanged;

    public event Action? ProjectChanged;

    public ThemeVariant Theme
    {
        get => Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        set
        {
            if (Application.Current is { } app)
            {
                app.RequestedThemeVariant = value;
            }

            OnPropertyChanged();
        }
    }

    public async Task<IDocument?> OpenAsync(string path)
    {
        string full = Path.GetFullPath(path);
        var existing = Panes.SelectMany(p => p.Tabs).FirstOrDefault(t => string.Equals(t.Document.FilePath, full, StringComparison.Ordinal));
        if (existing is not null)
        {
            ActivateTab(existing);
            return existing.Document;
        }

        string name = Path.GetFileName(full);
        if (DocumentTypes.FindFor(full) is not { } type)
        {
            Log.Warn(Tr.T("shell.log.noPlugin", Path.GetExtension(full), name));
            ShowBanner(new Banner(Tr.T("shell.banner.noPlugin", Path.GetExtension(full)), IsAlert: false));
            return null;
        }

        IsBusy = true;
        try
        {
            var document = await type.OpenAsync(full, CancellationToken.None);
            AddDocument(document);
            SetProject(full);
            Replace(RecentProjects, _recents.Touch(full, DateTime.Now));
            Log.Info(Tr.T("shell.log.opened", name, type.Label));
            return document;
        }
        catch (Exception ex)
        {
            Log.Error(Tr.T("shell.log.openFailed", name, ex.Message), ex);
            ShowBanner(new Banner(Tr.T("shell.log.openFailed", name, ex.Message)));
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Activate(IDocument document)
    {
        if (Panes.SelectMany(p => p.Tabs).FirstOrDefault(t => ReferenceEquals(t.Document, document)) is { } tab)
        {
            ActivateTab(tab);
        }
    }

    public void ShowBanner(Banner banner) => CurrentBanner = banner;

    public void DismissBanner(Banner banner)
    {
        if (ReferenceEquals(CurrentBanner, banner))
        {
            CurrentBanner = null;
        }
    }

    // ——— Documents ———

    public void AddDocument(IDocument document, DocumentPaneViewModel? pane = null)
    {
        pane ??= ActivePane;
        var tab = new DocumentTabViewModel(document, pane);
        pane.Tabs.Add(tab);
        ActivateTab(tab);
    }

    public void ActivateTab(DocumentTabViewModel tab)
    {
        var previous = ActiveDocument;
        foreach (var pane in Panes)
        {
            pane.IsFocused = ReferenceEquals(pane, tab.Pane);
        }

        tab.Pane.ActiveTab = tab;
        ActivePane = tab.Pane;

        if (!ReferenceEquals(previous, tab.Document))
        {
            previous?.Deactivate();
            Observe(tab.Document);
            tab.Document.Activate(Context);
            ActiveDocumentChanged?.Invoke();
        }

        RefreshDocumentState();
    }

    public async Task CloseTabAsync(DocumentTabViewModel tab)
    {
        var document = tab.Document;
        if (document.IsDirty && ConfirmUnsaved is { } confirm)
        {
            switch (await confirm([document]))
            {
                case UnsavedChoice.Cancel:
                    return;
                case UnsavedChoice.Save when !await SaveAsync(document):
                    return;
            }
        }

        var pane = tab.Pane;
        bool wasActive = ReferenceEquals(ActiveDocument, document);
        int index = pane.Tabs.IndexOf(tab);

        if (ReferenceEquals(pane.ActiveTab, tab))
        {
            pane.ActiveTab = null;
        }

        if (wasActive)
        {
            document.Deactivate();
            Observe(null);
        }

        pane.Tabs.Remove(tab);
        tab.Dispose();
        document.Dispose();

        if (pane.Tabs.Count == 0 && Panes.Count > 1)
        {
            Panes.Remove(pane);
            pane = Panes[0];
        }

        var next = pane.ActiveTab ?? (pane.Tabs.Count > 0 ? pane.Tabs[Math.Clamp(index - 1, 0, pane.Tabs.Count - 1)] : null);
        if (next is not null)
        {
            ActivateTab(next);
        }
        else
        {
            ActivePane = pane;
            ActiveDocumentChanged?.Invoke();
            RefreshDocumentState();
        }
    }

    public async Task<bool> SaveAsync(IDocument document, bool askForPath = false)
    {
        string? path = null;
        if (askForPath || document.FilePath is null)
        {
            if (PickSavePath is null || await PickSavePath(document) is not { } picked)
            {
                return false;
            }

            path = picked;
        }

        try
        {
            bool saved = await document.SaveAsync(path);
            if (saved)
            {
                Log.Info(Tr.T("shell.log.saved", document.Title));
            }

            return saved;
        }
        catch (Exception ex)
        {
            Log.Error(Tr.T("shell.log.saveFailed", document.Title, ex.Message), ex);
            ShowBanner(new Banner(Tr.T("shell.log.saveFailed", document.Title, ex.Message)));
            return false;
        }
    }

    /// <summary>Moves the active tab into a second pane, or merges the panes back.</summary>
    public void ToggleSplit()
    {
        if (Panes.Count == 2)
        {
            var first = Panes[0];
            var second = Panes[1];
            var active = second.ActiveTab ?? first.ActiveTab;
            second.ActiveTab = null;
            foreach (var tab in second.Tabs.ToList())
            {
                second.Tabs.Remove(tab);
                tab.Pane = first;
                first.Tabs.Add(tab);
            }

            Panes.Remove(second);
            if (active is not null)
            {
                ActivateTab(active);
            }

            return;
        }

        if (ActivePane.ActiveTab is not { } moving || ActivePane.Tabs.Count < 2)
        {
            Log.Info(Tr.T("shell.log.splitHint"));
            return;
        }

        var source = ActivePane;
        int index = source.Tabs.IndexOf(moving);
        source.ActiveTab = source.Tabs[index == 0 ? 1 : index - 1];
        source.Tabs.Remove(moving);

        var pane = new DocumentPaneViewModel(this);
        moving.Pane = pane;
        pane.Tabs.Add(moving);
        Panes.Add(pane);
        ActivateTab(moving);
    }

    // ——— Docks and rail ———

    public Control PanelContent(PanelDescriptor descriptor)
    {
        if (!_panelContent.TryGetValue(descriptor.Id, out var control))
        {
            control = descriptor.CreateContent(this);
            _panelContent[descriptor.Id] = control;
        }

        // The same panel moves between a dock stack and the slide-over; the previous host must let go of it first.
        return Controls.ControlHost.Detach(control)!;
    }

    public void RememberStack(string key, string? activeId, bool collapsed) => _stackState[key] = (activeId, collapsed);

    public void SendToRail(string panelId)
    {
        _pinned.Remove(panelId);
        _sentToRail.Add(panelId);
        Relayout();
    }

    public void PinFromRail(string panelId)
    {
        CloseSlideOver();
        _sentToRail.Remove(panelId);
        _pinned.Remove(panelId);
        _pinned.Add(panelId);
        Relayout();
    }

    public void ToggleSlideOver(PanelDescriptor descriptor)
    {
        if (SlideOver?.Id == descriptor.Id)
        {
            CloseSlideOver();
            return;
        }

        SlideOverContent = null;
        SlideOver = descriptor;
        SlideOverContent = PanelContent(descriptor);
        foreach (var item in Rail)
        {
            item.IsOpen = item.Descriptor.Id == descriptor.Id;
        }
    }

    public void CloseSlideOver()
    {
        SlideOverContent = null;
        SlideOver = null;
        foreach (var item in Rail)
        {
            item.IsOpen = false;
        }
    }

    public void Relayout()
    {
        var layout = DockPlanner.Plan(Panels.Panels, _sentToRail, _pinned);

        // Detach old stacks first so panel controls can be re-parented into the new ones.
        LeftStacks.Clear();
        RightStacks.Clear();
        BottomStack = null;

        foreach (var stack in layout.Left)
        {
            LeftStacks.Add(CreateStack(stack));
        }

        foreach (var stack in layout.Right)
        {
            RightStacks.Add(CreateStack(stack));
        }

        BottomStack = layout.Bottom is { } bottom ? CreateStack(bottom) : null;

        if (SlideOver is { } open && !layout.Rail.Contains(open))
        {
            CloseSlideOver();
        }

        Rail.Clear();
        foreach (var panel in layout.Rail)
        {
            Rail.Add(new RailItemViewModel(panel, this) { IsOpen = SlideOver?.Id == panel.Id });
        }

        OnPropertyChanged(nameof(HasRail));
        OnPropertyChanged(nameof(LeftColumnWidth));
        OnPropertyChanged(nameof(RightColumnWidth));
        OnPropertyChanged(nameof(BottomRowHeight));
        OnPropertyChanged(nameof(IsLeftDockShown));
        OnPropertyChanged(nameof(IsRightDockShown));
        OnPropertyChanged(nameof(IsBottomDockShown));
    }

    // ——— Language ———

    /// <summary>Switches the interface language and remembers the choice.</summary>
    public void SetLanguage(CultureInfo culture)
    {
        Tr.SetCulture(culture);
        if (_settings is not null)
        {
            _settings.Language = culture;
        }
    }

    private void OnLanguageChanged()
    {
        Strings.RaiseAll();
        if (ProjectDirectory is null)
        {
            ProjectName = Tr.T("shell.project.none");
        }

        // Dock tabs, rail labels and command titles all read their texts through the descriptors.
        Relayout();
        Commands.RaiseChanged();
        RefreshDocumentState();
        foreach (var pane in Panes)
        {
            pane.RaiseSummary();
            foreach (var tab in pane.Tabs)
            {
                tab.RaiseTitle();
            }
        }

        if (IsPaletteOpen)
        {
            Palette.Refresh();
        }
    }

    // ——— Palette ———

    public void OpenPalette()
    {
        CloseSlideOver();
        Palette.Query = string.Empty;
        Palette.Refresh();
        IsPaletteOpen = true;
    }

    public void ExecutePaletteSelection()
    {
        var selected = Palette.Selected;
        IsPaletteOpen = false;
        if (selected is not null)
        {
            Commands.TryExecute(selected.Command.Id);
        }
    }

    // ——— Internals ———

    private DockStackViewModel CreateStack(DockStack stack)
    {
        _stackState.TryGetValue(stack.Key, out var state);
        return new DockStackViewModel(stack, this, state.Active, state.Collapsed);
    }

    private void Observe(IDocument? document)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnObservedChanged;
        }

        _observed = document;
        if (document is not null)
        {
            document.PropertyChanged += OnObservedChanged;
        }
    }

    private void OnObservedChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IDocument.Title) or nameof(IDocument.IsDirty):
                UpdateWindowTitle();
                break;
            case nameof(IDocument.Summary):
                ActivePane.RaiseSummary();
                break;
            default:
                RefreshDocumentState();
                break;
        }
    }

    private void RefreshDocumentState()
    {
        var fields = ActiveDocument?.StatusFields ?? [];
        Replace(StatusLeft, fields.Where(f => !f.AlignEnd));
        Replace(StatusRight, fields.Where(f => f.AlignEnd));
        UpdateWindowTitle();
        ActiveDocumentStateChanged?.Invoke();
    }

    private void UpdateWindowTitle() =>
        WindowTitle = ActiveDocument is { } doc ? $"{doc.Title}{(doc.IsDirty ? " •" : string.Empty)} — Kicad·One" : "Kicad·One";

    private void SetProject(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is null || ProjectDirectory is not null)
        {
            return;
        }

        ProjectDirectory = directory;
        string? pro = Directory.EnumerateFiles(directory, "*.kicad_pro").FirstOrDefault();
        ProjectName = Path.GetFileNameWithoutExtension(pro ?? filePath);
        ProjectChanged?.Invoke();
    }

    private void SetDockSize(ref double field, GridLength value, double min, double max)
    {
        if (value.IsAbsolute && value.Value > 0)
        {
            field = Math.Clamp(value.Value, min, max);
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private sealed class PluginContext(PluginManifest manifest, ShellViewModel shell) : IPluginContext
    {
        public PluginManifest Manifest { get; } = manifest;

        public IWorkbench Workbench => shell;

        public ICommandRegistry Commands => shell.Commands;

        public IPanelRegistry Panels => shell.Panels;

        public IDocumentRegistry Documents => shell.DocumentTypes;

        public ILog Log => shell.Log;
    }

    public IPluginContext CreatePluginContext(PluginManifest manifest) => new PluginContext(manifest, this);
}
