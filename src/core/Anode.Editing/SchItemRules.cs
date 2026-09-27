using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

namespace Anode.Editing;

/// <summary>
/// What the editor asks of the things it edits: which of them move, turn and go; where each is held; how a move,
/// a turn or a mirror is written into the file; where a deleted thing goes and comes back from; and how a changed
/// thing is drawn again. A sheet answers one way, a library symbol another — its Y runs up and its pins live inside
/// it — and the editor, with its selection, gestures, handles and undo, is the same for both.
///
/// Every point that passes through here is in the scene's sheet coordinates, Y down, the ones the editor works in.
/// </summary>
public interface ISchItemRules
{
    /// <summary>Where a deleted thing is taken from and put back into.</summary>
    INodeHost Host { get; }

    /// <summary>Whether what is selected can be copied, duplicated and pasted.</summary>
    bool CanCopy { get; }

    bool CanTransform(SchItem item);

    bool CanRotate(SchItem item);

    bool CanDelete(SchItem item);

    /// <summary>The point of an item that lands on the grid when it is moved.</summary>
    Vector2L Anchor(SchItem item);

    /// <summary>Turns an item by <paramref name="degrees"/> counter-clockwise on screen about a pivot, then moves it.</summary>
    void Transform(SchItem item, Vector2L pivot, double degrees, Vector2L delta);

    void Mirror(SchItem item, Vector2L pivot, bool horizontal);

    /// <summary>Draws items again after they changed.</summary>
    void AddToScene(SchematicScene scene, IEnumerable<SchItem> items);

    IReadOnlyList<SchHandle> Handles(SchItem item);

    /// <summary>Everything pulling a handle may change.</summary>
    IReadOnlyList<SchItem> PointAffected(SchItem item, SchHandle handle);

    void MovePoint(SchItem item, SchHandle handle, Vector2L to);

    bool CanAddCorner(SchItem item);

    void AddCorner(SchItem item, Vector2L at);

    bool CanRemoveCorner(SchItem item, SchHandle handle);

    void RemoveCorner(SchItem item, SchHandle handle);
}

/// <summary>The rules of a sheet: what <see cref="SchEdits"/> and <see cref="SchPoints"/> say, written where they stand.</summary>
public sealed class SheetRules(Schematic sheet) : ISchItemRules
{
    public INodeHost Host => sheet;

    public bool CanCopy => true;

    public bool CanTransform(SchItem item) => SchEdits.CanTransform(item);

    public bool CanRotate(SchItem item) => SchEdits.CanRotate(item);

    public bool CanDelete(SchItem item) => !item.IsLocked;

    public Vector2L Anchor(SchItem item) => SchEdits.Anchor(item);

    public void Transform(SchItem item, Vector2L pivot, double degrees, Vector2L delta) => SchEdits.Transform(item, pivot, degrees, delta);

    public void Mirror(SchItem item, Vector2L pivot, bool horizontal) => SchEdits.Mirror(item, pivot, horizontal);

    public void AddToScene(SchematicScene scene, IEnumerable<SchItem> items) => SchematicSceneBuilder.AddItems(scene, items);

    public IReadOnlyList<SchHandle> Handles(SchItem item) => SchPoints.Handles(item);

    public IReadOnlyList<SchItem> PointAffected(SchItem item, SchHandle handle) => SchPoints.Affected(sheet, item, handle);

    public void MovePoint(SchItem item, SchHandle handle, Vector2L to) => SchPoints.Move(sheet, item, handle, to);

    public bool CanAddCorner(SchItem item) => SchPoints.CanAddCorner(item);

    public void AddCorner(SchItem item, Vector2L at) => SchPoints.AddCorner(item, at);

    public bool CanRemoveCorner(SchItem item, SchHandle handle) => SchPoints.CanRemoveCorner(item, handle);

    public void RemoveCorner(SchItem item, SchHandle handle) => SchPoints.RemoveCorner(item, handle);
}

/// <summary>
/// The rules of a library symbol on its own. The scene shows it turned over — the library's Y runs up — so every
/// point on its way into the file is turned back, and a move, a turn or a mirror is worked out in the scene exactly
/// as on a sheet and then carried into the library through the same flip: what is drawn while it moves and what is
/// written when it lands cannot disagree.
///
/// A derived symbol's body is its parent's: only its own fields can be changed from it, as in KiCad.
/// </summary>
public sealed class SymbolRules(LibSymbol shown, LibSymbol body) : ISchItemRules
{
    public INodeHost Host => body;

    public bool CanCopy => false;

    public bool CanTransform(SchItem item) => IsOwn(item) && SymbolEdits.CanTransform(item);

    public bool CanRotate(SchItem item) => CanTransform(item);

    public bool CanDelete(SchItem item) => IsOwn(item) && SymbolEdits.CanDelete(item);

    public Vector2L Anchor(SchItem item) => Flip(SymbolEdits.Anchor(item));

    public void Transform(SchItem item, Vector2L pivot, double degrees, Vector2L delta)
    {
        var t = Transform2D.Translation(-pivot.X, -pivot.Y)
            .Then(KiCadTransforms.Rotation(degrees))
            .Then(Transform2D.Translation(pivot.X + delta.X, pivot.Y + delta.Y));
        SymbolEdits.Transform(item, p => Flip(t.ApplyRounded(Flip(p))), degrees);
    }

    public void Mirror(SchItem item, Vector2L pivot, bool horizontal)
    {
        Vector2L Map(Vector2L p) => horizontal
            ? new Vector2L((2 * pivot.X) - p.X, p.Y)
            : new Vector2L(p.X, (2 * pivot.Y) - p.Y);

        SymbolEdits.Mirror(item, p => Flip(Map(Flip(p))), horizontal);
    }

    public void AddToScene(SchematicScene scene, IEnumerable<SchItem> items) => SchematicSceneBuilder.AddSymbolItems(scene, body, items);

    public IReadOnlyList<SchHandle> Handles(SchItem item) =>
        IsOwn(item) ? [.. SchPoints.Handles(item).Select(h => h with { At = Flip(h.At) })] : [];

    public IReadOnlyList<SchItem> PointAffected(SchItem item, SchHandle handle) => [item];

    public void MovePoint(SchItem item, SchHandle handle, Vector2L to) =>
        SchPoints.Move(null, item, handle with { At = Flip(handle.At) }, Flip(to));

    public bool CanAddCorner(SchItem item) => IsOwn(item) && SchPoints.CanAddCorner(item);

    public void AddCorner(SchItem item, Vector2L at) => SchPoints.AddCorner(item, Flip(at));

    public bool CanRemoveCorner(SchItem item, SchHandle handle) => IsOwn(item) && SchPoints.CanRemoveCorner(item, handle);

    public void RemoveCorner(SchItem item, SchHandle handle) => SchPoints.RemoveCorner(item, handle);

    /// <summary>Y turned over: the scene's point for a library one, and back — the flip is its own inverse.</summary>
    public static Vector2L Flip(Vector2L p) => new(p.X, -p.Y);

    /// <summary>What the symbol on screen may change: its fields always, its body only when the body is its own.</summary>
    private bool IsOwn(SchItem item) => item is SchField ? shown.Fields.Contains(item) : ReferenceEquals(shown, body);
}
