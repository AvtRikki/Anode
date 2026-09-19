using System.Numerics;
using Anode.Geometry;

namespace Anode.Render;

/// <summary>
/// Puts a drawing sheet into a scene's layers. What has no colour of its own goes on the frame's layer; text with a
/// colour goes on a layer of that colour beside it, named <c>{frame}/RRGGBBAA</c> — the renderers already draw and
/// dim a layer in its colour, so a coloured title costs one more layer rather than a colour on every stroke.
/// </summary>
internal sealed class DrawingSheetLayers(string frame, Func<string, LayerGeometry> layer, Func<Vector2D, Vector2> toScene)
    : IDrawingSheetSink
{
    public void Stroke(Vector2D a, Vector2D b, double width, ColorRgba? colour) =>
        For(colour).Lines.Add(new LinePrim(toScene(a), toScene(b), (float)(width / Units.NmPerMm), OutlineLoops.NoOwner));

    public void Fill(IReadOnlyList<Vector2D> outline, IReadOnlyList<IReadOnlyList<Vector2D>> holes, ColorRgba? colour) =>
        For(colour).Polygons.Add(PolygonPrim.FromRings(outline, holes, toScene, OutlineLoops.NoOwner));

    public void Picture(Vector2D centre, Vector2D size, byte[] image)
    {
        var c = toScene(centre);
        double w = size.X / Units.NmPerMm / 2, h = size.Y / Units.NmPerMm / 2;
        layer(frame).Images.Add(new ImagePrim(new RectD(c.X - w, c.Y - h, c.X + w, c.Y + h), image, OutlineLoops.NoOwner));
    }

    /// <summary>Empties the frame's layer and every coloured one beside it, ready to be drawn again.</summary>
    public static void Clear(string frame, IEnumerable<LayerGeometry> layers, Func<string, LayerGeometry> layer)
    {
        foreach (string name in layers.Select(l => l.Name).Where(n => LayerStyle.BaseOf(n) == frame).Append(frame).Distinct().ToList())
        {
            var geometry = layer(name);
            geometry.Lines.Clear();
            geometry.Circles.Clear();
            geometry.Polygons.Clear();
            geometry.Images.Clear();
        }
    }

    private LayerGeometry For(ColorRgba? colour)
    {
        if (colour is not { } c)
        {
            return layer(frame);
        }

        var tinted = layer($"{frame}/{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}");
        tinted.Color = c;
        return tinted;
    }
}
