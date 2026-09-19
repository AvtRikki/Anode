using System.Numerics;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Render.Fonts;

namespace Anode.Render;

/// <summary>Converts a schematic sheet into primitives. Safe to run on a background thread.</summary>
public static class SchematicSceneBuilder
{
    /// <summary>KiCad's defaults, in nanometres: 6 mil wires, 12 mil buses, 6 mil symbol strokes.</summary>
    private const long WireWidth = 152_400;
    private const long BusWidth = 304_800;
    private const long SymbolWidth = 152_400;
    private const long NoConnectArm = 635_000;

    /// <param name="sheetPath">The appearance of the sheet to draw; see <see cref="SchematicScene.SheetPath"/>.</param>
    /// <param name="frame">What the frame prints besides the title block.</param>
    public static SchematicScene Build(Schematic schematic, string? sheetPath = null, SheetFrameText? frame = null)
    {
        var paper = DrawingSheet.PaperOf(schematic.Root);
        var scene = new SchematicScene(schematic, new Vector2L(paper.X / 2, paper.Y / 2))
        {
            SheetPath = sheetPath,
            Frame = frame ?? new SheetFrameText(),
        };
        var builder = new Builder(scene);

        builder.AddSheetFrame(paper);
        foreach (var item in schematic.Items)
        {
            builder.Add(item);
        }

        scene.Commit();
        return scene;
    }

    /// <summary>
    /// Draws the frame and title block again — after the title block was edited, or the tab turned to another place
    /// in the design. Only the frame's own layer is rebuilt.
    /// </summary>
    public static void RedrawFrame(SchematicScene scene)
    {
        var layer = scene.Layer(LayerStyle.Sch.Frame);
        layer.Lines.Clear();
        layer.Polygons.Clear();
        layer.Circles.Clear();
        layer.Images.Clear();

        new Builder(scene).AddDrawingSheet(DrawingSheet.PaperOf(scene.Schematic.Root));
        scene.Commit();
    }

    /// <summary>A picture centred on a scene point, its size given in nanometres.</summary>
    internal static ImagePrim Picture(System.Numerics.Vector2 centre, Vector2D sizeNm, byte[] image)
    {
        double w = sizeNm.X / Units.NmPerMm / 2, h = sizeNm.Y / Units.NmPerMm / 2;
        return new ImagePrim(new RectD(centre.X - w, centre.Y - h, centre.X + w, centre.Y + h), image, OutlineLoops.NoOwner);
    }

    /// <summary>Draws items again after an edit, into a scene they were removed from.</summary>
    public static void AddItems(SchematicScene scene, IEnumerable<SchItem> items)
    {
        var builder = new Builder(scene);
        foreach (var item in items)
        {
            builder.Add(item);
        }

        scene.Commit();
    }

    private sealed class Builder(SchematicScene scene)
    {
        private const double Mm = Units.NmPerMm;

        /// <summary>The sheet itself: paper, plus the frame KiCad draws 10 mm inside it.</summary>
        public void AddSheetFrame(Vector2L paper)
        {
            var sheet = scene.Layer(LayerStyle.Sch.Sheet);
            Vector2D[] corners = [new(0, 0), new(paper.X, 0), new(paper.X, paper.Y), new(0, paper.Y)];
            var points = new Vector2[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                points[i] = scene.ToScene(corners[i]);
            }

            sheet.Polygons.Add(new PolygonPrim(points, OutlineLoops.NoOwner));
            scene.BoardOutline = new RectD(points[0].X, points[0].Y, points[2].X, points[2].Y);

            AddDrawingSheet(paper);
        }

        /// <summary>KiCad's default drawing sheet on the frame's own layer; see <see cref="DrawingSheet"/>.</summary>
        public void AddDrawingSheet(Vector2L paper)
        {
            var layer = scene.Layer(LayerStyle.Sch.Frame);
            DrawingSheet.Draw(
                paper,
                scene.Schematic.TitleBlock,
                scene.Schematic.Paper,
                scene.Frame,
                (a, b, width) => layer.Lines.Add(new LinePrim(scene.ToScene(a), scene.ToScene(b), (float)(width / Mm), OutlineLoops.NoOwner)),
                outline => layer.Polygons.Add(new PolygonPrim([.. outline.Select(scene.ToScene)], OutlineLoops.NoOwner)),
                (centre, size, image) => layer.Images.Add(Picture(scene.ToScene(centre), size, image)));
        }

