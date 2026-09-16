using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Render;

/// <summary>
/// What a renderer needs from a drawing: layers of primitives, where the drawing sits, and which net an owner
/// belongs to (for highlighting). A board and a schematic both answer this, so one renderer draws either.
/// </summary>
public interface IRenderScene
{
    /// <summary>Layers in draw order (bottom first).</summary>
    IReadOnlyList<LayerGeometry> Layers { get; }

    /// <summary>Everything drawn, in scene millimetres.</summary>
    RectD Bounds { get; }

    /// <summary>The drawing itself (board outline, sheet frame) — what "zoom to fit" should show.</summary>
    RectD BoardOutline { get; }

    LayerGeometry? Find(string name);

    /// <summary>Net of an owner id, or null when the drawing has no nets or the id is not one.</summary>
    Net? OwnerNet(int id);
}
