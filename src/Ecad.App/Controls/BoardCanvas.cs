using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Ecad.Geometry;
using Ecad.Rendering;

namespace Ecad.App.Controls;

/// <summary>
/// Interactive board view: wheel zoom around the cursor, middle/right-drag or space-drag pan, click to select.
/// Drawing is delegated to a Skia or OpenGL surface chosen by <see cref="AppOptions.Renderer"/>.
/// </summary>
public sealed class BoardCanvas : Panel
{
    public static readonly StyledProperty<BoardScene?> SceneProperty =
        AvaloniaProperty.Register<BoardCanvas, BoardScene?>(nameof(Scene));

    public static readonly StyledProperty<bool> FlipXProperty =
        AvaloniaProperty.Register<BoardCanvas, bool>(nameof(FlipX));

    public static readonly StyledProperty<int> SelectedOwnerProperty =
        AvaloniaProperty.Register<BoardCanvas, int>(nameof(SelectedOwner), -1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> HighlightNetProperty =
        AvaloniaProperty.Register<BoardCanvas, bool>(nameof(HighlightNet), true);

    private const double ClickSlopPixels = 4;

    private readonly Camera2D _camera = new();
    private readonly IBoardSurface _surface;
    private Point? _pressPoint;
    private Point _lastPoint;
    private bool _panning;
    private bool _spaceDown;
    private bool _fitPending;

    static BoardCanvas()
    {
        FocusableProperty.OverrideDefaultValue<BoardCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<BoardCanvas>(true);
    }

    public BoardCanvas()
    {
        // A background makes the whole panel hit-testable for pointer input.
        Background = Brushes.Transparent;

        _surface = AppOptions.Renderer == RendererKind.OpenGl ? new GlBoardSurface() : new SkiaBoardSurface();
        var control = (Control)_surface;
        control.IsHitTestVisible = false;
        Children.Add(control);
        _surface.FrameRendered += ms => FrameRendered?.Invoke(ms);
    }

    public BoardScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public bool FlipX
    {
        get => GetValue(FlipXProperty);
        set => SetValue(FlipXProperty, value);
    }

    public int SelectedOwner
    {
        get => GetValue(SelectedOwnerProperty);
        set => SetValue(SelectedOwnerProperty, value);
    }

    public bool HighlightNet
    {
        get => GetValue(HighlightNetProperty);
        set => SetValue(HighlightNetProperty, value);
    }

    public string BackendName => _surface.BackendName;

    /// <summary>Cursor position in board millimetres; null when the pointer leaves.</summary>
    public event Action<Vector2D?>? CursorMoved;

    /// <summary>Render time of the last frame in milliseconds (raised on the UI thread).</summary>
    public event Action<double>? FrameRendered;

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
            _surface.Scene = Scene;
            ZoomToFit();
            Present();
        }
        else if (change.Property == FlipXProperty || change.Property == SelectedOwnerProperty || change.Property == HighlightNetProperty)
        {
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
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        _lastPoint = point.Position;
        var props = point.Properties;

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
        {
            _panning = true;
            e.Pointer.Capture(this);
        }
        else if (props.IsLeftButtonPressed)
        {
            _pressPoint = point.Position;
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
            var world = _camera.ScreenToWorld(new Vector2D(p.X, p.Y));
            var board = scene.ToBoardNm(world);
            CursorMoved?.Invoke(new Vector2D(board.X / Units.NmPerMm, board.Y / Units.NmPerMm));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }
        else if (_pressPoint is { } press && Scene is { } scene
                 && Math.Abs(p.X - press.X) <= ClickSlopPixels && Math.Abs(p.Y - press.Y) <= ClickSlopPixels)
        {
            var world = _camera.ScreenToWorld(new Vector2D(p.X, p.Y));
            float tolerance = (float)(ClickSlopPixels / _camera.PixelsPerMm);
            SelectedOwner = HitTester.Pick(scene, new Vector2((float)world.X, (float)world.Y), tolerance);
        }

        _pressPoint = null;
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
                e.Handled = true;
                break;
            case Key.Home:
                ZoomToFit();
                e.Handled = true;
                break;
            case Key.Escape:
                SelectedOwner = -1;
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
        }
    }

    private void SyncViewport()
    {
        _camera.ViewportWidth = Math.Max(Bounds.Width, 1);
        _camera.ViewportHeight = Math.Max(Bounds.Height, 1);
        _camera.FlipX = FlipX;
    }

    private void Present()
    {
        if (Scene is not { } scene)
        {
            _surface.Present(null);
            return;
        }

        SyncViewport();
        var view = new ViewState(
            _camera.WorldToScreenTransform,
            _camera.PixelsPerMm,
            _camera.ViewportWidth,
            _camera.ViewportHeight,
            _camera.FlipX,
            SelectedOwner,
            HighlightNet && SelectedOwner >= 0 ? scene.OwnerNet(SelectedOwner) : null,
            RenderScaling: TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

        _surface.Present(view);
    }
}