        public void Add(SchItem item)
        {
            switch (item)
            {
                case SchWire wire:
                    AddWire(wire, scene.AddOwner(wire));
                    break;
                case SchBusEntry entry:
                    Line(LayerStyle.Sch.Bus, entry.Position.ToDouble(), entry.EndPoint.ToDouble(), BusWidth, scene.AddOwner(entry));
                    break;
                case SchJunction junction:
                    Circle(LayerStyle.Sch.Junction, junction.Position.ToDouble(), junction.Diameter / 2.0, scene.AddOwner(junction));
                    break;
                case SchNoConnect noConnect:
                    AddNoConnect(noConnect, scene.AddOwner(noConnect));
                    break;
                case SchLabel label:
                    AddLabel(label, scene.AddOwner(label));
                    break;
                case SchText text:
                    Text(LayerStyle.Sch.Text, text.Text, text.Position.ToDouble(), text.TextHeight, text.Angle,
                        text.Alignment, scene.AddOwner(text));
                    break;
                case SchGraphic graphic:
                    AddGraphic(graphic, Transform2D.Identity, LayerStyle.Sch.Symbol, scene.AddOwner(graphic));
                    break;
                case SymbolInstance symbol:
                    AddSymbol(symbol, scene.AddOwner(symbol));
                    break;
                case SchSheet sheet:
                    AddSheet(sheet, scene.AddOwner(sheet));
                    break;
            }
        }

        private void AddWire(SchWire wire, int owner)
        {
            var points = wire.Points;
            string layer = wire.IsBus ? LayerStyle.Sch.Bus : LayerStyle.Sch.Wire;
            long width = wire.StrokeWidth > 0 ? wire.StrokeWidth : wire.IsBus ? BusWidth : WireWidth;
            for (int i = 1; i < points.Length; i++)
            {
                Line(layer, points[i - 1].ToDouble(), points[i].ToDouble(), width, owner);
            }
        }

        private void AddNoConnect(SchNoConnect noConnect, int owner)
        {
            var p = noConnect.Position.ToDouble();
            Line(LayerStyle.Sch.NoConnect, p + new Vector2D(-NoConnectArm, -NoConnectArm), p + new Vector2D(NoConnectArm, NoConnectArm), WireWidth, owner);
            Line(LayerStyle.Sch.NoConnect, p + new Vector2D(-NoConnectArm, NoConnectArm), p + new Vector2D(NoConnectArm, -NoConnectArm), WireWidth, owner);
        }

        private void AddLabel(SchLabel label, int owner)
        {
            Text(LayerStyle.Sch.Label, label.Text, label.Position.ToDouble(), label.TextHeight, label.Angle, label.Alignment, owner);
        }

        private void AddSymbol(SymbolInstance symbol, int owner)
        {
            // A reused sheet names its parts, and may pick their sections, per appearance.
            string? path = scene.SheetPath;
            int unit = symbol.UnitAt(path);

            if (symbol.Definition is { } definition)
            {
                var t = symbol.ToSheet;
                foreach (var graphic in definition.GraphicsOf(unit, symbol.BodyStyle))
                {
                    AddGraphic(graphic, t, LayerStyle.Sch.Symbol, owner);
                }

                foreach (var pin in definition.PinsOf(unit, symbol.BodyStyle))
                {
                    AddPin(pin, definition, t, owner);
                }
            }

            // Fields carry their own place and angle on the sheet, so they stay upright whatever the symbol does.
            foreach (var field in symbol.Fields)
            {
                string value = string.Equals(field.Name, "Reference", StringComparison.OrdinalIgnoreCase)
                    ? symbol.ReferenceAt(path) ?? field.Value
                    : field.Value;

                if (!field.IsHidden && value.Length > 0)
                {
                    Text(LayerStyle.Sch.Field, value, field.Position.ToDouble(), field.TextHeight, field.Angle, field.Alignment, owner);
                }
            }
        }

