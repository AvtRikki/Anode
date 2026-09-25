using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Anode.Editing;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;
using Anode.Render.Avalonia;

namespace Anode.Plugin.Schematic;

/// <summary>
/// Sheet view and editor. Wheel zooms around the cursor; middle/right drag or Space+drag pans; left click selects
/// (Shift adds), a drag on empty paper draws a selection box, a drag on the selection moves it. R turns, X and Y
/// mirror, Delete removes, Esc cancels.
/// </summary>
public sealed class SchematicCanvas : Panel
{
    public static readonly StyledProperty<SchematicEditor?> EditorProperty =
        AvaloniaProperty.Register<SchematicCanvas, SchematicEditor?>(nameof(Editor));

    private const double DragSlopPixels = 4;
    private const double PinSnapPixels = 10;

    private readonly Camera2D _camera = new();
    private readonly ISceneSurface _surface;
    private ISchTool? _tool;
    private IReadOnlySet<int>? _highlighted;
    private IReadOnlySet<int>? _lit;
    private (IReadOnlySet<int>? Selected, IReadOnlySet<int>? Net) _litFrom;
    private Gesture _gesture;
    private Point _lastPoint;
    private Point _pressPoint;
    private int _pressOwner = -1;
    private bool _spaceDown;

    /// <summary>
    /// The press was a double click that has already done its work — gone into a group, put a corner in or taken
    /// one out. Its release is not a click as well: that would select whatever is under it, or nothing.
    /// </summary>
    private bool _pressDone;
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

