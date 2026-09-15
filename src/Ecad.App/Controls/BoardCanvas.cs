using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ecad.Editor;
using Ecad.Geometry;
using Ecad.Rendering;

namespace Ecad.App.Controls;

/// <summary>
/// Interactive board view. Wheel zooms around the cursor; middle/right drag or Space+drag pans.
/// Left click selects (Shift toggles), dragging an item moves it, dragging empty space draws a selection box
/// (left-to-right encloses, right-to-left crosses). M moves the selection with the cursor, R rotates, Delete deletes.
/// Drawing is delegated to a Skia or OpenGL surface chosen by <see cref="AppOptions.Renderer"/>.
/// </summary>
public sealed class BoardCanvas : Panel
{
    public static readonly StyledProperty<BoardEditor?> EditorProperty =
        AvaloniaProperty.Register<BoardCanvas, BoardEditor?>(nameof(Editor));

    public static readonly StyledProperty<bool> FlipXProperty =
        AvaloniaProperty.Register<BoardCanvas, bool>(nameof(FlipX));

    public static readonly StyledProperty<bool> HighlightNetProperty =
        AvaloniaProperty.Register<BoardCanvas, bool>(nameof(HighlightNet), true);

    private const double DragSlopPixels = 4;

    private readonly Camera2D _camera = new();
    private readonly IBoardSurface _surface;
    private Gesture _gesture;
    private Point _pressPoint;
    private int _pressOwner = -1;
    private Point _lastPoint;
    private bool _spaceDown;
    private bool _fitPending;

    private enum Gesture
    {
        None,
        Pressed,
        Panning,
        Moving,
        MovingWithCursor,
        BoxSelecting,
    }

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

