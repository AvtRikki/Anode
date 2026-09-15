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

    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

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
                _viewModel.FrameText = string.Create(CultureInfo.InvariantCulture, $"{ms,6:0.0} ms");
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
        if (e.Handled)
        {
            return;
        }

        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (command && e.Key == Key.O)
        {
            OnOpenClick(this, e);
            e.Handled = true;
        }
        else if (command && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.S)
        {
            OnSaveAsClick(this, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            Canvas.ZoomToFit();
            e.Handled = true;
        }
    }

    private void OnFitRequested() => Dispatcher.UIThread.Post(Canvas.ZoomToFit, DispatcherPriority.Loaded);

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
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

    private void OnFitClick(object? sender, RoutedEventArgs e) => Canvas.ZoomToFit();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFile() is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path && _viewModel is not null)
        {
            await _viewModel.OpenAsync(path);
        }
    }
}
