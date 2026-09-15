using System.Numerics;
using Ecad.Geometry;
using Ecad.KiCad;
using Ecad.Rendering.Fonts;

namespace Ecad.Rendering;

public static class SceneBuilder
{
    /// <summary>Converts a board into per-layer primitives. Safe to run on a background thread.</summary>
    public static BoardScene Build(Board board)
    {
        var bounds = board.ComputeBounds();
        var scene = new BoardScene(board, bounds.IsEmpty ? Vector2L.Zero : bounds.Center);
        var builder = new Builder(scene);

        foreach (var shape in board.Shapes)
        {
            builder.AddShape(shape, scene.AddOwner(shape));
        }

        foreach (var zone in board.Zones)
        {
            builder.AddZone(zone, scene.AddOwner(zone));
        }

        foreach (var fp in board.Footprints)
        {
            builder.AddFootprint(fp);
        }

        foreach (var segment in board.Segments)
        {
            int owner = scene.AddOwner(segment);
            foreach (string layer in segment.LayerNames)
            {
                builder.Line(layer, segment.Start.ToDouble(), segment.End.ToDouble(), segment.Width, owner);
            }
        }

        foreach (var arc in board.Arcs)
        {
            int owner = scene.AddOwner(arc);
            if (arc.Geometry is { } geometry)
            {
                var points = ArcMath.Tessellate(geometry);
                foreach (string layer in arc.LayerNames)
                {
                    builder.Polyline(layer, points, arc.Width, Transform2D.Identity, false, owner);
                }
            }
        }

        foreach (var via in board.Vias)
        {
            builder.AddVia(via, scene.AddOwner(via));
        }

        foreach (var text in board.Texts)
        {
            builder.AddText(text, scene.AddOwner(text));
        }

        if (!bounds.IsEmpty)
        {
            var min = scene.ToScene(new Vector2D(bounds.MinX, bounds.MinY));
            var max = scene.ToScene(new Vector2D(bounds.MaxX, bounds.MaxY));
            scene.BoardOutline = new RectD(min.X, min.Y, max.X, max.Y);
        }

        scene.Finish();
        return scene;
    }

    private sealed class Builder(BoardScene scene)
    {
        private const double Mm = Units.NmPerMm;

        public void AddFootprint(Footprint fp)
        {
            foreach (var shape in fp.Shapes)
            {
                AddShape(shape, scene.AddOwner(shape));
            }

            foreach (var zone in fp.Zones)
            {
                AddZone(zone, scene.AddOwner(zone));
            }

            foreach (var pad in fp.Pads)
            {
                AddPad(pad, scene.AddOwner(pad));
            }

            foreach (var text in fp.Texts)
            {
                if (!text.IsHidden && text.LayerName is not null)
                {
                    AddText(text, scene.AddOwner(text));
                }
            }
        }

        public void AddShape(Shape shape, int owner)
        {
            var t = shape.ToBoard;
            double width = shape.StrokeWidth * t.ScaleFactor;
            foreach (string layer in Expand(shape.LayerNames))
            {
                switch (shape.Kind)
                {
                    case ShapeKind.Line:
                        Line(layer, t.Apply(shape.Start), t.Apply(shape.End), width, owner);
                        break;

                    case ShapeKind.Rect:
                        var s = shape.Start.ToDouble();
                        var e = shape.End.ToDouble();
                        Vector2D[] corners = [s, new(e.X, s.Y), e, new(s.X, e.Y)];
                        Outline(layer, corners, width, shape.IsFilled, t, owner);
                        break;

                    case ShapeKind.Circle:
                        var center = t.Apply(shape.Center);
                        double radius = shape.Radius * t.ScaleFactor;
                        if (shape.IsFilled)
                        {
                            Circle(layer, center, radius + width / 2, owner);
                        }
                        else
                        {
                            var ring = ArcMath.Tessellate(new Arc(center, radius, 0, 2 * Math.PI));
                            Polyline(layer, ring, width, Transform2D.Identity, false, owner);
                        }

                        break;

                    case ShapeKind.Arc:
                        if (shape.ArcGeometry is { } arc)
                        {
                            Polyline(layer, ArcMath.Tessellate(arc), width, t, false, owner);
                        }

                        break;

                    case ShapeKind.Polygon:
                        var points = Array.ConvertAll(shape.Points, p => p.ToDouble());
                        Outline(layer, points, width, shape.IsFilled, t, owner);
                        break;

                    case ShapeKind.Bezier:
                        if (shape.Points is { Length: 4 } cp)
                        {
                            Polyline(layer, Bezier(cp), width, t, false, owner);
                        }

                        break;
                }
            }
        }

