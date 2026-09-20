using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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

    private static readonly PluginManifest ShellManifest = new("anode.workbench", "Anode", "0.1", "Anode.Workbench.dll", typeof(ShellViewModel).FullName!, PlatformContract.Version);

    private readonly Dictionary<string, Control> _panelContent = [];
    private readonly Dictionary<string, Controls.PanelHost> _panelHosts = [];
    private readonly List<Controls.PanelHost> _liveHosts = [];
    private readonly Dictionary<string, (string? Active, bool Collapsed)> _stackState = [];
    private readonly HashSet<string> _sentToRail = [];
    private readonly RecentProjectsStore _recents;
    private readonly SettingsStore? _settings;
    private DocumentPaneViewModel _activePane;
    private IDocument? _observed;
    private static readonly StringComparer FilePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, Task<IDocument?>> _opening = new(FilePaths);
    private readonly HashSet<string> _savingPaths = new(FilePaths);
    private readonly HashSet<IDocument> _savingDocuments = new(ReferenceEqualityComparer.Instance);
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
        // Undo and redo live in the title bar; whether they exist at all is the active document's business.
        Commands.Changed += RaiseHistoryState;
        // Panels follow the document: a board brings its layers, a sheet brings its hierarchy.
        ActiveDocumentChanged += Relayout;
        Tr.Changed += OnLanguageChanged;
        RecentProjects = new ObservableCollection<RecentProject>(recents.Load());
    }

    public LogService Log { get; }

    ILog IWorkbench.Log => Log;

    public CommandRegistry Commands { get; } = new();

    public PanelRegistry Panels { get; } = new();

    public DocumentRegistry DocumentTypes { get; } = new();

    /// <summary>What plugins told us about the files of the project, for the tree.</summary>
    public ProjectStructureRegistry ProjectStructure { get; } = new();

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

    /// <summary>The icons of each place, in the rail of its own side: top place at the top, bottom place at the bottom.</summary>
    public ObservableCollection<RailItemViewModel> LeftRailTop { get; } = [];

    public ObservableCollection<RailItemViewModel> LeftRailBottom { get; } = [];

    public ObservableCollection<RailItemViewModel> RightRailTop { get; } = [];

    public ObservableCollection<RailItemViewModel> RightRailBottom { get; } = [];

    /// <summary>The bottom dock is run from the left rail: its icons stand at the foot of it, below the left place.</summary>
    public ObservableCollection<RailItemViewModel> BottomRail { get; } = [];

    /// <summary>Every icon of the rails, for lookups.</summary>
    public IEnumerable<RailItemViewModel> Rail =>
        LeftRailTop.Concat(LeftRailBottom).Concat(BottomRail).Concat(RightRailTop).Concat(RightRailBottom);

    /// <summary>The foot of the left rail holds two groups; they are told apart by a line only when both are there.</summary>
    public bool HasRailDivider => LeftRailBottom.Count > 0 && BottomRail.Count > 0;

    /// <summary>The left rail is part of the frame (mockup 2c): it stays even when every panel is docked.</summary>
    public bool HasRail => true;

    /// <summary>The right rail appears only when the right places hold something.</summary>
    public bool HasRightRail => RightRailTop.Count > 0 || RightRailBottom.Count > 0;

    public ObservableCollection<RecentProject> RecentProjects { get; }

    public ObservableCollection<StatusField> StatusLeft { get; } = [];

    public ObservableCollection<StatusField> StatusRight { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlideOverSide))]
    public partial PanelDescriptor? SlideOver { get; set; }

    /// <summary>A slid-over panel hugs the edge its dock would be on.</summary>
    public HorizontalAlignment SlideOverSide =>
        SlideOver is { } panel && panel.Area.IsRight() ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    [ObservableProperty]
    public partial Banner? CurrentBanner { get; set; }

    [ObservableProperty]
    public partial bool IsPaletteOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Anode";

    [ObservableProperty]
    public partial string ProjectName { get; set; } = Tr.T("shell.project.none");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    public partial string? ProjectDirectory { get; set; }

    /// <summary>Branch of the version control system the project sits in, or null when it is not in one.</summary>
    [ObservableProperty]
    public partial string? ProjectBranch { get; set; }

    public bool HasProject => ProjectDirectory is not null;

    /// <summary>The active document offers undo at all — the title bar shows its buttons only then.</summary>
    public bool HasHistory => Commands.Find("edit.undo") is not null || Commands.Find("edit.redo") is not null;

    public bool CanUndo => CanRun("edit.undo");

    public bool CanRedo => CanRun("edit.redo");

    public string UndoTip => Tip("edit.undo", "shell.hint.undo");

    public string RedoTip => Tip("edit.redo", "shell.hint.redo");

    /// <summary>Raises the state of the history buttons; the document says when its history moved.</summary>
    public void RaiseHistoryState()
    {
        foreach (string name in (string[])[nameof(HasHistory), nameof(CanUndo), nameof(CanRedo), nameof(UndoTip), nameof(RedoTip)])
        {
            OnPropertyChanged(name);
        }
    }

    private bool CanRun(string id)
    {
        if (Commands.Find(id) is not { } command)
        {
            return false;
        }

        try
        {
            return command.CanExecute();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string Tip(string id, string fallbackKey) =>
        Commands.Find(id) is { } command ? command.Title : Tr.T(fallbackKey);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftColumnWidth), nameof(IsLeftDockShown))]
    public partial bool IsLeftDockVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightColumnWidth), nameof(IsRightDockShown))]
    public partial bool IsRightDockVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BottomRowHeight), nameof(IsBottomDockShown))]
    public partial bool IsBottomDockVisible { get; set; } = true;

    // A closed section is not a dock: the column goes with the last open section, and the rail icon is the way back.
    public bool IsLeftDockShown => IsLeftDockVisible && LeftStacks.Any(s => !s.IsCollapsed);

    public bool IsRightDockShown => IsRightDockVisible && RightStacks.Any(s => !s.IsCollapsed);

    public bool IsBottomDockShown => IsBottomDockVisible && BottomStack is { IsCollapsed: false };

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

    /// <summary>Asks the window where to put a file that does not exist yet: name, extension, dialog title, folder.</summary>
    public Func<string, string, string, string?, Task<string?>>? PickNewFile { get; set; }

    /// <summary>The type that can start a file from nothing; null when no plugin offers one.</summary>
    public IDocumentType? CreatableType => DocumentTypes.Types.FirstOrDefault(t => t.CanCreate);

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

    /// <summary>
    /// A project from nothing: a folder with a KiCad project file and one empty document in it, opened straight
    /// away. Which document that is belongs to the plugins — the shell asks for the first type that can create one.
    /// Passing <paramref name="path"/> skips the dialog.
    /// </summary>
    public async Task<IDocument?> NewProjectAsync(string? path = null)
    {
        if (CreatableType is not { } type)
        {
            Log.Warn(Tr.T("shell.project.noCreator"));
            return null;
        }

        path ??= PickNewFile is { } pick
            ? await pick(Tr.T("shell.project.newName"), ".kicad_pro", "command.file.newProject", ProjectDirectory)
            : null;

        if (path is null)
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(path);
            string name = Path.GetFileNameWithoutExtension(full);
            string parent = Path.GetDirectoryName(full) ?? throw new IOException(full);

            // The folder is the project, so a new one gets a folder of its own named after it — unless the place the
            // user picked is already that folder.
            string directory = string.Equals(Path.GetFileName(parent), name, StringComparison.Ordinal)
                ? parent
                : Path.Combine(parent, name);
            Directory.CreateDirectory(directory);

            string project = Path.Combine(directory, name + ".kicad_pro");
            string document = Path.Combine(directory, name + type.Extensions[0]);
            await CreateFilesAsync(type, document, project, ProjectFile(name));

            OpenProject(directory);
            Log.Info(Tr.T("shell.project.created", name));
            return await OpenAsync(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error(Tr.T("shell.project.createFailed", ex.Message), ex);
            ShowBanner(new Banner(Tr.T("shell.project.createFailed", ex.Message)));
            return null;
        }
    }

    /// <summary>Adds another empty document to the open project and opens it.</summary>
    public async Task<IDocument?> NewSheetAsync(string? path = null)
    {
        if (CreatableType is not { } type)
        {
            Log.Warn(Tr.T("shell.project.noCreator"));
            return null;
        }

        path ??= PickNewFile is { } pick
            ? await pick(Tr.T("shell.project.newSheetName"), type.Extensions[0], "command.file.newSheet", ProjectDirectory)
            : null;

        if (path is null)
        {
            return null;
        }

        try
        {
            await CreateFilesAsync(type, Path.GetFullPath(path));
            ProjectChanged?.Invoke();
            return await OpenAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error(Tr.T("shell.project.createFailed", ex.Message), ex);
            ShowBanner(new Banner(Tr.T("shell.project.createFailed", ex.Message)));
            return null;
        }
    }

    // Stage plugin output before publishing either file. Moves never overwrite existing files, even if
    // another process creates a target while the plugin is working. Roll back only files we published.
    private static async Task CreateFilesAsync(IDocumentType type, string document, string? project = null, string? projectText = null)
    {
        foreach (string target in project is null ? new[] { document } : new[] { document, project })
        {
            if (File.Exists(target) || Directory.Exists(target))
            {
                throw new IOException(Tr.T("shell.project.exists", target));
            }
        }

        string staging = Path.Combine(Path.GetDirectoryName(document)!, ".anode-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool publishedProject = false;
        try
        {
            string stagedDocument = Path.Combine(staging, Path.GetFileName(document));
            await type.CreateAsync(stagedDocument, CancellationToken.None);
            if (project is not null)
            {
                string stagedProject = Path.Combine(staging, Path.GetFileName(project));
                await File.WriteAllTextAsync(stagedProject, projectText);
                File.Move(stagedProject, project);
                publishedProject = true;
            }

            File.Move(stagedDocument, document);
        }
        catch
        {
            if (publishedProject)
            {
                File.Delete(project!);
            }

            throw;
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Switches the workbench to a project folder: the tree, the title chip and the branch follow it.</summary>
    public void OpenProject(string directory)
    {
        ProjectDirectory = directory;
        string? project = SafeProjectFile(directory);
        ProjectName = Path.GetFileNameWithoutExtension(project ?? directory);
        ProjectBranch = GitBranch.Of(directory);
        ProjectChanged?.Invoke();
    }

    /// <summary>Opens a file at one of its appearances; see <see cref="IWorkbench.OpenAsync(string, string?)"/>.</summary>
    public async Task<IDocument?> OpenAsync(string path, string? instance)
    {
        var document = await OpenAsync(path);
        if (document is not null && instance is not null)
        {
            document.ShowInstance(instance);
        }

        return document;
    }

    public async Task<IDocument?> OpenAsync(string path)
    {
        string full = Path.GetFullPath(path);
        var existing = Panes.SelectMany(p => p.Tabs).FirstOrDefault(t => t.Document.FilePath is { } file && FilePaths.Equals(Path.GetFullPath(file), full));
        if (existing is not null)
        {
            ActivateTab(existing);
            return existing.Document;
        }

        if (_opening.TryGetValue(full, out var pending))
        {
            return await pending;
        }

        if (_savingPaths.Contains(full))
        {
            ShowBanner(new Banner(Tr.T("shell.banner.pathBusy", full)));
            return null;
        }

        string name = Path.GetFileName(full);
        if (DocumentTypes.FindFor(full) is not { } type)
        {
            Log.Warn(Tr.T("shell.log.noPlugin", Path.GetExtension(full), name));
            ShowBanner(new Banner(Tr.T("shell.banner.noPlugin", Path.GetExtension(full)), IsAlert: false));
            return null;
        }

        var completion = new TaskCompletionSource<IDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _opening.Add(full, completion.Task);
        IsBusy = true;
        IDocument? opened = null;
        try
        {
            var document = await type.OpenAsync(full, CancellationToken.None);
            AddDocument(document);
            SetProject(full);
            Replace(RecentProjects, _recents.Touch(full, DateTime.Now));
            Log.Info(Tr.T("shell.log.opened", name, type.Label));
            opened = document;
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
            _opening.Remove(full);
            IsBusy = _opening.Count > 0;
            completion.SetResult(opened);
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
        if (!_savingDocuments.Add(document))
        {
            ShowBanner(new Banner(Tr.T("shell.banner.pathBusy", document.FilePath ?? document.Title)));
            return false;
        }

        string? reserved = null;
        try
        {
            string? path = null;
            if (askForPath || document.FilePath is null)
            {
                if (PickSavePath is null || await PickSavePath(document) is not { } picked)
                {
                    return false;
                }

                path = Path.GetFullPath(picked);
            }

            string target = Path.GetFullPath(path ?? document.FilePath!);
            bool conflict = Documents.Any(d => !ReferenceEquals(d, document) && d.FilePath is { } file
                && FilePaths.Equals(Path.GetFullPath(file), target));
            if (conflict || _opening.ContainsKey(target) || !_savingPaths.Add(target))
            {
                ShowBanner(new Banner(Tr.T("shell.banner.pathBusy", target)));
                return false;
            }

            reserved = target;
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
        finally
        {
            if (reserved is not null)
            {
                _savingPaths.Remove(reserved);
            }

            _savingDocuments.Remove(document);
        }
    }

    /// <summary>
    /// Pins or unpins a tab. A pinned tab moves to the front of its pane and keeps its place, which is what makes
    /// pinning worth anything: the tab you keep coming back to stops drifting and cannot be closed by a stray click.
    /// </summary>
    public void SetPinned(DocumentTabViewModel tab, bool pinned)
    {
        tab.IsPinned = pinned;

        var tabs = tab.Pane.Tabs;
        int index = tabs.IndexOf(tab);
        int target = tabs.Count(t => t.IsPinned && !ReferenceEquals(t, tab));
        if (index >= 0 && target < tabs.Count && index != target)
        {
            tabs.Move(index, target);
        }
    }

    /// <summary>Closes every other tab of the pane, keeping the pinned ones.</summary>
    public async Task CloseOthersAsync(DocumentTabViewModel keep)
    {
        foreach (var tab in keep.Pane.Tabs.Where(t => !ReferenceEquals(t, keep) && !t.IsPinned).ToList())
        {
            await CloseTabAsync(tab);
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

    /// <summary>
    /// Hands a panel's view to <paramref name="host"/> and takes it off whoever held it before. One view, one host:
    /// a container that has been replaced must not be able to put the view back and give it a second parent.
    /// </summary>
    public Control ClaimPanel(PanelDescriptor descriptor, Controls.PanelHost host)
    {
        if (!_panelContent.TryGetValue(descriptor.Id, out var control))
        {
            control = descriptor.CreateContent(this);
            _panelContent[descriptor.Id] = control;
        }

        if (!_liveHosts.Contains(host))
        {
            _liveHosts.Add(host);
        }

        if (_panelHosts.TryGetValue(descriptor.Id, out var previous) && !ReferenceEquals(previous, host))
        {
            previous.Revoke();
        }

        _panelHosts[descriptor.Id] = host;
        return control;
    }

    /// <summary>True while some host shows this panel: a host that lost it waits for the place to be free again.</summary>
    public bool IsPanelShown(string panelId) => _panelHosts.ContainsKey(panelId);

    /// <summary>A host leaving the tree gives up its claims; a panel freed this way goes back to a host that wants it.</summary>
    public void ReleasePanels(Controls.PanelHost host)
    {
        foreach (string id in _panelHosts.Where(p => ReferenceEquals(p.Value, host)).Select(p => p.Key).ToList())
        {
            _panelHosts.Remove(id);
        }

        _liveHosts.Remove(host);
        foreach (var other in _liveHosts.ToList())
        {
            other.Reclaim();
        }
    }

    public void RememberStack(string key, string? activeId, bool collapsed) => _stackState[key] = (activeId, collapsed);

    public void SendToRail(string panelId)
    {
        _sentToRail.Add(panelId);
        Relayout();
    }

    public void PinFromRail(string panelId)
    {
        CloseSlideOver();
        _sentToRail.Remove(panelId);
        Relayout();
    }

    /// <summary>All sections of the frame, in reading order.</summary>
    public IEnumerable<DockStackViewModel> Stacks =>
        LeftStacks.Concat(RightStacks).Concat(BottomStack is { } bottom ? [bottom] : Array.Empty<DockStackViewModel>());

    /// <summary>
    /// The rail icon is a switch, not a button: it brings its panel to the front of its section, and pressing it
    /// again — on the panel already on show — closes the section. A panel with no dock of its own slides over the
    /// canvas instead, and closes the same way.
    /// </summary>
    public void ShowPanel(string panelId)
    {
        foreach (var stack in Stacks)
        {
            if (stack.Tabs.FirstOrDefault(t => t.Descriptor.Id == panelId) is { } tab)
            {
                CloseSlideOver();
                if (!stack.IsCollapsed && ReferenceEquals(stack.ActiveTab, tab))
                {
                    stack.IsCollapsed = true;
                }
                else
                {
                    stack.Select(tab);
                }

                RefreshRailState();
                return;
            }
        }

        if (Panels.Panels.FirstOrDefault(p => p.Id == panelId) is { } descriptor)
        {
            ToggleSlideOver(descriptor);
        }
    }

    /// <summary>A section opened or closed: the columns of the frame follow it.</summary>
    public void RaiseDockVisibility()
    {
        OnPropertyChanged(nameof(IsLeftDockShown));
        OnPropertyChanged(nameof(IsRightDockShown));
        OnPropertyChanged(nameof(IsBottomDockShown));
        OnPropertyChanged(nameof(LeftColumnWidth));
        OnPropertyChanged(nameof(RightColumnWidth));
        OnPropertyChanged(nameof(BottomRowHeight));
    }

    /// <summary>Marks the rail icons whose panels are the ones currently on show in their stack.</summary>
    public void RefreshRailState()
    {
        foreach (var item in Rail)
        {
            item.IsActive = Stacks.Any(s => !s.IsCollapsed && s.ActiveTab?.Descriptor.Id == item.Descriptor.Id);
        }
    }

    public void ToggleSlideOver(PanelDescriptor descriptor)
    {
        if (SlideOver?.Id == descriptor.Id)
        {
            CloseSlideOver();
            return;
        }

        SlideOver = descriptor;
        foreach (var item in Rail)
        {
            item.IsOpen = item.Descriptor.Id == descriptor.Id;
        }
    }

    public void CloseSlideOver()
    {
        SlideOver = null;
        foreach (var item in Rail)
        {
            item.IsOpen = false;
        }
    }

    public void Relayout()
    {
        string? documentType = ActiveDocument?.DocumentTypeId;
        var applicable = Panels.Panels.Where(p => p.AppliesTo(documentType)).ToList();
        var layout = DockPlanner.Plan(applicable, _sentToRail);

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

        // Each rail lists the panels of its own side — docked or not — so a closed section is one click from coming
        // back. The bottom dock has no rail of its own, so its icons join the foot of the left one.
        var docked = layout.Left.Concat(layout.Right).Concat(layout.Bottom is { } b ? [b] : Array.Empty<DockStack>())
            .SelectMany(s => s.Panels).ToHashSet();

        LeftRailTop.Clear();
        LeftRailBottom.Clear();
        BottomRail.Clear();
        RightRailTop.Clear();
        RightRailBottom.Clear();

        foreach (var panel in applicable)
        {
            var rail = panel.Area switch
            {
                DockArea.LeftTop => LeftRailTop,
                DockArea.LeftBottom => LeftRailBottom,
                DockArea.RightTop => RightRailTop,
                DockArea.RightBottom => RightRailBottom,
                _ => BottomRail,
            };

            rail.Add(new RailItemViewModel(panel, this)
            {
                IsOpen = SlideOver?.Id == panel.Id,
                IsDocked = docked.Contains(panel),
            });
        }

        RefreshRailState();

        OnPropertyChanged(nameof(HasRail));
        OnPropertyChanged(nameof(HasRightRail));
        OnPropertyChanged(nameof(HasRailDivider));
        RaiseDockVisibility();
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
        RaiseHistoryState();
        foreach (var pane in Panes)
        {
            pane.RaiseTools();
        }

        ActiveDocumentStateChanged?.Invoke();
    }

    private void UpdateWindowTitle() =>
        WindowTitle = ActiveDocument is { } doc ? $"{doc.Title}{(doc.IsDirty ? " •" : string.Empty)} — Kicad·One" : "Anode";

    /// <summary>
    /// A KiCad project file with only what names the project. KiCad fills in every setting it does not find, and we
    /// never read this file ourselves — here the folder is what makes a project.
    /// </summary>
    private static string ProjectFile(string name) =>
        "{\n" +
        "  \"meta\": {\n" +
        $"    \"filename\": \"{name}.kicad_pro\",\n" +
        "    \"version\": 3\n" +
        "  },\n" +
        "  \"sheets\": [],\n" +
        "  \"text_variables\": {}\n" +
        "}\n";

    private static string? SafeProjectFile(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.kicad_pro").FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
        ProjectBranch = GitBranch.Of(directory);
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

        public IProjectStructureRegistry Project => shell.ProjectStructure;

        public string DataDirectory => shell.DataDirectory;

        public ILog Log => shell.Log;
    }

    /// <summary>
    /// Where plugins keep their per-user files. The application's own folder in normal use; a test points it
    /// somewhere of its own, so that running the suite neither reads nor writes what the person using this machine
    /// has saved.
    /// </summary>
    public string DataDirectory { get; set; } = AppPaths.DataDirectory;

    public IPluginContext CreatePluginContext(PluginManifest manifest) => new PluginContext(manifest, this);
}
