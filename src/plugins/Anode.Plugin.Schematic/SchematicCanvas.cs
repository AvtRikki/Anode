using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Render;
using Anode.Render.Avalonia;

namespace Anode.Plugin.Schematic;

/// <summary>
/// Sheet view. Wheel zooms around the cursor; middle/right drag or Space+drag pans; left click selects one item
/// (Shift adds to the selection). Reading only for now — the schematic is not editable yet.
/// </summary>
public sealed class SchematicCanvas : Panel
{
    public static readonly StyledProperty<SchematicScene?> SceneProperty =
        AvaloniaProperty.Register<SchematicCanvas, SchematicScene?>(nameof(Scene));

    private const double DragSlopPixels = 4;

    private readonly Camera2D _camera = new();
    private readonly ISceneSurface _surface;
    private readonly HashSet<int> _selected = [];
    private Point _lastPoint;
    private Point _pressPoint;
    private bool _panning;
    private bool _spaceDown;
    private bool _fitPending;

    static SchematicCanvas()
    {
        FocusableProperty.OverrideDefaultValue<SchematicCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<SchematicCanvas>(true);
    }

    public SchematicCanvas()
    {
        // A background makes the whole panel hit-testable for pointer input.
        Background = Brushes.Transparent;

        _surface = SceneSurface.Create();
        var control = (Control)_surface;
        control.IsHitTestVisible = false;
        Children.Add(control);
        _surface.FrameRendered += ms => FrameRendered?.Invoke(ms);
    }

    public SchematicScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public string BackendName => _surface.BackendName;

    /// <summary>Zoom as a percentage of "one sheet millimetre is one pixel".</summary>
    public double ZoomPercent => _camera.PixelsPerMm * 100;

    /// <summary>The single selected item, or null.</summary>
    public SchItem? Selection { get; private set; }

    /// <summary>Cursor position in sheet millimetres; null when the pointer leaves.</summary>
    public event Action<Vector2D?>? CursorMoved;

    public event Action<double>? FrameRendered;

    public event Action? ViewChanged;

    public event Action? SelectionChanged;

    public void Redraw() => Present();

    public void ZoomToFit()
    {
        if (Scene is not { } scene)
        {
            return;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _fitPending = true;
            return;
        }

        SyncViewport();
        _camera.Fit(scene.BoardOutline.IsEmpty ? scene.Bounds : scene.BoardOutline);
        Present();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            _selected.Clear();
            Selection = null;
            _surface.Scene = Scene;
            ZoomToFit();
            Present();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitPending)
        {
            _fitPending = false;
            ZoomToFit();
        }
        else
        {
            Present();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _camera.PanPixels(e.Delta.Y * 40, 0);
        }
        else
        {
            _camera.ZoomAt(new Vector2D(p.X, p.Y), Math.Pow(1.2, e.Delta.Y));
        }

        Present();
        ViewChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        _lastPoint = point.Position;
        _pressPoint = point.Position;
        var props = point.Properties;

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
        {
            _panning = true;
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_panning)
        {
            _camera.PanPixels(p.X - _lastPoint.X, p.Y - _lastPoint.Y);
            Present();
        }

        _lastPoint = p;

        if (Scene is { } scene)
        {
            var sheet = scene.ToSheetNm(World(p));
            CursorMoved?.Invoke(new Vector2D(sheet.X / Units.NmPerMm, sheet.Y / Units.NmPerMm));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);

        if (_panning)
        {
            _panning = false;
        }
        else if (e.InitialPressMouseButton == MouseButton.Left && Distance(p, _pressPoint) <= DragSlopPixels)
        {
            Select(Pick(p), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }

        e.Pointer.Capture(null);
        Present();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CursorMoved?.Invoke(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Space:
                _spaceDown = true;
                break;
            case Key.Home:
                ZoomToFit();
                break;
            case Key.Escape:
                Select(-1, false);
                Present();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
        }
    }

    private void Select(int owner, bool add)
    {
        if (!add)
        {
            _selected.Clear();
        }

        if (owner >= 0 && Scene?.IsLive(owner) == true)
        {
            _selected.Add(owner);
            Selection = Scene.Owner(owner);
        }
        else if (_selected.Count == 0)
        {
            Selection = null;
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>Topmost primitive under the cursor, or -1. Decoration layers (the paper) are never picked.</summary>
    private int Pick(Point screen)
    {
        if (Scene is not { } scene)
        {
            return -1;
        }

        var world = World(screen);
        var point = new Vector2((float)world.X, (float)world.Y);
        float tolerance = (float)(DragSlopPixels / _camera.PixelsPerMm);

        for (int i = scene.Layers.Count - 1; i >= 0; i--)
        {
            var layer = scene.Layers[i];
            if (layer.IsDecoration || !layer.IsVisible || !layer.Bounds.Inflate(tolerance).Contains(point.X, point.Y))
            {
                continue;
            }

            foreach (var circle in layer.Circles)
            {
                if (Vector2.DistanceSquared(circle.Center, point) <= (circle.Radius + tolerance) * (circle.Radius + tolerance))
                {
                    return circle.Owner;
                }
            }

            foreach (var line in layer.Lines)
            {
                float reach = (line.Width / 2) + tolerance;
                if (HitTester.DistanceToSegmentSquared(point, line.A, line.B) <= reach * reach)
                {
                    return line.Owner;
                }
            }

            foreach (var polygon in layer.Polygons)
            {
                if (HitTester.Contains(polygon.Points, point))
                {
                    return polygon.Owner;
                }
            }
        }

        return -1;
    }

    private Vector2D World(Point screen)
    {
        SyncViewport();
        return _camera.ScreenToWorld(new Vector2D(screen.X, screen.Y));
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private void SyncViewport()
    {
        _camera.ViewportWidth = Math.Max(Bounds.Width, 1);
        _camera.ViewportHeight = Math.Max(Bounds.Height, 1);
    }

    private void Present()
    {
        if (Scene is null)
        {
            _surface.Present(null);
            return;
        }

        SyncViewport();
        _surface.Present(new ViewState(
            _camera.WorldToScreenTransform,
            _camera.PixelsPerMm,
            _camera.ViewportWidth,
            _camera.ViewportHeight,
            _camera.FlipX,
            _selected.Count > 0 ? new HashSet<int>(_selected) : null,
            null,
            RenderScaling: TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
    }
}