        // A part can be dragged in from the components panel and dropped where it belongs.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = SymbolDrag.Carried(e.DataTransfer) is not null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });

        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (SymbolDrag.Carried(e.DataTransfer) is { } part && PartDropped is { } drop)
            {
                e.DragEffects = drop(part, e.GetPosition(this)) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            }
        });

        // A right drag pans the sheet; the menu belongs to a right click that stayed put, and never to a tool run.
        ContextRequested += (_, e) =>
        {
            // While a tool is armed, a right click puts the pointer back instead of opening the menu — the way out
            // that a hand reaches for first, and the one Esc offers from the keyboard.
            if (_tool is not null)
            {
                CancelToolRun();
                ToolCancelled?.Invoke();
                e.Handled = true;
                return;
            }

            if (Distance(_lastPoint, _pressPoint) > DragSlopPixels)
            {
                e.Handled = true;
            }
        };
    }

    private enum Gesture
    {
        None,
        Panning,
        BoxSelecting,
        Moving,
        EditingPoint,
    }

    /// <summary>How big a handle is drawn, and how near the pointer has to be to take hold of it, in pixels.</summary>
    private const double HandlePixels = 4.5;
    private const double HandleReachPixels = 8;

    public SchematicEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public SchematicScene? Scene => Editor?.Scene;

    /// <summary>The tool the pointer drives; null means the pointer selects and moves.</summary>
    internal ISchTool? Tool
    {
        get => _tool;
        set
        {
            if (ReferenceEquals(_tool, value))
            {
                return;
            }

            if (_tool is { } previous)
            {
                previous.Changed -= Present;
                _ = previous.Cancel();
            }

            _tool = value;
            Cursor = _tool is WireTool ? new Cursor(StandardCursorType.Cross) : null;
            if (_tool is { } tool)
            {
                tool.Changed += Present;
            }

            Present();
        }
    }

    public string BackendName => _surface.BackendName;

    /// <summary>Zoom as a percentage of "one sheet millimetre is one pixel".</summary>
    public double ZoomPercent => _camera.PixelsPerMm * 100;

    /// <summary>Cursor position in sheet millimetres; null when the pointer leaves.</summary>
    public event Action<Vector2D?>? CursorMoved;

    public event Action<double>? FrameRendered;

    public event Action? ViewChanged;

    /// <summary>Esc left the tool; the document puts the pointer back to selecting.</summary>
    public event Action? ToolCancelled;

    /// <summary>A part was dropped on the sheet; the document is the one that knows how to put it down.</summary>
    public Func<SymbolChoice, Point, bool>? PartDropped { get; set; }

    /// <summary>The key that highlights the net of what is selected was pressed — backquote, as in KiCad.</summary>
    public event Action? HighlightNetRequested;

    /// <summary>Esc with nothing left to cancel: the highlighted net goes back to normal with the selection.</summary>
    public event Action? HighlightCleared;

    /// <summary>
    /// Owners drawn lit besides the selection: a highlighted net. The renderer dims everything else, which is how a
    /// net stands out in KiCad too. Pass a new set when it changes; the renderer caches by reference.
    /// </summary>
    public IReadOnlySet<int>? HighlightedOwners
    {
        get => _highlighted;
        set
        {
            _highlighted = value;
            Present();
        }
    }

    public void Redraw() => Present();

    /// <summary>
    /// The selection with the highlighted net added. Built only when either changes, so the renderer, which keys its
    /// highlight cache on the set it is handed, sees the same set from frame to frame.
    /// </summary>
    private IReadOnlySet<int> Lit(IReadOnlySet<int> selected)
    {
        if (_highlighted is not { Count: > 0 } net)
        {
            return selected;
        }

        if (!ReferenceEquals(selected, _litFrom.Selected) || !ReferenceEquals(net, _litFrom.Net) || _lit is null)
        {
            var lit = new HashSet<int>(selected);
            lit.UnionWith(net);
            _lit = lit;
            _litFrom = (selected, net);
        }

        return _lit;
    }

    /// <summary>
    /// Brings a place on the sheet into view: the view is centred on it, and moved closer only when it would
    /// otherwise be hard to find — something taking up less than a quarter of the view is zoomed to, and anything
    /// larger is left at the zoom the reader chose. Jumping to a chosen zoom every time would throw away the view
    /// they had set up, which is worse than a small item being small.
    /// </summary>
    public void ShowArea(RectD area)
    {
        if (Scene is null || area.IsEmpty)
        {
            return;
        }

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        SyncViewport();
        _camera.Center = new Vector2D((area.MinX + area.MaxX) / 2, (area.MinY + area.MaxY) / 2);

        if (CameraFocus.AreaFor(area, _camera.VisibleWorld) is { } closer)
        {
            _camera.Fit(closer);
        }

        Present();
    }

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

    /// <summary>
    /// Asks for a name where the item is being dropped. A label with no name says nothing, so the tool cannot place
    /// one until this answers; Enter gives the name, Esc gives nothing. The field belongs to the canvas because the
    /// canvas is what owns the screen — the tool only knows it asked.
    /// </summary>
    /// <summary>Where the pointer last was over the canvas, in sheet nanometres; null before it has been over it.</summary>
    internal Vector2L? CursorSheet => Scene is { } scene && _lastPoint != default ? scene.ToSheetNm(World(_lastPoint)).Round() : null;

    /// <summary>
    /// Offers a short list at the pointer and answers what was chosen, or null when the menu was closed without a
    /// choice — the menu KiCad pops up for Unfold from Bus.
    /// </summary>
    internal Task<string?> ChooseAsync(IReadOnlyList<string> choices)
    {
        var answer = new TaskCompletionSource<string?>();
        var menu = new ContextMenu { Placement = Avalonia.Controls.PlacementMode.Pointer };
        foreach (string choice in choices)
        {
            var item = new MenuItem { Header = choice };
            item.Click += (_, _) => answer.TrySetResult(choice);
            menu.Items.Add(item);
        }

        // Closed is raised before the item's Click on some platforms: the answer is given a turn to arrive first.
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => answer.TrySetResult(null));
        menu.Open(this);
        return answer.Task;
    }

    internal Task<string?> AskForNameAsync(Vector2L sheetPoint, string initial)
    {
        if (Scene is not { } scene)
        {
            return Task.FromResult<string?>(null);
        }

        var answer = new TaskCompletionSource<string?>();
        var screen = _camera.WorldToScreen(scene.ToSceneMm(sheetPoint.ToDouble()));

        var box = new TextBox
        {
            Classes = { "filter" },
            Text = initial,
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Max(screen.X, 0), Math.Max(screen.Y - 12, 0), 0, 0),
        };

        // The field is only given up once it has actually had the keys: focus arrives a layout pass later, and a
        // box that never got it must not quietly cancel the placing.
        bool focused = false;

        void Close(string? result)
        {
            // The answer is given before the field goes away. Removing it makes it lose focus, and that handler
            // re-enters here with "cancelled" — which would win the race and throw away the name that was typed.
            answer.TrySetResult(result);

            if (Children.Contains(box))
            {
                Children.Remove(box);
            }

            Focus();
            Present();
        }

        box.GotFocus += (_, _) => focused = true;

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter or Key.Return:
                    Close(box.Text);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close(null);
                    e.Handled = true;
                    break;
            }
        };

        box.LostFocus += (_, _) =>
        {
            if (focused)
            {
                Close(null);
            }
        };

        Children.Add(box);
        box.SelectAll();

        // Focus after the layout pass: a control that is not in the tree yet cannot take it.
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Loaded);
        return answer.Task;
    }

    /// <summary>
    /// Ends the run the tool has in progress. False when there was none — which is how Esc knows to leave the tool
    /// altogether rather than only end a run. The document calls this too, because Esc reaches the window first.
    /// </summary>
    internal bool CancelToolRun()
    {
        if (_tool is not { } tool)
        {
            return false;
        }

        bool ended = tool.Cancel();
        Present();
        return ended;
    }

    /// <summary>Starts a move from the keyboard, the way KiCad's M does: the selection follows the cursor.</summary>
    /// <param name="stretching">
    /// Whether the wires that meet the selection keep hold of it. That is the difference between taking a part away
    /// from its wiring and nudging it while the wiring follows.
    /// </param>
    public void BeginMoveWithCursor(bool stretching = false)
    {
        if (Editor is { } editor && _gesture == Gesture.None && editor.BeginMove(null, World(_lastPoint), stretching))
        {
            _gesture = Gesture.Moving;
            editor.UpdateMove(World(_lastPoint));
            Present();
        }
    }

    /// <summary>
    /// The sheet point under a place on this control, snapped to the grid — what a drop needs to know, and the same
    /// conversion the cursor already goes through.
    /// </summary>
    public Vector2L? SheetPointAt(Point point) =>
        Editor is { } editor && Scene is { } scene ? editor.Snap(scene.ToSheetNm(World(point)).Round()) : null;

    /// <summary>Pastes where the pointer is, as KiCad does: the clipboard lands under the cursor, not where it was cut.</summary>
    public void PasteAtCursor()
    {
        if (Editor is { } editor && Scene is { } scene)
        {
            editor.Paste(scene.ToSheetNm(World(_lastPoint)).Round());
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EditorProperty)
        {
            return;
        }

        if (change.OldValue is SchematicEditor old)
        {
            old.SceneChanged -= Present;
            old.SelectionChanged -= Present;
        }

        if (change.NewValue is SchematicEditor editor)
        {
            editor.SceneChanged += Present;
            editor.SelectionChanged += Present;
        }

        _gesture = Gesture.None;
        _surface.Scene = Scene;
        ZoomToFit();
        Present();
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

        // A click while the selection is on the cursor drops it where it stands.
        if (_gesture == Gesture.Moving && props.IsLeftButtonPressed)
        {
            Editor?.CommitMove();
            _gesture = Gesture.None;
            Present();
            e.Handled = true;
            return;
        }

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
        {
            _gesture = Gesture.Panning;
            e.Pointer.Capture(this);
        }
        else if (props.IsLeftButtonPressed && _tool is { } tool && Scene is { } toolScene)
        {
            UpdateWireSnapRadius();
            tool.Click(toolScene.ToSheetNm(World(point.Position)).Round());
            Present();
        }
        else if (props.IsLeftButtonPressed && Editor is { } editor && editor.Selection is [var shape] && HandleUnder(point.Position) is { } handle)
        {
            // A double click on a corner takes it out; a single one takes hold of it.
            if (e.ClickCount == 2)
            {
                editor.RemoveCorner(shape, handle);
                _pressDone = true;
            }
            else if (editor.BeginPointEdit(shape, handle))
            {
                _gesture = Gesture.EditingPoint;
                e.Pointer.Capture(this);
            }

            Present();
        }
        else if (props.IsLeftButtonPressed && e.ClickCount == 2 && Editor is { } outlined && outlined.Selection is [var line]
            && SchPoints.CanAddCorner(line) && Pick(point.Position) is var hit and >= 0 && ReferenceEquals(outlined.Scene.Owner(hit), line))
        {
            // A double click on the outline of the selected shape puts a corner in there.
            outlined.AddCorner(line, World(point.Position));
            _pressDone = true;
            Present();
        }
        else if (props.IsLeftButtonPressed && e.ClickCount == 2 && Editor is { } grouping && _tool is null
            && Pick(point.Position) is var under and >= 0 && grouping.EnterGroup(grouping.Scene.Owner(under)))
        {
            // A double click on a member of a group goes into it, as KiCad's does.
            _pressDone = true;
            Present();
        }
        else if (props.IsLeftButtonPressed)
        {
            _pressOwner = Pick(point.Position);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_tool is { } tool && Scene is { } toolScene && _gesture != Gesture.Panning)
        {
            UpdateWireSnapRadius();
            tool.Move(toolScene.ToSheetNm(World(p)).Round());
        }

        switch (_gesture)
        {
            case Gesture.Panning:
                _camera.PanPixels(p.X - _lastPoint.X, p.Y - _lastPoint.Y);
                Present();
                break;

            case Gesture.Moving:
                Editor?.UpdateMove(World(p));
                Present();
                break;

            case Gesture.EditingPoint:
                Editor?.UpdatePointEdit(World(p));
                Present();
                break;

            case Gesture.BoxSelecting:
                Present();
                break;

            case Gesture.None when e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && Distance(p, _pressPoint) > DragSlopPixels:
                StartDrag(p);
                break;
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
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (_gesture)
        {
            case Gesture.Panning:
                _gesture = Gesture.None;
                break;

            case Gesture.BoxSelecting:
                Editor?.SelectInBox(SelectionBox(p), IsCrossing(p), shift);
                _gesture = Gesture.None;
                break;

            case Gesture.Moving:
                // The move stays on the cursor until the next click, as KiCad does.
                break;

            case Gesture.EditingPoint:
                Editor?.CommitPointEdit();
                _gesture = Gesture.None;
                break;

            default:
                if (e.InitialPressMouseButton == MouseButton.Left && Distance(p, _pressPoint) <= DragSlopPixels && !_pressDone)
                {
                    Editor?.Click(_pressOwner, shift);
                }

                break;
        }

        _pressOwner = -1;
        _pressDone = false;
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
        switch (e.Key)
        {
            case Key.Space:
                _spaceDown = true;
                break;
            case Key.Home:
                ZoomToFit();
                break;
            case Key.Enter or Key.Return:
                _ = _tool?.Finish();
                Present();
                break;
            case Key.Escape:
                if (_tool is not null)
                {
                    // The first Esc ends the run; a second one puts the pointer back to selecting, as KiCad does.
                    if (!CancelToolRun())
                    {
                        ToolCancelled?.Invoke();
                    }
                }
                else if (_gesture == Gesture.Moving)
                {
                    editor?.CancelMove();
                    _gesture = Gesture.None;
                }
                else if (_gesture == Gesture.EditingPoint)
                {
                    editor?.CancelPointEdit();
                    _gesture = Gesture.None;
                }
                else if (editor is { EnteredGroup: not null, Selection.Count: 0 })
                {
                    // With nothing selected, Esc comes back out of the group gone into — KiCad's second Esc.
                    editor.LeaveGroup();
                }
                else
                {
                    editor?.SetSelection([]);
                    HighlightCleared?.Invoke();
                }

                Present();
                break;
            case Key.OemTilde:
                HighlightNetRequested?.Invoke();
                break;
            case Key.Tab when _tool is WireTool wire:
                wire.FlipCorner();
                Present();
                break;
            case Key.Delete or Key.Back:
                editor?.DeleteSelection();
                break;
            case Key.R:
                editor?.Rotate(90);
                Present();
                break;
            case Key.X:
                editor?.Mirror(horizontal: true);
                break;
            case Key.Y:
                editor?.Mirror(horizontal: false);
                break;
            case Key.M:
                BeginMoveWithCursor();
                break;
            case Key.G:
                BeginMoveWithCursor(stretching: true);
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

    /// <summary>A drag that started on the selection moves it; anywhere else it draws a selection box.</summary>
    private void StartDrag(Point current)
    {
        if (Editor is not { } editor)
        {
            return;
        }

        bool onSelection = _pressOwner >= 0 && editor.Scene.IsLive(_pressOwner)
            && editor.IsSelected(editor.Scene.Owner(_pressOwner));

        // Pulling a selection with the mouse keeps its wiring, as KiCad does unless told otherwise (its
        // input.drag_is_move is false): a part pulled across the sheet takes the ends of its wires with it. Tearing
        // it away from them is what M is for.
        if (onSelection && editor.BeginMove(editor.Scene.Owner(_pressOwner), World(_pressPoint), stretching: true))
        {
            _gesture = Gesture.Moving;
            editor.UpdateMove(World(current));
        }
        else
        {
            _gesture = Gesture.BoxSelecting;
        }

        Present();
    }

    /// <summary>The handle of the selected shape under the pointer, if it is near enough one to take hold of it.</summary>
    private SchHandle? HandleUnder(Point screen)
    {
        if (Editor is not { } editor || _tool is not null || _gesture != Gesture.None)
        {
            return null;
        }

        SchHandle? nearest = null;
        double best = HandleReachPixels * HandleReachPixels;
        foreach (var handle in editor.Handles)
        {
            var at = _camera.WorldToScreen(editor.Scene.ToSceneMm(handle.At.ToDouble()));
            double dx = at.X - screen.X, dy = at.Y - screen.Y;
            if ((dx * dx) + (dy * dy) <= best)
            {
                best = (dx * dx) + (dy * dy);
                nearest = handle;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The handles of the selected shape, drawn over everything: a ring of the selection's colour round a core of
    /// the paper, the same size on screen at any zoom. The handle being pulled is drawn where it now is.
    /// </summary>
    private IReadOnlyList<LayerGeometry>? HandleLayers(SchematicEditor editor)
    {
        var handles = editor.PointEdit is { } edit ? SchPoints.Handles(edit.Item) : editor.Handles;
        if (handles.Count == 0 || _tool is not null)
        {
            return null;
        }

        float outer = (float)(HandlePixels / _camera.PixelsPerMm);
        var ring = new LayerGeometry(LayerStyle.Sch.Handle);
        var core = new LayerGeometry(LayerStyle.Sch.HandleCore);
        foreach (var handle in handles)
        {
            var mm = editor.Scene.ToSceneMm(handle.At.ToDouble());
            var at = new System.Numerics.Vector2((float)mm.X, (float)mm.Y);
            ring.Circles.Add(new CirclePrim(at, outer, -1));
            core.Circles.Add(new CirclePrim(at, outer * 0.55f, -1));
        }

        return [ring, core];
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

    private RectD SelectionBox(Point current)
    {
        var a = World(_pressPoint);
        var b = World(current);
        return new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    /// <summary>Right-to-left on screen means crossing selection.</summary>
    private bool IsCrossing(Point current) => current.X < _pressPoint.X;

    private Vector2D World(Point screen)
    {
        SyncViewport();
        return _camera.ScreenToWorld(new Vector2D(screen.X, screen.Y));
    }

    private void UpdateWireSnapRadius()
    {
        if (_tool is WireTool wire)
        {
            wire.SnapRadiusNm = PinSnapPixels * Units.NmPerMm / _camera.PixelsPerMm;
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private void SyncViewport()
    {
        _camera.ViewportWidth = Math.Max(Bounds.Width, 1);
        _camera.ViewportHeight = Math.Max(Bounds.Height, 1);
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
            Lit(editor.SelectedOwners),
            RenderScaling: TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)
        {
            Preview = move?.Preview is { } moved ? moved : _tool?.Preview is { } drawn ? [drawn] : null,
            PreviewTransform = move?.PreviewTransform ?? Transform2D.Identity,
            PreviewInPlace = move?.Rubber?.Layers ?? HandleLayers(editor),
            SelectionBox = _gesture == Gesture.BoxSelecting ? SelectionBox(_lastPoint) : null,
            SelectionBoxCrossing = _gesture == Gesture.BoxSelecting && IsCrossing(_lastPoint),
        };

        _surface.Present(view);
    }
}
