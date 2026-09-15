using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Ecad.KiCad;
using Ecad.Rendering;

namespace Ecad.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];

    public ObservableCollection<PropertyRow> Properties { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBoard))]
    public partial BoardScene? Scene { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Ecad";

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a .kicad_pcb file (⌘O) or drop it onto the window";

    [ObservableProperty]
    public partial string CursorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool FlipView { get; set; }

    [ObservableProperty]
    public partial bool HighlightNet { get; set; } = true;

    [ObservableProperty]
    public partial int SelectedOwner { get; set; } = -1;

    public bool HasBoard => Scene is not null;

    public string? FilePath { get; private set; }

    /// <summary>Raised when something outside bindings (layer visibility) needs a repaint.</summary>
    public event Action? RedrawRequested;

    public event Action? FitRequested;

    public async Task OpenAsync(string path)
    {
        IsBusy = true;
        Status = $"Loading {Path.GetFileName(path)}…";
        try
        {
            var sw = Stopwatch.StartNew();
            var (board, scene, loadMs, sceneMs) = await Task.Run(() =>
            {
                var b = Board.Load(path);
                double parsed = sw.Elapsed.TotalMilliseconds;
                var s = SceneBuilder.Build(b);
                if (AppOptions.Renderer == RendererKind.OpenGl)
                {
                    // GPU upload needs triangles; compute them here instead of stalling the first frame.
                    SceneTriangulator.Triangulate(s);
                }

                return (b, s, parsed, sw.Elapsed.TotalMilliseconds - parsed);
            });

            SelectedOwner = -1;
            Properties.Clear();
            FilePath = path;
            Scene = scene;
            Title = $"{Path.GetFileName(path)} — Ecad";

            Layers.Clear();
            foreach (var layer in scene.Layers.Reverse())
            {
                Layers.Add(new LayerItemViewModel(layer, DisplayName(board, layer.Name), () => RedrawRequested?.Invoke()));
            }

            string version = board.GeneratorVersion is { } gv ? $"KiCad {gv}" : $"format {board.Version}";
            string warning = !board.IsSupportedVersion ? " · ⚠ older than KiCad 8" : board.IsNewerThanKnown ? " · ⚠ newer than known format" : string.Empty;
            Status = $"{version} · {board.Footprints.Count:N0} footprints · {board.Segments.Count + board.Arcs.Count:N0} tracks · "
                     + $"{board.Vias.Count:N0} vias · {board.Zones.Count:N0} zones · {scene.PrimitiveCount:N0} primitives · "
                     + $"parse {loadMs:N0} ms, scene {sceneMs:N0} ms{warning}";

            FitRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Status = $"Failed to open {Path.GetFileName(path)}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SaveAs(string path)
    {
        if (Scene is null)
        {
            return;
        }

        try
        {
            Scene.Board.Save(path);
            Status = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Status = $"Failed to save: {ex.Message}";
        }
    }

    partial void OnSelectedOwnerChanged(int value)
    {
        Properties.Clear();
        if (Scene is null || value < 0)
        {
            return;
        }

        foreach (var row in ItemProperties.For(Scene.Owner(value)))
        {
            Properties.Add(row);
        }
    }

    private static string DisplayName(Board board, string layerName) => layerName switch
    {
        LayerStyle.PlatedHoles => "Plated holes",
        LayerStyle.NonPlatedHoles => "Non-plated holes",
        _ => board.Layers.Find(layerName)?.DisplayName ?? layerName,
    };
}
