using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

namespace Anode.Editing;

/// <summary>An in-progress move: the moved items' primitives are drawn with <see cref="PreviewTransform"/> until committed.</summary>
public sealed class SchMoveOperation
{
    internal SchMoveOperation(IReadOnlyList<SchItem> items, IReadOnlyList<LayerGeometry> preview, Vector2L anchorNm, Vector2D startCursorNm)
    {
        Items = items;
        Preview = preview;
        AnchorNm = anchorNm;
        StartCursorNm = startCursorNm;
    }

    public IReadOnlyList<SchItem> Items { get; }

    /// <summary>Primitives of the moved items, removed from the scene for the duration of the move.</summary>
    public IReadOnlyList<LayerGeometry> Preview { get; }

    /// <summary>Point of the grabbed item that snaps to the grid.</summary>
    public Vector2L AnchorNm { get; }

    public Vector2D StartCursorNm { get; }

    public Vector2L Delta { get; internal set; }

    /// <summary>Degrees, counter-clockwise on screen, about the anchor.</summary>
    public double Rotation { get; internal set; }

    /// <summary>Scene-space (mm) transform for <see cref="Preview"/>.</summary>
    public Transform2D PreviewTransform { get; internal set; } = Transform2D.Identity;
}

/// <summary>
/// Editing session for one sheet: selection, moves, rotation, mirroring, deletion and undo, keeping the scene in
/// sync. The counterpart of <see cref="BoardEditor"/> — same shape, same undo stack, schematic rules: the grid is
/// KiCad's 50 mil, and a mirror is a property of the symbol rather than reflected geometry.
/// UI-agnostic; coordinates passed in are scene millimetres.
/// </summary>
public sealed class SchematicEditor
{
    private readonly List<SchItem> _selection = [];

    public SchematicEditor(SchematicScene scene)
    {
        Scene = scene;
    }

    public SchematicScene Scene { get; }

    public Schematic Sheet => Scene.Schematic;

    public UndoStack History { get; } = new();

    /// <summary>Grid step for move snapping: 50 mil, as KiCad draws schematics on. 0 disables snapping.</summary>
    public long GridNm { get; set; } = 1_270_000;

    /// <summary>Triangulate polygons of rebuilt items right away (needed by the OpenGL backend).</summary>
    public bool TriangulateChanges { get; set; }

    public IReadOnlyList<SchItem> Selection => _selection;

    /// <summary>Scene owners of the selected items. A new set instance is published on every change.</summary>
    public IReadOnlySet<int> SelectedOwners { get; private set; } = new HashSet<int>();

    /// <summary>The primitive owner last clicked.</summary>
    public int FocusOwner { get; private set; } = -1;

    public SchMoveOperation? Move { get; private set; }

    public event Action? SelectionChanged;

    /// <summary>Scene primitives changed (edit, undo, move started or ended).</summary>
    public event Action? SceneChanged;

    public bool IsSelected(SchItem item) => _selection.Contains(item);

    /// <summary>Selects the item of <paramref name="owner"/>; with <paramref name="toggle"/> adds or removes it.</summary>
    public void Click(int owner, bool toggle)
    {
        if (!Scene.IsLive(owner))
        {
            if (!toggle)
            {
                SetSelection([]);
            }

            return;
        }

        var item = Scene.Owner(owner);
        FocusOwner = owner;
        if (!toggle)
        {
            _selection.Clear();
            _selection.Add(item);
        }
        else if (!_selection.Remove(item))
        {
            _selection.Add(item);
        }

        PublishSelection();
    }

    public void SetSelection(IEnumerable<SchItem> items)
    {
        _selection.Clear();
        foreach (var item in items)
        {
            if (!_selection.Contains(item))
            {
                _selection.Add(item);
            }
        }

        PublishSelection();
    }

    /// <summary>
    /// Selects items inside <paramref name="box"/> (scene mm): fully enclosed ones, or with
    /// <paramref name="crossing"/> everything the box touches.
    /// </summary>
    public void SelectInBox(RectD box, bool crossing, bool toggle)
    {
        if (!toggle)
        {
            _selection.Clear();
        }

        var candidates = new List<SchItem>();
        foreach (var item in Scene.TopLevelItems)
        {
            var bounds = Scene.BoundsOf(item);
            bool hit = crossing
                ? box.Intersects(bounds)
                : !bounds.IsEmpty && box.Contains(bounds.MinX, bounds.MinY) && box.Contains(bounds.MaxX, bounds.MaxY);

            if (hit)
            {
                candidates.Add(item);
            }
        }

        // Bounds only narrow crossing selection down; a symbol's box covers the gaps between its own lines.
        IEnumerable<SchItem> hits = crossing
            ? candidates.Where(SelectionGeometry.Touching(Scene.Layers, box, candidates, Scene.OwnersOf).Contains)
            : candidates;

        foreach (var item in hits)
        {
            if (toggle && _selection.Remove(item))
            {
                continue;
            }

            _selection.Add(item);
        }

        FocusOwner = -1;
        PublishSelection();
    }