        public void AddPad(Pad pad, int owner)
        {
            var t = pad.ToBoard;
            var drill = pad.Drill;
            var offset = drill?.Offset.ToDouble() ?? Vector2D.Zero;
            var parts = PadOutline.Build(pad, pad.Shape, pad.Size, offset).ToList();

            foreach (string layer in Expand(pad.LayerNames))
            {
                foreach (var part in parts)
                {
                    switch (part)
                    {
                        case PadOutline.Disc d:
                            Circle(layer, t.Apply(d.Center), d.Radius * t.ScaleFactor, owner);
                            break;
                        case PadOutline.Stadium st:
                            Line(layer, t.Apply(st.A), t.Apply(st.B), st.Width * t.ScaleFactor, owner);
                            break;
                        case PadOutline.Polygon poly:
                            Polygon(layer, poly.Points, t, owner);
                            break;
                    }
                }

                if (pad.Shape == PadShape.Custom)
                {
                    foreach (var primitive in pad.Primitives)
                    {
                        AddPrimitive(layer, primitive, owner);
                    }
                }
            }

            if (drill is { } hole && hole.Size.X > 0)
            {
                string holeLayer = pad.Type == PadType.NonPlatedThroughHole ? LayerStyle.NonPlatedHoles : LayerStyle.PlatedHoles;
                if (hole.IsOval && hole.Size.X != hole.Size.Y)
                {
                    foreach (var part in PadOutline.Build(pad, PadShape.Oval, hole.Size, Vector2D.Zero))
                    {
                        if (part is PadOutline.Stadium st)
                        {
                            Line(holeLayer, t.Apply(st.A), t.Apply(st.B), st.Width * t.ScaleFactor, owner);
                        }
                        else if (part is PadOutline.Disc d)
                        {
                            Circle(holeLayer, t.Apply(d.Center), d.Radius * t.ScaleFactor, owner);
                        }
                    }
                }
                else
                {
                    Circle(holeLayer, t.Apply(Vector2D.Zero), hole.Size.X / 2.0 * t.ScaleFactor, owner);
                }
            }
        }

        public void AddVia(Via via, int owner)
        {
            var layers = via.LayerNames;
            var center = via.Position.ToDouble();
            IEnumerable<string> copper = layers.Count == 2
                ? scene.Board.Layers.CopperSpan(layers[0], layers[1]).Select(l => l.Name)
                : Expand(layers);

            foreach (string layer in copper)
            {
                Circle(layer, center, via.Size / 2.0, owner);
            }

            if (via.Drill > 0)
            {
                Circle(LayerStyle.PlatedHoles, center, via.Drill / 2.0, owner);
            }
        }

        public void AddZone(Zone zone, int owner)
        {
            foreach (var fill in zone.FilledPolygons)
            {
                if (fill.Points.Length >= 3)
                {
                    Polygon(fill.LayerName, Array.ConvertAll(fill.Points, p => p.ToDouble()), Transform2D.Identity, owner);
                }
            }
        }

        public void AddText(Text text, int owner)
        {
            string value = text.DisplayValue;
            if (text.LayerName is not { } layerName || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var size = text.Size;
            double scale = text.ToBoard.ScaleFactor;
            var style = new StrokeTextStyle(
                size.X * scale,
                size.Y * scale,
                text.PenWidth * scale,
                text.HorizontalJustify switch { "left" => TextHAlign.Left, "right" => TextHAlign.Right, _ => TextHAlign.Center },
                text.VerticalJustify switch { "top" => TextVAlign.Top, "bottom" => TextVAlign.Bottom, _ => TextVAlign.Center },
                text.DrawAngle,
                text.IsMirrored,
                text.IsItalic,
                text.LineSpacing);

            // Text becomes ordinary stroked segments, so every backend, hit-testing and highlighting handle it.
            var lines = scene.Layer(layerName).Lines;
            float width = (float)(style.PenWidth / Mm);
            StrokeTextLayout.Layout(StrokeFont.Default, value, text.BoardPosition.ToDouble(), style,
                (a, b) => lines.Add(new LinePrim(scene.ToScene(a), scene.ToScene(b), width, owner)));
        }

        public void Line(string layer, Vector2D a, Vector2D b, double widthNm, int owner) =>
            scene.Layer(layer).Lines.Add(new LinePrim(scene.ToScene(a), scene.ToScene(b), (float)(widthNm / Mm), owner));

        public void Circle(string layer, Vector2D center, double radiusNm, int owner) =>
            scene.Layer(layer).Circles.Add(new CirclePrim(scene.ToScene(center), (float)(radiusNm / Mm), owner));

        public void Polygon(string layer, Vector2D[] points, Transform2D t, int owner)
        {
            var scenePoints = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                scenePoints[i] = scene.ToScene(t.Apply(points[i]));
            }

            scene.Layer(layer).Polygons.Add(new PolygonPrim(scenePoints, owner));
        }