        private void AddPin(SchPin pin, LibSymbol definition, Transform2D toSheet, int owner)
        {
            if (pin.IsHidden)
            {
                return;
            }

            var root = toSheet.Apply(pin.Position.ToDouble());
            var tip = toSheet.Apply(pin.EndPoint.ToDouble());
            LineScene(LayerStyle.Sch.Pin, root, tip, SymbolWidth, owner);

            // Text reads left to right whatever the pin direction: horizontal pins keep 0°, vertical ones turn 90°.
            bool vertical = Math.Abs(tip.Y - root.Y) > Math.Abs(tip.X - root.X);
            double angle = vertical ? 90 : 0;

            if (definition.ShowPinNumbers && pin.Number.Length > 0)
            {
                var middle = new Vector2D((root.X + tip.X) / 2, (root.Y + tip.Y) / 2);
                var offset = vertical ? new Vector2D(-pin.NumberHeight * 0.7, 0) : new Vector2D(0, -pin.NumberHeight * 0.7);
                TextScene(LayerStyle.Sch.PinText, pin.Number, middle + offset, pin.NumberHeight, angle, ("center", "center"), owner);
            }

            if (definition.ShowPinNames && pin.Name is { Length: > 0 } name && name != "~")
            {
                double dx = Math.Sign(root.X - tip.X) * definition.PinNameOffset;
                double dy = Math.Sign(root.Y - tip.Y) * definition.PinNameOffset;
                var anchor = new Vector2D(root.X + dx, root.Y + dy);
                var alignment = vertical
                    ? (dy > 0 ? ("left", "center") : ("right", "center"))
                    : (dx > 0 ? ("left", "center") : ("right", "center"));
                TextScene(LayerStyle.Sch.PinText, name, anchor, pin.NameHeight, angle, alignment, owner);
            }
        }

        private void AddSheet(SchSheet sheet, int owner)
        {
            var origin = sheet.Position.ToDouble();
            var size = sheet.Size.ToDouble();
            Vector2D[] corners =
            [
                origin, new(origin.X + size.X, origin.Y),
                new(origin.X + size.X, origin.Y + size.Y), new(origin.X, origin.Y + size.Y),
            ];

            for (int i = 0; i < corners.Length; i++)
            {
                Line(LayerStyle.Sch.Label, corners[i], corners[(i + 1) % corners.Length], SymbolWidth, owner);
            }

            foreach (var field in sheet.Fields)
            {
                if (!field.IsHidden && field.Value.Length > 0)
                {
                    Text(LayerStyle.Sch.Field, field.Value, field.Position.ToDouble(), field.TextHeight, field.Angle, field.Alignment, owner);
                }
            }

            foreach (var pin in sheet.Pins)
            {
                Text(LayerStyle.Sch.Label, pin.Name, pin.Position.ToDouble(), pin.TextHeight, pin.Angle, ("left", "center"), owner);
            }
        }

        private void AddGraphic(SchGraphic graphic, Transform2D toSheet, string layer, int owner)
        {
            long width = graphic.StrokeWidth > 0 ? graphic.StrokeWidth : SymbolWidth;
            switch (graphic.Kind)
            {
                case SchShapeKind.Rectangle:
                    var s = graphic.Start.ToDouble();
                    var e = graphic.End.ToDouble();
                    Vector2D[] corners = [s, new(e.X, s.Y), e, new(s.X, e.Y)];
                    Outline(layer, corners, width, graphic.IsFilled, toSheet, owner);
                    break;

                case SchShapeKind.Polyline:
                case SchShapeKind.Bezier:
                    var points = Array.ConvertAll(graphic.Points, p => p.ToDouble());
                    if (graphic.IsFilled && points.Length >= 3)
                    {
                        Polygon(layer, points, toSheet, owner);
                    }

                    Polyline(layer, points, width, toSheet, owner);
                    break;

                case SchShapeKind.Circle:
                    var center = toSheet.Apply(graphic.Center.ToDouble());
                    double radius = graphic.Radius * toSheet.ScaleFactor;
                    if (graphic.IsFilled)
                    {
                        CircleScene(layer, center, radius, owner);
                    }
                    else
                    {
                        var ring = ArcMath.Tessellate(new Arc(center, radius, 0, 2 * Math.PI));
                        PolylineScene(layer, ring, width, owner);
                    }

                    break;

                case SchShapeKind.Arc:
                    if (graphic.ArcGeometry is { } arc)
                    {
                        Polyline(layer, ArcMath.Tessellate(arc), width, toSheet, owner);
                    }

                    break;
            }
        }

