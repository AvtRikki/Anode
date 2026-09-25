using System.Numerics;
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

    /// <summary>The wire ends that are following what is being moved; empty for an ordinary move.</summary>
    public IReadOnlyList<WireEnd> Stretching => Wiring?.Following ?? [];

    /// <summary>
    /// What gives when the wiring is kept: the wires that follow and the neighbours they may slide to stay square.
    /// Null for an ordinary move.
    /// </summary>
    public WireDrag? Wiring { get; init; }

    /// <summary>
    /// The stretched wires as they are just now, drawn where they stand rather than carried with the pointer. One
    /// end of each is held still, which no transform of the wire can express.
    /// </summary>
    public RubberBand? Rubber { get; init; }

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

    /// <summary>
    /// Nothing is changed through this editor: it selects, and shows, and that is all. What a library symbol is
    /// looked at through until it can be edited in its own coordinates.
    /// </summary>
    public bool IsReadOnly { get; init; }

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

        // A click outside the group that was gone into comes back out of it first, as KiCad does.
        if (EnteredGroup is { } entered && !IsInside(item, entered))
        {
            EnteredGroup = null;
        }

        // A click on a member of a group takes the whole group.
        var taken = SchGroups.Selecting(Sheet, item, EnteredGroup);
        if (!toggle)
        {
            _selection.Clear();
            _selection.AddRange(taken);
        }
        else if (taken.All(_selection.Contains))
        {
            _selection.RemoveAll(taken.Contains);
        }
        else
        {
            _selection.AddRange(taken.Where(t => !_selection.Contains(t)));
        }

        PublishSelection();
    }

    /// <summary>
    /// The group gone into, where a click takes one item rather than the whole group — KiCad's Enter Group. Null
    /// when none is.
    /// </summary>
    public SchGroup? EnteredGroup { get; private set; }

    /// <summary>Goes into the outermost group <paramref name="item"/> is in, and selects what is in it.</summary>
    public bool EnterGroup(SchItem item)
    {
        if (Move is not null || SchGroups.Top(Sheet, item, EnteredGroup) is not { } group)
        {
            return false;
        }

        EnteredGroup = group;
        SetSelection(SchGroups.Leaves(Sheet, group));
        return true;
    }

    /// <summary>Comes back out of the group gone into; a click takes the whole group again.</summary>
    public bool LeaveGroup()
    {
        if (EnteredGroup is null)
        {
            return false;
        }

        EnteredGroup = null;
        SetSelection([]);
        return true;
    }

    /// <summary>
    /// Groups what is selected, as one step to undo: KiCad's Group Items. A member that was already in a group comes
    /// out of it — an item is in one group at most — unless the whole of that group was selected, when the group
    /// itself becomes a member and the two nest. Inside a group gone into, the new group stays inside it.
    /// </summary>
    public bool Group()
    {
        var members = SchGroups.Grouping(Sheet, _selection, EnteredGroup);
        if (Move is not null || members.Count < 2)
        {
            return false;
        }

        var group = SchGroups.New(members);
        var ids = members.Select(m => m.Uuid!).ToHashSet(StringComparer.Ordinal);
        var parents = members.Select(m => SchGroups.Parent(Sheet, m)).OfType<SchGroup>().Distinct().ToList();
        var inside = EnteredGroup;
        if (inside is not null && !parents.Contains(inside))
        {
            parents.Add(inside);
        }

        var steps = new List<IEditCommand>();
        if (parents.Count > 0)
        {
            steps.Add(new ModifyNodesCommand("Group", parents, () =>
            {
                foreach (var parent in parents)
                {
                    SchGroups.Remove(parent, ids);
                }

                if (inside is not null)
                {
                    SchGroups.Add(inside, [group.Uuid!]);
                }
            }));
        }

        steps.Add(new AddNodesCommand(Sheet, [group]));
        Execute(new CompositeCommand("Group", steps));
        return true;
    }

    /// <summary>
    /// Takes apart the outermost groups the selection is in, as one step to undo: KiCad's Ungroup Items. Their
    /// members stay where they are and stay selected; a group that was itself in a group hands its members to it.
    /// </summary>
    public bool Ungroup()
    {
        var groups = _selection.Select(i => SchGroups.Top(Sheet, i, EnteredGroup)).OfType<SchGroup>().Distinct().ToList();
        if (Move is not null || groups.Count == 0)
        {
            return false;
        }

        var parents = groups.Select(g => SchGroups.Parent(Sheet, g)).OfType<SchGroup>().Distinct().ToList();
        var steps = new List<IEditCommand>();
        if (parents.Count > 0)
        {
            steps.Add(new ModifyNodesCommand("Ungroup", parents, () =>
            {
                foreach (var group in groups)
                {
                    if (SchGroups.Parent(Sheet, group) is { } parent)
                    {
                        SchGroups.Remove(parent, [group.Uuid!]);
                        SchGroups.Add(parent, group.Members);
                    }
                }
            }));
        }

        steps.Add(new DeleteNodesCommand(Sheet, groups));
        var kept = _selection.ToList();
        Execute(new CompositeCommand("Ungroup", steps));
        SetSelection(kept.Where(i => i.IsAttached));
        return true;
    }

    /// <summary>The item is in <paramref name="group"/>, directly or through a group inside it.</summary>
    private bool IsInside(SchItem item, SchGroup group)
    {
        var seen = new HashSet<SchGroup>();
        for (var at = SchGroups.Parent(Sheet, item); at is not null && seen.Add(at); at = SchGroups.Parent(Sheet, at))
        {
            if (ReferenceEquals(at.Node, group.Node))
            {
                return true;
            }
        }

        return false;
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

        // A group comes whole or not at all: enclosing it all, or touching any of it when crossing — KiCad's rule.
        var caught = hits.ToHashSet();
        var taken = new List<SchItem>();
        foreach (var item in hits)
        {
            if (SchGroups.Top(Sheet, item, EnteredGroup) is not { } group)
            {
                taken.Add(item);
                continue;
            }

            var leaves = SchGroups.Leaves(Sheet, group);
            if (crossing || leaves.All(caught.Contains))
            {
                taken.AddRange(leaves.Where(l => !taken.Contains(l)));
            }
        }

        foreach (var item in taken)
        {
            if (toggle && _selection.Remove(item))
            {
                continue;
            }

            if (!_selection.Contains(item))
            {
                _selection.Add(item);
            }
        }

        FocusOwner = -1;
        PublishSelection();
    }

    /// <summary>
    /// Starts moving the movable part of the selection, grabbing <paramref name="grabbed"/> if it is selected.
    ///
    /// With <paramref name="stretching"/>, the wires that meet what is moving keep hold of it: the ends that sit on
    /// its pins travel with it and the wires stretch. That is the difference between taking a part away from its
    /// wiring and nudging it while the wiring follows.
    /// </summary>
    public bool BeginMove(SchItem? grabbed, Vector2D cursorScene, bool stretching = false)
    {
        if (Move is not null || IsReadOnly)
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
        var wiring = stretching ? WireDrag.For(Sheet, items) : null;
        bool stretches = wiring is { Wires.Count: > 0 };
        Move = new SchMoveOperation(items, preview, SchEdits.Anchor(anchorItem), Scene.ToSheetNm(cursorScene))
        {
            Wiring = stretches ? wiring : null,
            Rubber = stretches ? new RubberBand(LayerStyle.Sch.Wire) : null,
        };

        // The wires that are being stretched come off the scene for the duration: what is drawn for them is the
        // rubber band, which changes with every step of the pointer.
        if (stretches)
        {
            Scene.Remove([.. wiring!.Wires]);
        }

        StretchRubber(Move);
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
        StretchRubber(move);
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
            AddToScene(move.Items.Concat(Wires(move)).Distinct());
            return;
        }

        var (items, anchor, rotation, delta) = (move.Items, move.AnchorNm, move.Rotation, move.Delta);

        // The wires that are keeping hold of it change too, so they are part of the same step: one undo has to put
        // the drawing back as it was, wires and all.
        var shape = move.Wiring?.Shape(delta, DragStep);
        var touched = items.Concat(Wires(move)).Distinct().ToList();
        var name = Named(rotation, move.Stretching.Count);

        var command = new ModifyNodesCommand(name, touched, () =>
        {
            foreach (var item in items)
            {
                SchEdits.Transform(item, anchor, SchEdits.CanRotate(item) ? rotation : 0, delta);
            }

            shape?.Write();
        });

        // A wire the drag has shrunk to nothing goes, and the steps that keep the others square come in — all in
        // the same step, so that one undo puts the drawing back.
        var collapsed = shape?.Collapsed.Cast<SchItem>().ToList() ?? [];
        var added = shape?.NewWires().Cast<SchItem>().ToList() ?? [];
        if (collapsed.Count == 0 && added.Count == 0)
        {
            Execute(command, removedFromScene: true);
            return;
        }

        var steps = new List<IEditCommand> { command };
        if (collapsed.Count > 0)
        {
            steps.Add(new DeleteNodesCommand(Sheet, collapsed));
        }

        if (added.Count > 0)
        {
            steps.Add(new AddNodesCommand(Sheet, added));
        }

        Execute(new CompositeCommand(name, steps), removedFromScene: true);
    }

    /// <summary>The wires a drag has taken off the scene to draw as a rubber band.</summary>
    private static IEnumerable<SchItem> Wires(SchMoveOperation move) => move.Wiring?.Wires ?? [];

    /// <summary>How far apart the steps put into neighbouring wires are set: one grid, as KiCad does.</summary>
    private long DragStep => GridNm > 0 ? GridNm : 1_270_000;

    /// <summary>A handle being pulled: which, of what, and the items as they were before it was taken hold of.</summary>
    public sealed record PointEditing(SchItem Item, SchHandle Handle, IReadOnlyList<SchItem> Affected, IReadOnlyList<Sexpr.SList> Before)
    {
        public Vector2L To { get; internal set; }
    }

    /// <summary>The handle being pulled, if one is.</summary>
    public PointEditing? PointEdit { get; private set; }

    /// <summary>The handles of the one item selected, when it is a shape whose points can be pulled.</summary>
    public IReadOnlyList<SchHandle> Handles =>
        Move is null && !IsReadOnly && _selection is [var item] && item.IsAttached ? SchPoints.Handles(item) : [];

    /// <summary>
    /// Takes hold of a handle. Until it is let go the shape is redrawn as it is being pulled; letting go makes it
    /// one step to undo, and calling it off puts everything back as it was.
    /// </summary>
    public bool BeginPointEdit(SchItem item, SchHandle handle)
    {
        if (Move is not null || PointEdit is not null || !SchPoints.Handles(item).Contains(handle))
        {
            return false;
        }

        var affected = SchPoints.Affected(Sheet, item, handle);
        PointEdit = new PointEditing(item, handle, affected, [.. affected.Select(a => a.Node.CloneList())]) { To = handle.At };
        return true;
    }

    /// <summary>Pulls the handle to the cursor, snapped to the grid as KiCad snaps a point being edited.</summary>
    public void UpdatePointEdit(Vector2D cursorScene)
    {
        if (PointEdit is not { } edit)
        {
            return;
        }

        var to = Snap(Scene.ToSheetNm(cursorScene).Round());
        if (to == edit.To)
        {
            return;
        }

        edit.To = to;
        Restore(edit);
        SchPoints.Move(Sheet, edit.Item, edit.Handle, to);
        Refresh(edit.Affected);
    }

    public void CommitPointEdit()
    {
        if (PointEdit is not { } edit)
        {
            return;
        }

        PointEdit = null;
        Restore(edit);
        if (edit.To == edit.Handle.At)
        {
            Refresh(edit.Affected);
            return;
        }

        Execute(new ModifyNodesCommand("Move point", edit.Affected, () => SchPoints.Move(Sheet, edit.Item, edit.Handle, edit.To)));
    }

    public void CancelPointEdit()
    {
        if (PointEdit is not { } edit)
        {
            return;
        }

        PointEdit = null;
        Restore(edit);
        Refresh(edit.Affected);
    }

    /// <summary>Puts a corner into the selected outline where it passes nearest the point, as one step to undo.</summary>
    public bool AddCorner(SchItem item, Vector2D atScene)
    {
        if (!SchPoints.CanAddCorner(item))
        {
            return false;
        }

        var at = Snap(Scene.ToSheetNm(atScene).Round());
        Execute(new ModifyNodesCommand("Add corner", [item], () => SchPoints.AddCorner(item, at)));
        return true;
    }

    /// <summary>Takes a corner out of the selected outline, as one step to undo.</summary>
    public bool RemoveCorner(SchItem item, SchHandle handle)
    {
        if (!SchPoints.CanRemoveCorner(item, handle))
        {
            return false;
        }

        Execute(new ModifyNodesCommand("Remove corner", [item], () => SchPoints.RemoveCorner(item, handle)));
        return true;
    }

    private static void Restore(PointEditing edit)
    {
        for (int i = 0; i < edit.Affected.Count; i++)
        {
            edit.Affected[i].Node.RestoreFrom(edit.Before[i]);
            edit.Affected[i].AfterRestore();
        }
    }

    /// <summary>What the step is called, which is what the reader is offered to undo.</summary>
    private static string Named(double rotation, int stretched) =>
        stretched > 0 ? "Drag" : rotation == 0 ? "Move" : "Move and rotate";

    public void CancelMove()
    {
        if (Move is not { } move)
        {
            return;
        }

        Move = null;
        AddToScene(move.Items);

        // The wires that were being stretched go back untouched: a drag that was called off changes nothing.
        if (move.Wiring is { } wiring)
        {
            AddToScene(wiring.Wires);
        }
    }

    /// <summary>
    /// Redraws the wires that are keeping hold of what is moving: every point of each stays where it is except the
    /// end that is following, which is wherever the pointer has taken it.
    /// </summary>
    private void StretchRubber(SchMoveOperation move)
    {
        if (move.Rubber is not { } rubber)
        {
            return;
        }

        var shape = move.Wiring!.Shape(move.Delta, DragStep);
        var lines = new List<(Vector2 From, Vector2 To, float Width, bool IsBus)>();
        foreach (var (wire, points) in shape.Points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Add(wire, points[i - 1], points[i]);
            }
        }

        foreach (var (like, from, to) in shape.Added)
        {
            Add(like, from, to);
        }

        rubber.Set(lines);

        void Add(SchWire wire, Vector2L from, Vector2L to)
        {
            if (from == to)
            {
                return;
            }

            // Nanometres to millimetres through the helper: both are whole numbers, and dividing them as they stand
            // gave every stretched line a width of zero.
            var width = wire.StrokeWidth > 0 ? wire.StrokeWidth : wire.IsBus ? BusWidthNm : WireWidthNm;
            lines.Add((At(from), At(to), (float)Units.NmToMm(width), wire.IsBus));
        }

        Vector2 At(Vector2L point)
        {
            var mm = Scene.ToSceneMm(point.ToDouble());
            return new Vector2((float)mm.X, (float)mm.Y);
        }
    }

    /// <summary>KiCad's default wire width, which is what a stretched wire is drawn with.</summary>
    private const long WireWidthNm = 152_400;
    private const long BusWidthNm = 304_800;

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

        // A locked item is not deleted with the rest: it stays, and stays selected, so it is plain that it did.
        var items = _selection.Where(item => !item.IsLocked).ToList();
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            _selection.Remove(item);
        }


        // A dot is only a dot while the branch under it is there. Taking the branch away takes the dot with it, in
        // the same step, so one undo gives both back.
        var stale = SchJunctions.Stale(Sheet, items);
        List<SchItem> going = [.. items, .. stale];

        // A group keeps the members that stay; one left with none goes too, as KiCad never writes an empty group.
        var (shrinking, emptied) = SchGroups.Losing(Sheet, going);
        if (shrinking.Count == 0 && emptied.Count == 0)
        {
            Execute(new DeleteNodesCommand(Sheet, going));
            return;
        }

        var ids = going.Concat(emptied).Select(i => i.Uuid).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var steps = new List<IEditCommand>();
        if (shrinking.Count > 0)
        {
            steps.Add(new ModifyNodesCommand("Delete", shrinking, () =>
            {
                foreach (var group in shrinking)
                {
                    SchGroups.Remove(group, ids);
                }
            }));
        }

        steps.Add(new DeleteNodesCommand(Sheet, [.. going, .. emptied]));
        Execute(new CompositeCommand(going.Count == 1 ? "Delete item" : $"Delete {going.Count} items", steps));
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

    /// <summary>Which edge of the selection the rest is brought to, or the middle of it.</summary>
    public enum AlignTo
    {
        Left,
        Right,
        Top,
        Bottom,
        MiddleAcross,
        MiddleDown,
        Grid,
    }

    /// <summary>
    /// Brings the selection into line: every item moves until the chosen edge of it meets the same edge of the
    /// selection as a whole — or, for the grid, until each one sits on it. Nothing turns and nothing changes size;
    /// items only slide, which is what makes this safe to do to a drawing that is already wired.
    ///
    /// Each item is measured by what it covers on the sheet, so a part is brought into line by its body rather than
    /// by the point it happens to be drawn from.
    /// </summary>
    public void Align(AlignTo edge)
    {
        var items = _selection.Where(SchEdits.CanTransform).ToList();
        if (Move is not null || items.Count == 0 || (edge != AlignTo.Grid && items.Count < 2))
        {
            return;
        }

        var all = RectD.Empty;
        foreach (var item in items)
        {
            all = all.Union(Scene.BoundsOf(item));
        }

        var moves = new List<(SchItem Item, Vector2L Delta)>();
        foreach (var item in items)
        {
            var box = Scene.BoundsOf(item);
            var delta = edge switch
            {
                AlignTo.Left => OnSheet(all.MinX - box.MinX, 0),
                AlignTo.Right => OnSheet(all.MaxX - box.MaxX, 0),
                AlignTo.Top => OnSheet(0, all.MinY - box.MinY),
                AlignTo.Bottom => OnSheet(0, all.MaxY - box.MaxY),
                AlignTo.MiddleAcross => OnSheet(((all.MinX + all.MaxX) - (box.MinX + box.MaxX)) / 2, 0),
                AlignTo.MiddleDown => OnSheet(0, ((all.MinY + all.MaxY) - (box.MinY + box.MaxY)) / 2),
                _ => ToGrid(item),
            };

            if (delta != default)
            {
                moves.Add((item, delta));
            }
        }

        if (moves.Count == 0)
        {
            return;
        }

        Execute(new ModifyNodesCommand("Align", [.. moves.Select(m => m.Item)], () =>
        {
            foreach (var (item, delta) in moves)
            {
                SchEdits.Transform(item, default, 0, delta);
            }
        }));
    }

    /// <summary>A distance on the screen as a distance on the sheet; the scene's scale is the only thing between them.</summary>
    private Vector2L OnSheet(double dx, double dy)
    {
        var origin = Scene.ToSheetNm(new Vector2D(0, 0));
        var moved = Scene.ToSheetNm(new Vector2D(dx, dy));
        return (moved - origin).Round();
    }

    /// <summary>What it would take to put an item's own point on the grid.</summary>
    private Vector2L ToGrid(SchItem item)
    {
        var anchor = SchEdits.Anchor(item);
        return Snap(anchor) - anchor;
    }

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
        if (IsReadOnly)
        {
            return;
        }

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

    /// <summary>Draws items again without changing them — when what they show depends on something outside the file.</summary>
    public void Redraw(IReadOnlyList<SchItem> items) => Refresh(items);

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

        // An undo can take away the group that was gone into.
        if (EnteredGroup is { IsAttached: false })
        {
            EnteredGroup = null;
        }

        var owners = new HashSet<int>();
        foreach (var item in _selection)
        {
            owners.UnionWith(Scene.OwnersOf(item));
        }

        SelectedOwners = owners;
        SelectionChanged?.Invoke();
    }
}