    /// <summary>Starts moving the movable part of the selection, grabbing <paramref name="grabbed"/> if it is selected.</summary>
    public bool BeginMove(SchItem? grabbed, Vector2D cursorScene)
    {
        if (Move is not null)
        {
            return false;
        }

        var items = _selection.Where(SchEdits.CanTransform).ToList();
        if (items.Count == 0)
        {
            return false;
        }

        var anchorItem = grabbed is not null && items.Contains(grabbed) ? grabbed : items[0];
        var preview = Scene.Remove(items, collect: true);
        Move = new SchMoveOperation(items, preview, SchEdits.Anchor(anchorItem), Scene.ToSheetNm(cursorScene));
        SceneChanged?.Invoke();
        return true;
    }

    public void UpdateMove(Vector2D cursorScene)
    {
        if (Move is not { } move)
        {
            return;
        }

        var cursor = Scene.ToSheetNm(cursorScene);
        var raw = new Vector2L((long)Math.Round(cursor.X - move.StartCursorNm.X), (long)Math.Round(cursor.Y - move.StartCursorNm.Y));
        move.Delta = Snap(move.AnchorNm + raw) - move.AnchorNm;
        UpdatePreviewTransform(move);
    }

    public void CommitMove()
    {
        if (Move is not { } move)
        {
            return;
        }

        Move = null;
        if (move.Delta == Vector2L.Zero && move.Rotation == 0)
        {
            AddToScene(move.Items);
            return;
        }

        var (items, anchor, rotation, delta) = (move.Items, move.AnchorNm, move.Rotation, move.Delta);
        var command = new ModifyNodesCommand(rotation == 0 ? "Move" : "Move and rotate", items, () =>
        {
            foreach (var item in items)
            {
                SchEdits.Transform(item, anchor, SchEdits.CanRotate(item) ? rotation : 0, delta);
            }
        });

        Execute(command, removedFromScene: true);
    }

    public void CancelMove()
    {
        if (Move is not { } move)
        {
            return;
        }

        Move = null;
        AddToScene(move.Items);
    }

    /// <summary>Rotates the selection (or the move in progress) counter-clockwise by <paramref name="degrees"/>.</summary>
    public void Rotate(double degrees)
    {
        if (Move is { } move)
        {
            move.Rotation = KiCadNumber.Normalize360(move.Rotation + degrees);
            UpdatePreviewTransform(move);
            return;
        }

        var items = _selection.Where(SchEdits.CanRotate).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var pivot = items.Count == 1 ? SchEdits.Anchor(items[0]) : SelectionCenter(items);
        Execute(new ModifyNodesCommand("Rotate", items, () =>
        {
            foreach (var item in items)
            {
                SchEdits.Transform(item, pivot, degrees, default);
            }
        }));
    }

    /// <summary>Mirrors the selection left to right (<paramref name="horizontal"/>) or top to bottom.</summary>
    public void Mirror(bool horizontal)
    {
        if (Move is not null)
        {
            return;
        }

        var items = _selection.Where(SchEdits.CanTransform).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var pivot = items.Count == 1 ? SchEdits.Anchor(items[0]) : SelectionCenter(items);
        Execute(new ModifyNodesCommand(horizontal ? "Mirror horizontally" : "Mirror vertically", items, () =>
        {
            foreach (var item in items)
            {
                SchEdits.Mirror(item, pivot, horizontal);
            }
        }));
    }

    /// <summary>
    /// Puts new items on the sheet as one undoable step. The selection is left alone unless asked for: a tool that
    /// draws should not select what it drew, or every committed leg would dim the rest of the sheet for a frame.
    /// </summary>
    public void Add(IReadOnlyList<SchItem> items, bool select = false)
    {
        if (items.Count == 0)
        {
            return;
        }

        Execute(new AddNodesCommand(Sheet, items), removedFromScene: true);
        if (select)
        {
            SetSelection(items);
        }
    }