        public void Polyline(string layer, IReadOnlyList<Vector2D> points, double widthNm, Transform2D t, bool closed, int owner)
        {
            var lines = scene.Layer(layer).Lines;
            float w = (float)(widthNm / Mm);
            int n = points.Count;
            for (int i = 1; i < n + (closed ? 1 : 0); i++)
            {
                lines.Add(new LinePrim(scene.ToScene(t.Apply(points[i - 1])), scene.ToScene(t.Apply(points[i % n])), w, owner));
            }
        }

        private void Outline(string layer, Vector2D[] points, double widthNm, bool filled, Transform2D t, int owner)
        {
            if (filled && points.Length >= 3)
            {
                Polygon(layer, points, t, owner);
            }

            if (widthNm > 0 || !filled)
            {
                Polyline(layer, points, widthNm, t, closed: true, owner);
            }
        }

        private void AddPrimitive(string layer, Shape primitive, int owner)
        {
            var t = primitive.ToBoard;
            double width = primitive.StrokeWidth * t.ScaleFactor;
            switch (primitive.Kind)
            {
                case ShapeKind.Polygon:
                    // Pad primitives are filled unless explicitly marked otherwise.
                    var pts = Array.ConvertAll(primitive.Points, p => p.ToDouble());
                    bool filled = primitive.Node.Find("fill") is null || primitive.IsFilled;
                    Outline(layer, pts, width, filled, t, owner);
                    break;
                case ShapeKind.Circle when primitive.Node.Find("fill") is null || primitive.IsFilled:
                    Circle(layer, t.Apply(primitive.Center), primitive.Radius * t.ScaleFactor + width / 2, owner);
                    break;
                default:
                    var single = new List<string> { layer };
                    AddShapeOnLayers(primitive, single, owner);
                    break;
            }
        }

        private void AddShapeOnLayers(Shape shape, List<string> layers, int owner)
        {
            var t = shape.ToBoard;
            double width = shape.StrokeWidth * t.ScaleFactor;
            foreach (var layer in layers)
            {
                switch (shape.Kind)
                {
                    case ShapeKind.Line:
                        Line(layer, t.Apply(shape.Start), t.Apply(shape.End), width, owner);
                        break;
                    case ShapeKind.Arc when shape.ArcGeometry is { } arc:
                        Polyline(layer, ArcMath.Tessellate(arc), width, t, false, owner);
                        break;
                    case ShapeKind.Rect:
                        var s = shape.Start.ToDouble();
                        var e = shape.End.ToDouble();
                        Outline(layer, [s, new(e.X, s.Y), e, new(s.X, e.Y)], width, shape.IsFilled, t, owner);
                        break;
                    case ShapeKind.Circle:
                        var ring = ArcMath.Tessellate(new Arc(shape.Center.ToDouble(), shape.Radius, 0, 2 * Math.PI));
                        Polyline(layer, ring, width, t, false, owner);
                        break;
                }
            }
        }

        private IEnumerable<string> Expand(IReadOnlyList<string> layerNames)
        {
            foreach (var name in layerNames)
            {
                foreach (var expanded in scene.Board.Layers.Expand(name))
                {
                    yield return expanded;
                }
            }
        }

        private static Vector2D[] Bezier(Vector2L[] cp)
        {
            const int steps = 24;
            var p0 = cp[0].ToDouble();
            var p1 = cp[1].ToDouble();
            var p2 = cp[2].ToDouble();
            var p3 = cp[3].ToDouble();
            var result = new Vector2D[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double u = (double)i / steps, v = 1 - u;
                result[i] = p0 * (v * v * v) + p1 * (3 * v * v * u) + p2 * (3 * v * u * u) + p3 * (u * u * u);
            }

            return result;
        }
    }
}
