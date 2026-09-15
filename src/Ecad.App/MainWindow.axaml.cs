using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ecad.App.ViewModels;

namespace Ecad.App;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType KiCadBoard = new("KiCad board")
    {
        Patterns = ["*.kicad_pcb"],
        AppleUniformTypeIdentifiers = ["public.data"],
    };

    private MainViewModel? _viewModel;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += OnClosing;

        Canvas.CursorMoved += p =>
        {
            if (_viewModel is not null)
            {
                _viewModel.CursorText = p is { } v
                    ? string.Create(CultureInfo.InvariantCulture, $"X {v.X,10:0.000}  Y {v.Y,10:0.000} mm")
                    : string.Empty;
            }
        };

        Canvas.FrameRendered += ms =>
        {
            if (_viewModel is not null)
            {
                _viewModel.FrameText = string.Create(CultureInfo.InvariantCulture, $"{Canvas.BackendName} {ms,6:0.0} ms");
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.RedrawRequested -= Canvas.Redraw;
            _viewModel.FitRequested -= OnFitRequested;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.RedrawRequested += Canvas.Redraw;
            _viewModel.FitRequested += OnFitRequested;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _viewModel is null)
        {
            return;
        }

        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.O when command:
                OnOpenClick(this, e);
                break;
            case Key.S when command && shift:
                OnSaveAsClick(this, e);
                break;
            case Key.S when command:
                OnSaveClick(this, e);
                break;
            case Key.Z when command && shift:
            case Key.Y when command:
                _viewModel.Redo();
                break;
            case Key.Z when command:
                _viewModel.Undo();
                break;
            case Key.Home:
                Canvas.ZoomToFit();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnFitRequested() => Dispatcher.UIThread.Post(Canvas.ZoomToFit, DispatcherPriority.Loaded);

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open KiCad board",
            AllowMultiple = false,
            FileTypeFilter = [KiCadBoard, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && _viewModel is not null)
        {
            await _viewModel.OpenAsync(path);
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => _viewModel?.Save();

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilePath is not { } current)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save board as",
            SuggestedFileName = Path.GetFileName(current),
            DefaultExtension = "kicad_pcb",
            FileTypeChoices = [KiCadBoard],
            ShowOverwritePrompt = true,
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            _viewModel.SaveAs(path);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _viewModel?.Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => _viewModel?.Redo();

    private void OnRotateClick(object? sender, RoutedEventArgs e) => _viewModel?.Rotate(90);

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => _viewModel?.DeleteSelection();

    private void OnFitClick(object? sender, RoutedEventArgs e) => Canvas.ZoomToFit();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFile() is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path && _viewModel is not null && await ConfirmDiscardOrSaveAsync())
        {
            await _viewModel.OpenAsync(path);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || _viewModel is not { IsDirty: true })
        {
            return;
        }

        e.Cancel = true;
        if (await ConfirmDiscardOrSaveAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Returns true when it is fine to replace the current board: nothing unsaved, saved, or discarded.</summary>
    private async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (_viewModel is not { IsDirty: true } vm)
        {
            return true;
        }

        var choice = await new UnsavedChangesDialog(Path.GetFileName(vm.FilePath ?? "board")).ShowDialog<UnsavedChoice>(this);
        return choice switch
        {
            UnsavedChoice.Save => vm.Save(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }
}