    /// <summary>
    /// Draws the legs of a run: the wires, a dot where the run met another wire or where three ends now meet, and the
    /// cut that turns the wire it ran into two items. All of it is one step, because it was one click. A bus meets a
    /// wire through an entry, so it gets neither dot nor cut.
    /// </summary>
    public void DrawWire(IReadOnlyList<(Vector2L From, Vector2L To)> legs, bool bus = false)
    {
        if (legs.Count == 0)
        {
            return;
        }

        var add = new List<SchItem>();
        var remove = new List<SchItem>();
        add.AddRange(legs.Select(leg => SchNodes.Wire([leg.From, leg.To], bus)));

        if (!bus)
        {
            add.AddRange(SchJunctions.Needed(Sheet, legs).Select(SchNodes.Junction));

            Vector2L[] ends = [.. legs.SelectMany(leg => (Vector2L[])[leg.From, leg.To])];
            foreach (var split in SchSplits.At(Sheet, ends))
            {
                remove.Add(split.Wire);
                add.AddRange(split.Pieces);
            }
        }

        Apply(bus ? "Draw bus" : "Draw wire", add, remove);
    }

    /// <summary>
    /// Adds and removes in one undoable step. A tap draws a wire, dots the meeting point and cuts the wire it ran
    /// into: three changes, one click, one undo.
    /// </summary>
    public void Apply(string name, IReadOnlyList<SchItem> add, IReadOnlyList<SchItem> remove)
    {
        if (add.Count == 0 && remove.Count == 0)
        {
            return;
        }

        var steps = new List<IEditCommand>();
        if (remove.Count > 0)
        {
            steps.Add(new DeleteNodesCommand(Sheet, remove));
        }

        if (add.Count > 0)
        {
            steps.Add(new AddNodesCommand(Sheet, add));
        }

        Execute(new CompositeCommand(name, steps));
    }

    /// <summary>
    /// Changes items where they stand, as one undoable step — what the inspector commits when a field is written.
    /// The snapshot the command takes is what makes an edit that is undone give the file back byte for byte.
    /// </summary>
    public void Modify(string name, IReadOnlyList<SchItem> items, Action mutate)
    {
        if (items.Count == 0)
        {
            return;
        }

        Execute(new ModifyNodesCommand(name, items, mutate));
    }

    /// <summary>
    /// Runs a command that was prepared elsewhere, as one step. Placing a part is two changes — the definition
    /// copied into the sheet and the instance that draws from it — and one click made both, so one undo must take
    /// back both; composing them is the caller's business, running them is this.
    /// </summary>
    public void Run(IEditCommand command) => Execute(command, removedFromScene: true);

    public void DeleteSelection()
    {
        if (Move is not null || _selection.Count == 0)
        {
            return;
        }

        var items = _selection.ToList();
        _selection.Clear();

        // A dot is only a dot while the branch under it is there. Taking the branch away takes the dot with it, in
        // the same step, so one undo gives both back.
        var stale = SchJunctions.Stale(Sheet, items);
        Execute(new DeleteNodesCommand(Sheet, stale.Count == 0 ? items : [.. items, .. stale]));
    }

    /// <summary>
    /// Puts a copy of the selection on the clipboard. What is stored is already a copy, so what happens to the
    /// original afterwards — moved, edited, deleted — does not reach into what will be pasted.
    /// </summary>
    public void Copy()
    {
        if (_selection.Count > 0)
        {
            SchClipboard.Put(_selection);
        }
    }

