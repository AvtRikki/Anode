using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

// Shapes.Path and System.IO.Path collide under implicit usings.
using GlyphPath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Views;

public partial class MainWindow : Window
{
    private ShellViewModel? _shell;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PaletteInput.AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);

        // Icons are drawn, not resources, so the ones outside data templates are set here.
        RailMore.Content = Icons.Draw(Icons.Plus, 16);
        SearchIcon.Content = Icons.Draw(Icons.Search, 13);
        ProjectChevron.Content = Icons.Draw(Icons.ChevronDown, 10);
        BranchIcon.Content = Icons.Draw(Icons.Branch, 12);
        ToggleLeftDock.Content = Icons.Draw(Icons.DockLeft, 15);
        ToggleRightDock.Content = Icons.Draw(Icons.DockRight, 15);
        ToggleBottomDock.Content = Icons.Draw(Icons.DockBottom, 15);

        SetUpWindowButtons();


        // macOS reveals the marks when the pointer is over the group, not over one light. A class carries that, so
        // it can be asserted in a test rather than only seen.
        WindowButtons.PointerEntered += (_, _) => WindowButtons.Classes.Set("hover", true);
        WindowButtons.PointerExited += (_, _) => WindowButtons.Classes.Set("hover", false);

        // Out of focus the lights go grey, the way macOS greys its own.
        this.GetObservable(IsActiveProperty).Subscribe(new AnonymousObserver<bool>(active => WindowButtons.Classes.Set("inactive", !active)));

        LeftSplitter.DragCompleted += (_, _) => StoreDockSizes();
        RightSplitter.DragCompleted += (_, _) => StoreDockSizes();
        BottomSplitter.DragCompleted += (_, _) => StoreDockSizes();
        Closing += OnClosing;

        // The band changes with full screen and between displays, so follow it rather than reading it once.

    }

    /// <summary>
    /// Who draws the window buttons. macOS keeps its own in a band of its own height and lays them out with
    /// constraints, so they cannot be centred in a taller bar — the window drops the system title bar and the
    /// workbench draws the three lights itself, which puts them on the same line as everything else by construction.
    /// Elsewhere the system title bar is left alone and sits above our content.
    /// </summary>
    private void SetUpWindowButtons()
    {
        if (!OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = false;
            return;
        }

        WindowDecorations = WindowDecorations.BorderOnly;
        WindowButtons.IsVisible = true;

        // The marks the system draws: a cross, a bar, and since Big Sur two triangles for zoom. Their opacity is
        // left to the theme — a local value would outrank the style that reveals them under the pointer.
        Glyph(0, "M4 4 L8 8 M8 4 L4 8", filled: false);
        Glyph(1, "M3.2 6 H8.8", filled: false);
        Glyph(2, "M3.3 3.3 H6.5 L3.3 6.5 Z M8.7 8.7 H5.5 L8.7 5.5 Z", filled: true);

        void Glyph(int index, string data, bool filled)
        {
            var ink = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
            ((Button)WindowButtons.Children[index]).Content = new GlyphPath
            {
                Data = Geometry.Parse(data),
                Fill = filled ? ink : null,
                Stroke = filled ? null : ink,
                StrokeThickness = filled ? 0 : 1.15,
                StrokeLineCap = PenLineCap.Round,
                Width = 12,
                Height = 12,
            };
        }
    }

    private void OnWindowClose(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowMinimise(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnWindowZoom(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellChanged;
            _shell.ConfirmUnsaved = null;
            _shell.PickFileToOpen = null;
            _shell.PickSavePath = null;
        }

        _shell = DataContext as ShellViewModel;
        if (_shell is not null)
        {
            _shell.PropertyChanged += OnShellChanged;
            _shell.ConfirmUnsaved = ConfirmUnsavedAsync;
            _shell.PickFileToOpen = PickFileToOpenAsync;
            _shell.PickSavePath = PickSavePathAsync;
            ApplyDockSizes();
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.LeftColumnWidth) or nameof(ShellViewModel.RightColumnWidth) or nameof(ShellViewModel.BottomRowHeight):
                ApplyDockSizes();
                break;
            case nameof(ShellViewModel.IsPaletteOpen) when _shell!.IsPaletteOpen:
                Dispatcher.UIThread.Post(() => PaletteInput.Focus(), DispatcherPriority.Loaded);
                break;
            case nameof(ShellViewModel.IsPaletteOpen):
                Focus();
                break;
        }
    }

    // ——— Dock sizes: column definitions are not bindable, so they are synced here ———

    private void ApplyDockSizes()
    {
        if (_shell is null)
        {
            return;
        }

        SetColumn(Body.ColumnDefinitions[1], _shell.LeftColumnWidth, ShellViewModel.MinLeftDock, ShellViewModel.MaxLeftDock);
        SetColumn(Body.ColumnDefinitions[5], _shell.RightColumnWidth, ShellViewModel.MinRightDock, ShellViewModel.MaxRightDock);

        var bottom = Center.RowDefinitions[2];
        bool shown = _shell.BottomRowHeight.Value > 0;
        bottom.MinHeight = shown ? 96 : 0;
        bottom.MaxHeight = shown ? 420 : 0;
        bottom.Height = _shell.BottomRowHeight;
        Center.RowDefinitions[1].Height = new GridLength(shown ? 1 : 0);
        Body.ColumnDefinitions[2].Width = new GridLength(_shell.IsLeftDockShown ? 1 : 0);
        Body.ColumnDefinitions[4].Width = new GridLength(_shell.IsRightDockShown ? 1 : 0);

        static void SetColumn(ColumnDefinition column, GridLength width, double min, double max)
        {
            bool visible = width.Value > 0;
            column.MinWidth = visible ? min : 0;
            column.MaxWidth = visible ? max : 0;
            column.Width = width;
        }
    }

    private void StoreDockSizes()
    {
        if (_shell is null)
        {
            return;
        }

        if (_shell.IsLeftDockShown)
        {
            _shell.LeftColumnWidth = new GridLength(Body.ColumnDefinitions[1].ActualWidth);
        }

        if (_shell.IsRightDockShown)
        {
            _shell.RightColumnWidth = new GridLength(Body.ColumnDefinitions[5].ActualWidth);
        }

        if (_shell.IsBottomDockShown)
        {
            _shell.BottomRowHeight = new GridLength(Center.RowDefinitions[2].ActualHeight);
        }

        // Splitters turn the centre into pixels; keep it the one star-sized track.
        Body.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
        Center.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        ApplyDockSizes();
    }

    // ——— Keyboard: every command gesture, Esc closes overlays ———

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (_shell.IsPaletteOpen)
            {
                _shell.IsPaletteOpen = false;
                e.Handled = true;
            }
            else if (_shell.SlideOver is not null)
            {
                _shell.CloseSlideOver();
                e.Handled = true;
            }

            return;
        }

        if (_shell.IsPaletteOpen)
        {
            return;
        }

        // Plain-letter shortcuts belong to the text field when one has focus.
        bool typing = FocusManager?.GetFocusedElement() is TextBox;
        foreach (var command in _shell.Commands.Commands.Reverse())
        {
            if (command.Gesture is not { } gesture || !gesture.Matches(e))
            {
                continue;
            }

            if (typing && (gesture.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
            {
                return;
            }

            if (command.CanExecute())
            {
                command.Execute();
                e.Handled = true;
            }

            return;
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _shell.Palette.Move(1);
                break;
            case Key.Up:
                _shell.Palette.Move(-1);
                break;
            case Key.Enter:
                _shell.ExecutePaletteSelection();
                break;
            default:
                return;
        }

        if (_shell.Palette.Selected is { } selected)
        {
            PaletteList.ScrollIntoView(selected);
        }

        e.Handled = true;
    }

    // ——— Title bar ———

    /// <summary>Project switcher: recent boards, plus the few things that have no other home yet.</summary>
    private void OnProjectClick(object? sender, RoutedEventArgs e)
    {
        if (_shell is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var project in _shell.RecentProjects.Take(8))
        {
            var item = new MenuItem { Header = $"{project.Name}  ·  {Path.GetFileName(project.Path)}", IsEnabled = File.Exists(project.Path) };
            string path = project.Path;
            item.Click += (_, _) => _ = _shell.OpenAsync(path);
            flyout.Items.Add(item);
        }

        if (flyout.Items.Count > 0)
        {
            flyout.Items.Add(new Separator());
        }

        foreach (string id in (string[])["file.open", "view.start"])
        {
            if (_shell.Commands.Find(id) is { } command)
            {
                var item = new MenuItem { Header = command.Title, InputGesture = command.Gesture };
                item.Click += (_, _) => _shell.Commands.TryExecute(command.Id);
                flyout.Items.Add(item);
            }
        }

        flyout.Items.Add(new Separator());
        foreach (var command in _shell.Commands.Commands.Where(c => c.Id.StartsWith("view.language.", StringComparison.Ordinal)))
        {
            var item = new MenuItem { Header = command.Title, IsEnabled = command.CanExecute() };
            var target = command;
            item.Click += (_, _) => target.Execute();
            flyout.Items.Add(item);
        }

        flyout.ShowAt(ProjectButton);
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e) => _shell?.OpenPalette();

    /// <summary>The dock toggles run the same commands as ⌥1, ⌥2 and ⌥3.</summary>
    private void OnToggleDock(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            _shell?.Commands.TryExecute(id);
        }
    }

    /// <summary>Dragging the workbench title bar moves the window, as the native one would.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is Visual source
            && source.FindAncestorOfType<Button>() is null && source.FindAncestorOfType<Menu>() is null)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is null)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    // ——— Documents ———

    private void OnPanePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_shell is not null && sender is Control { DataContext: DocumentPaneViewModel { ActiveTab: { } tab } pane } && !pane.IsFocused)
        {
            _shell.ActivateTab(tab);
        }
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && sender is Control { DataContext: DocumentTabViewModel tab } && _shell is not null)
        {
            _ = _shell.CloseTabAsync(tab);
            e.Handled = true;
        }
    }

    // ——— Rail, slide-over, banner, palette ———

    private void OnRailDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: RailItemViewModel item })
        {
            item.PinCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSlideOverPin(object? sender, RoutedEventArgs e)
    {
        if (_shell?.SlideOver is { } panel)
        {
            _shell.PinFromRail(panel.Id);
        }
    }

    private void OnBannerAction(object? sender, RoutedEventArgs e)
    {
        if (_shell?.CurrentBanner is { } banner)
        {
            banner.Action?.Invoke();
        }
    }

    private void OnBannerDismiss(object? sender, RoutedEventArgs e)
    {
        if (_shell?.CurrentBanner is { } banner)
        {
            _shell.DismissBanner(banner);
        }
    }

    private void OnPaletteBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.IsPaletteOpen = false;
        }
    }

    private void OnPalettePanelPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e)
    {
        if (_shell is not null && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>() is not null)
        {
            _shell.ExecutePaletteSelection();
        }
    }

    // ——— Files ———

    private async Task<string?> PickFileToOpenAsync()
    {
        if (_shell is null)
        {
            return null;
        }

        List<FilePickerFileType> filters = [.. _shell.DocumentTypes.Types.Select(t => new FilePickerFileType(t.Label)
        {
            Patterns = [.. t.Extensions.Select(x => "*" + x)],
            AppleUniformTypeIdentifiers = ["public.data"],
        })];
        filters.Add(FilePickerFileTypes.All);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.T("command.file.open"),
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSavePathAsync(IDocument document)
    {
        string? extension = Path.GetExtension(document.FilePath);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.T("command.file.saveAs"),
            SuggestedFileName = document.FilePath is { } path ? Path.GetFileName(path) : document.Title,
            DefaultExtension = extension?.TrimStart('.'),
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    private Task<UnsavedChoice> ConfirmUnsavedAsync(IReadOnlyList<IDocument> documents) =>
        new UnsavedChangesDialog(documents).ShowDialog<UnsavedChoice>(this);

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFile() is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path && _shell is not null)
        {
            await _shell.OpenAsync(path);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || _shell is null)
        {
            return;
        }

        var dirty = _shell.Documents.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        switch (await ConfirmUnsavedAsync(dirty))
        {
            case UnsavedChoice.Save:
                foreach (var document in dirty)
                {
                    if (!await _shell.SaveAsync(document))
                    {
                        return;
                    }
                }

                break;
            case UnsavedChoice.Cancel:
                return;
        }

        _closeConfirmed = true;
        Close();
    }
}
