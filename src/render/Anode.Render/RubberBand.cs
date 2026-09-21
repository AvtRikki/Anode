using System.Numerics;

namespace Anode.Render;

/// <summary>
/// The lines drawn where something is being stretched: a wire whose far end is held still while the end in hand
/// follows the pointer.
///
/// It is one layer, rewritten in place rather than built afresh each time the pointer moves. Backends drop their
/// caches by the layer's version, so a new layer every frame would leave a cache entry behind on every one of them
/// — and a drag is hundreds of frames long.
/// </summary>
public sealed class RubberBand(string layerName)
{
    /// <summary>The layer to draw. It is drawn where it stands, not moved with the rest of the preview.</summary>
    public LayerGeometry Layer { get; } = new(layerName);

    /// <summary>Bus segments use their own layer so they keep the bus color.</summary>
    public LayerGeometry BusLayer { get; } = new(LayerStyle.Sch.Bus);

    public IReadOnlyList<LayerGeometry> Layers => [Layer, BusLayer];

    /// <summary>Replaces what is drawn with the given lines, in scene millimetres.</summary>
    public void Set(IEnumerable<(Vector2 From, Vector2 To, float Width, bool IsBus)> lines)
    {
        Layer.Lines.Clear();
        BusLayer.Lines.Clear();
        foreach (var (from, to, width, isBus) in lines)
        {
            (isBus ? BusLayer : Layer).Lines.Add(new LinePrim(from, to, width, OutlineLoops.NoOwner));
        }

        Layer.Version++;
        BusLayer.Version++;
    }
}