    /// <summary>Copy, then delete: one step in the history, because it was one keystroke.</summary>
    public void Cut()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        Copy();
        DeleteSelection();
    }

    /// <summary>
    /// A copy of the selection, one grid square down and to the right so it can be seen, and selected in place of
    /// the original — which is what makes the usual next gesture, dragging it somewhere, work straight away.
    /// </summary>
    public void Duplicate()
    {
        if (Move is not null || _selection.Count == 0)
        {
            return;
        }

        long step = GridNm > 0 ? GridNm : 1_270_000;
        var copies = new List<SchItem>();
        foreach (var item in _selection)
        {
            if (SchClone.Of(Sheet, item) is { } copy)
            {
                if (SchEdits.CanTransform(copy))
                {
                    SchEdits.Transform(copy, SchEdits.Anchor(copy), 0, new Vector2L(step, step));
                }

                copies.Add(copy);
            }
        }

        Place(copies);
    }

    /// <summary>Whether there is anything to paste. A clipboard outlives the sheet it was filled from.</summary>
    public bool CanPaste => SchClipboard.HasContent;

    /// <summary>
    /// Pastes the clipboard so that the top-left of what was copied lands on <paramref name="at"/>, snapped to the
    /// grid. The group keeps its own shape: what was copied together stays together.
    /// </summary>
    public void Paste(Vector2L at)
    {
        if (Move is not null)
        {
            return;
        }

        var copies = new List<SchItem>();
        foreach (var node in SchClipboard.Content)
        {
            if (SchClone.Of(Sheet, node) is { } copy)
            {
                copies.Add(copy);
            }
        }

        var movable = copies.Where(SchEdits.CanTransform).ToList();
        if (movable.Count > 0)
        {
            var origin = movable.Select(SchEdits.Anchor).Aggregate((a, b) => new Vector2L(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)));
            var delta = Snap(at) - origin;
            foreach (var copy in movable)
            {
                SchEdits.Transform(copy, SchEdits.Anchor(copy), 0, delta);
            }
        }

        Place(copies);
    }

    /// <summary>What duplicate and paste both end with: the new items on the sheet, and selected.</summary>
    private void Place(IReadOnlyList<SchItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Execute(new AddNodesCommand(Sheet, items), removedFromScene: true);
        SetSelection(items);
    }

    public void Undo()
    {
        CancelMove();
        if (History.Undo() is { } command)
        {
            Refresh([.. command.Affected.OfType<SchItem>()]);
        }
    }

    public void Redo()
    {
        CancelMove();
        if (History.Redo() is { } command)
        {
            Refresh([.. command.Affected.OfType<SchItem>()]);
        }
    }

    public void Save(string path)
    {
        Sheet.Save(path);
        History.MarkSaved();
    }

    public Vector2L Snap(Vector2L point) =>
        GridNm <= 0 ? point : new Vector2L(SnapValue(point.X), SnapValue(point.Y));

    private long SnapValue(long value) => (long)Math.Round((double)value / GridNm, MidpointRounding.AwayFromZero) * GridNm;

    private Vector2L SelectionCenter(IEnumerable<SchItem> items)
    {
        var bounds = RectD.Empty;
        foreach (var item in items)
        {
            bounds = bounds.Union(Scene.BoundsOf(item));
        }

        var center = Scene.ToSheetNm(new Vector2D((bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2));
        return Snap(center.Round());
    }

    private void UpdatePreviewTransform(SchMoveOperation move)
    {
        var anchor = Scene.ToSceneMm(move.AnchorNm.ToDouble());
        double dx = (double)move.Delta.X / Units.NmPerMm;
        double dy = (double)move.Delta.Y / Units.NmPerMm;
        move.PreviewTransform = Transform2D.Translation(-anchor.X, -anchor.Y)
            .Then(KiCadTransforms.Rotation(move.Rotation))
            .Then(Transform2D.Translation(anchor.X + dx, anchor.Y + dy));
    }

    /// <summary>
    /// Applies a command and keeps the scene in step. Named apart from the public <see cref="Run"/> on purpose: when
    /// both were called Run, every one-argument call inside this class quietly started meaning "the scene has
    /// already been cleared", and items went on being drawn after they were deleted.
    /// </summary>
    private void Execute(IEditCommand command, bool removedFromScene = false)
    {
        var affected = command.Affected.OfType<SchItem>().ToList();
        if (!removedFromScene)
        {
            Scene.Remove(affected);
        }

        try
        {
            History.Execute(command);
        }
        finally
        {
            AddToScene(affected.Where(i => i.IsAttached));
            PublishSelection();
        }
    }

    private void Refresh(IReadOnlyList<SchItem> items)
    {
        Scene.Remove(items);
        AddToScene(items.Where(i => i.IsAttached));
        PublishSelection();
    }

    private void AddToScene(IEnumerable<SchItem> items)
    {
        SchematicSceneBuilder.AddItems(Scene, items);
        if (TriangulateChanges)
        {
            SceneTriangulator.Triangulate(Scene);
        }

        SceneChanged?.Invoke();
    }

    private void PublishSelection()
    {
        _selection.RemoveAll(item => !item.IsAttached);
        var owners = new HashSet<int>();
        foreach (var item in _selection)
        {
            owners.UnionWith(Scene.OwnersOf(item));
        }

        SelectedOwners = owners;
        SelectionChanged?.Invoke();
    }
}