    public BoardEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public bool FlipX
    {
        get => GetValue(FlipXProperty);
        set => SetValue(FlipXProperty, value);
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
        if (Editor?.Scene is not { } scene)
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

        if (change.Property == EditorProperty)
        {
            if (change.OldValue is BoardEditor old)
            {
                old.SceneChanged -= Present;
                old.SelectionChanged -= Present;
            }

            _gesture = Gesture.None;
            if (Editor is { } editor)
            {
                editor.SceneChanged += Present;
                editor.SelectionChanged += Present;
            }

            _surface.Scene = Editor?.Scene;
            ZoomToFit();
            Present();
        }
        else if (change.Property == FlipXProperty || change.Property == HighlightNetProperty)
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

        UpdateMoveToCursor(p);
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
            if (_gesture is Gesture.None)
            {
                _gesture = Gesture.Panning;
                e.Pointer.Capture(this);
            }
        }
        else if (props.IsLeftButtonPressed && Editor is { } editor)
        {
            if (_gesture == Gesture.MovingWithCursor)
            {
                editor.CommitMove();
                _gesture = Gesture.None;
            }
            else
            {
                _gesture = Gesture.Pressed;
                _pressPoint = point.Position;
                _pressOwner = Pick(point.Position);
                e.Pointer.Capture(this);
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var editor = Editor;

        switch (_gesture)
        {
            case Gesture.Panning:
                _camera.PanPixels(p.X - _lastPoint.X, p.Y - _lastPoint.Y);
                Present();
                break;

            case Gesture.Pressed when editor is not null && Distance(p, _pressPoint) > DragSlopPixels:
                bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _gesture = Gesture.BoxSelecting;
                if (editor.Scene.IsLive(_pressOwner))
                {
                    var grabbed = editor.Scene.TopLevelOf(_pressOwner);
                    if (!editor.IsSelected(grabbed))
                    {
                        editor.Click(_pressOwner, toggle);
                    }

                    if (editor.BeginMove(grabbed, World(_pressPoint)))
                    {
                        _gesture = Gesture.Moving;
                        editor.UpdateMove(World(p));
                    }
                }

                Present();
                break;

            case Gesture.Moving or Gesture.MovingWithCursor:
                UpdateMoveToCursor(p);
                Present();
                break;

            case Gesture.BoxSelecting:
                Present();
                break;
        }

        _lastPoint = p;

        if (editor is not null)
        {
            var board = editor.Scene.ToBoardNm(World(p));
            CursorMoved?.Invoke(new Vector2D(board.X / Units.NmPerMm, board.Y / Units.NmPerMm));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        var editor = Editor;
        bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (_gesture)
        {
            case Gesture.Panning:
                _gesture = Gesture.None;
                break;

            case Gesture.Pressed:
                _gesture = Gesture.None;
                editor?.Click(_pressOwner, toggle);
                break;

            case Gesture.Moving:
                _gesture = Gesture.None;
                editor?.CommitMove();
                break;

            case Gesture.BoxSelecting:
                _gesture = Gesture.None;
                editor?.SelectInBox(SelectionBox(p), crossing: IsCrossing(p), toggle);
                break;

            default:
                return;
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
        var editor = Editor;
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Space:
                _spaceDown = true;
                break;

            case Key.Home:
                ZoomToFit();
                break;

            case Key.Escape:
                if (editor?.Move is not null)
                {
                    editor.CancelMove();
                }
                else if (_gesture != Gesture.BoxSelecting)
                {
                    editor?.SetSelection([]);
                }

                _gesture = Gesture.None;
                Present();
                break;

            case Key.M when !command && editor is not null && _gesture == Gesture.None && editor.Selection.Count > 0:
                if (editor.BeginMove(null, World(_lastPoint)))
                {
                    _gesture = Gesture.MovingWithCursor;
                    UpdateMoveToCursor(_lastPoint);
                }

                break;

            case Key.R when !command && editor is not null:
                editor.Rotate(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -90 : 90);
                Present();
                break;

            case Key.Delete or Key.Back when editor is not null && _gesture == Gesture.None:
                editor.DeleteSelection();
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

    private int Pick(Point screen)
    {
        if (Editor?.Scene is not { } scene)
        {
            return -1;
        }

        var world = World(screen);
        float tolerance = (float)(DragSlopPixels / _camera.PixelsPerMm);
        return HitTester.Pick(scene, new Vector2((float)world.X, (float)world.Y), tolerance);
    }

    private void UpdateMoveToCursor(Point screen)
    {
        if (_gesture is Gesture.Moving or Gesture.MovingWithCursor)
        {
            Editor?.UpdateMove(World(screen));
        }
    }

    private Vector2D World(Point screen)
    {
        SyncViewport();
        return _camera.ScreenToWorld(new Vector2D(screen.X, screen.Y));
    }

    private RectD SelectionBox(Point current)
    {
        var a = World(_pressPoint);
        var b = World(current);
        return new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    /// <summary>Right-to-left on screen means crossing selection, regardless of bottom view mirroring.</summary>
    private bool IsCrossing(Point current) => current.X < _pressPoint.X;

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void SyncViewport()
    {
        _camera.ViewportWidth = Math.Max(Bounds.Width, 1);
        _camera.ViewportHeight = Math.Max(Bounds.Height, 1);
        _camera.FlipX = FlipX;
    }

    private void Present()
    {
        if (Editor is not { } editor)
        {
            _surface.Present(null);
            return;
        }

        SyncViewport();
        var move = editor.Move;
        var view = new ViewState(
            _camera.WorldToScreenTransform,
            _camera.PixelsPerMm,
            _camera.ViewportWidth,
            _camera.ViewportHeight,
            _camera.FlipX,
            editor.SelectedOwners,
            HighlightNet ? editor.FocusNet : null,
            RenderScaling: TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)
        {
            Preview = move?.Preview,
            PreviewTransform = move?.PreviewTransform ?? Transform2D.Identity,
            SelectionBox = _gesture == Gesture.BoxSelecting ? SelectionBox(_lastPoint) : null,
            SelectionBoxCrossing = _gesture == Gesture.BoxSelecting && IsCrossing(_lastPoint),
        };

        _surface.Present(view);
    }
}
