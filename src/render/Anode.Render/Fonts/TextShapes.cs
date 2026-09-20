using System.Numerics;
using Anode.Geometry;
using Anode.Kicad;

namespace Anode.Render.Fonts;

/// <summary>
/// Puts a text of a sheet or a board into its layer: strokes of the stroke font, or the filled glyphs of the face it
/// names with its overbars as strokes. Either way it becomes ordinary primitives of its owner, which every backend,
/// hit-testing and highlighting already handle.
/// </summary>
internal static class TextShapes
{
    public static void Emit(
        LayerGeometry layer,
        string value,
        Vector2D anchor,
        in TextStyle style,
        TextFont font,
        Func<Vector2D, Vector2> toScene,
        Action<RectD> grow,
        int owner)
    {
        float width = (float)(style.PenWidth / Units.NmPerMm);
        void Stroke(Vector2D a, Vector2D b)
        {
            var p = toScene(a);
            var q = toScene(b);
            layer.Lines.Add(new LinePrim(p, q, width, owner));
            float h = width / 2;
            grow(new RectD(Math.Min(p.X, q.X) - h, Math.Min(p.Y, q.Y) - h, Math.Max(p.X, q.X) + h, Math.Max(p.Y, q.Y) + h));
        }

        if (font.Face is { } face)
        {
            OutlineText.Layout(face, font.Bold, value, anchor, style, shape =>
            {
                var polygon = PolygonPrim.FromRings(shape.Outline, shape.Holes, toScene, owner);
                layer.Polygons.Add(polygon);
                grow(polygon.Bounds);
            }, Stroke);
            return;
        }

        StrokeTextLayout.Layout(StrokeFont.Default, value, anchor, style, Stroke);
    }

    /// <summary>
    /// The letters themselves, in board units rather than on a layer: the strokes to be drawn with the pen, and the
    /// shapes to be filled. What knockout text is cut out of its box with.
    /// </summary>
    public static (List<(Vector2D A, Vector2D B)> Strokes, List<PolygonWithHoles> Shapes) Collect(
        string value,
        Vector2D anchor,
        in TextStyle style,
        TextFont font)
    {
        var strokes = new List<(Vector2D, Vector2D)>();
        var shapes = new List<PolygonWithHoles>();

        if (font.Face is { } face)
        {
            OutlineText.Layout(face, font.Bold, value, anchor, style,
                shape => shapes.Add(new PolygonWithHoles(shape.Outline, shape.Holes)),
                (a, b) => strokes.Add((a, b)));
        }
        else
        {
            StrokeTextLayout.Layout(StrokeFont.Default, value, anchor, style, (a, b) => strokes.Add((a, b)));
        }

        return (strokes, shapes);
    }
}