        // ——— Primitive helpers: "Scene" variants take points already in sheet coordinates ———

        private void Line(string layer, Vector2D a, Vector2D b, long widthNm, int owner) =>
            LineScene(layer, a, b, widthNm, owner);

        private void LineScene(string layer, Vector2D a, Vector2D b, long widthNm, int owner)
        {
            var geometry = scene.Layer(layer);
            var p = scene.ToScene(a);
            var q = scene.ToScene(b);
            float width = (float)(widthNm / Mm);
            geometry.Lines.Add(new LinePrim(p, q, width, owner));
            float h = width / 2;
            scene.GrowOwner(owner, new RectD(Math.Min(p.X, q.X) - h, Math.Min(p.Y, q.Y) - h, Math.Max(p.X, q.X) + h, Math.Max(p.Y, q.Y) + h));
        }

        private void Circle(string layer, Vector2D center, double radiusNm, int owner) => CircleScene(layer, center, radiusNm, owner);

        private void CircleScene(string layer, Vector2D center, double radiusNm, int owner)
        {
            var c = scene.ToScene(center);
            float r = (float)(radiusNm / Mm);
            scene.Layer(layer).Circles.Add(new CirclePrim(c, r, owner));
            scene.GrowOwner(owner, new RectD(c.X - r, c.Y - r, c.X + r, c.Y + r));
        }

        private void Polygon(string layer, Vector2D[] points, Transform2D t, int owner)
        {
            var scenePoints = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                scenePoints[i] = scene.ToScene(t.Apply(points[i]));
            }

            var polygon = new PolygonPrim(scenePoints, owner);
            scene.Layer(LayerStyle.Sch.SymbolFill).Polygons.Add(polygon);
            scene.GrowOwner(owner, polygon.Bounds);
        }

        private void Polyline(string layer, IReadOnlyList<Vector2D> points, long widthNm, Transform2D t, int owner)
        {
            for (int i = 1; i < points.Count; i++)
            {
                LineScene(layer, t.Apply(points[i - 1]), t.Apply(points[i]), widthNm, owner);
            }
        }

        private void PolylineScene(string layer, IReadOnlyList<Vector2D> points, long widthNm, int owner)
        {
            for (int i = 1; i < points.Count; i++)
            {
                LineScene(layer, points[i - 1], points[i], widthNm, owner);
            }
        }

        private void Outline(string layer, Vector2D[] points, long widthNm, bool filled, Transform2D t, int owner)
        {
            if (filled && points.Length >= 3)
            {
                Polygon(layer, points, t, owner);
            }

            for (int i = 0; i < points.Length; i++)
            {
                LineScene(layer, t.Apply(points[i]), t.Apply(points[(i + 1) % points.Length]), widthNm, owner);
            }
        }

        private void Text(string layer, string value, Vector2D anchorNm, long heightNm, double angle,
            (string Horizontal, string Vertical) alignment, int owner) =>
            TextScene(layer, value, anchorNm, heightNm, angle, alignment, owner);

        private void TextScene(string layer, string value, Vector2D anchorNm, long heightNm, double angle,
            (string Horizontal, string Vertical) alignment, int owner)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            double height = heightNm > 0 ? heightNm : 1_270_000;
            var style = new StrokeTextStyle(
                height,
                height,
                height / 8,
                alignment.Horizontal switch { "left" => TextHAlign.Left, "right" => TextHAlign.Right, _ => TextHAlign.Center },
                alignment.Vertical switch { "top" => TextVAlign.Top, "bottom" => TextVAlign.Bottom, _ => TextVAlign.Center },
                // Schematic text is never upside down: 180° reads as 0°, 270° as 90°.
                angle % 180,
                false,
                false,
                1.0);

            var geometry = scene.Layer(layer);
            float width = (float)(style.PenWidth / Mm);
            StrokeTextLayout.Layout(StrokeFont.Default, value, anchorNm, style, (a, b) =>
            {
                var p = scene.ToScene(a);
                var q = scene.ToScene(b);
                geometry.Lines.Add(new LinePrim(p, q, width, owner));
                float h = width / 2;
                scene.GrowOwner(owner, new RectD(Math.Min(p.X, q.X) - h, Math.Min(p.Y, q.Y) - h, Math.Max(p.X, q.X) + h, Math.Max(p.Y, q.Y) + h));
            });
        }
    }
}
