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

    /// <summary>The drawing sheet's line and text pen: KiCad's 0.15 mm.</summary>
    private const long FrameWidth = 150_000;

    /// <param name="sheetPath">The appearance of the sheet to draw; see <see cref="SchematicScene.SheetPath"/>.</param>
    /// <param name="frame">What the frame prints besides the title block.</param>
    public static SchematicScene Build(Schematic schematic, string? sheetPath = null, SheetFrameText? frame = null)
    {
        var paper = PaperSize(schematic.Paper, schematic.IsPortrait);
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

        new Builder(scene).AddDrawingSheet(PaperSize(scene.Schematic.Paper, scene.Schematic.IsPortrait));
        scene.Commit();
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

    /// <summary>Paper sizes in nanometres, landscape unless the file says portrait.</summary>
    private static Vector2L PaperSize(string name, bool portrait)
    {
        var (width, height) = name switch
        {
            "A5" => (210.0, 148.0),
            "A4" => (297.0, 210.0),
            "A3" => (420.0, 297.0),
            "A2" => (594.0, 420.0),
            "A1" => (841.0, 594.0),
            "A0" => (1189.0, 841.0),
            "A" or "USLetter" => (279.4, 215.9),
            "B" or "USLedger" => (431.8, 279.4),
            "C" => (558.8, 431.8),
            "D" => (863.6, 558.8),
            "E" => (1117.6, 863.6),
            "USLegal" => (355.6, 215.9),
            _ => (297.0, 210.0),
        };

        return portrait
            ? new Vector2L(Units.MmToNm(height), Units.MmToNm(width))
            : new Vector2L(Units.MmToNm(width), Units.MmToNm(height));
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

        private enum Corner
        {
            RightBottom,
            LeftTop,
            LeftBottom,
            RightTop,
        }

        /// <summary>
        /// KiCad's default drawing sheet, item for item from its built-in description: a double border 2 mm apart,
        /// a tick every 50 mm numbered along the top and bottom and lettered down the sides, and the title block in
        /// the bottom-right corner. Distances are in millimetres from a corner of the area inside the 10 mm margins,
        /// the bottom-right one unless said otherwise; repeated items stop where they would leave that area.
        /// </summary>
        public void AddDrawingSheet(Vector2L paper)
        {
            const double margin = 10;
            double right = (paper.X / Mm) - margin, bottom = (paper.Y / Mm) - margin;
            string layer = LayerStyle.Sch.Frame;

            Vector2D At(double x, double y, Corner corner = Corner.RightBottom) => corner switch
            {
                Corner.LeftTop => new Vector2D(margin + x, margin + y) * Mm,
                Corner.LeftBottom => new Vector2D(margin + x, bottom - y) * Mm,
                Corner.RightTop => new Vector2D(right - x, margin + y) * Mm,
                _ => new Vector2D(right - x, bottom - y) * Mm,
            };

            bool Inside(Vector2D p) =>
                p.X >= (margin * Mm) - 1 && p.X <= (right * Mm) + 1 && p.Y >= (margin * Mm) - 1 && p.Y <= (bottom * Mm) + 1;

            void Segment(Vector2D a, Vector2D b) => Line(layer, a, b, FrameWidth, OutlineLoops.NoOwner);

            void Box(Vector2D a, Vector2D b)
            {
                Segment(a, new Vector2D(b.X, a.Y));
                Segment(new Vector2D(b.X, a.Y), b);
                Segment(b, new Vector2D(a.X, b.Y));
                Segment(new Vector2D(a.X, b.Y), a);
            }

            // The border, twice, and the scale along its edges.
            Box(At(0, 0, Corner.LeftTop), At(0, 0));
            Box(At(2, 2, Corner.LeftTop), At(2, 2));

            foreach (var corner in new[] { Corner.LeftTop, Corner.LeftBottom })
            {
                for (int i = 0; i < 30; i++)
                {
                    var (a, b) = (At(50 + (50 * i), 2, corner), At(50 + (50 * i), 0, corner));
                    if (Inside(a) && Inside(b))
                    {
                        Segment(a, b);
                    }
                }

                for (int i = 0; i < 100 && Inside(At(25 + (50 * i), 1, corner)); i++)
                {
                    FrameText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), At(25 + (50 * i), 1, corner), 1.3, TextHAlign.Left);
                }
            }

            foreach (var corner in new[] { Corner.LeftTop, Corner.RightTop })
            {
                for (int i = 0; i < 30; i++)
                {
                    var (a, b) = (At(0, 50 + (50 * i), corner), At(2, 50 + (50 * i), corner));
                    if (Inside(a) && Inside(b))
                    {
                        Segment(a, b);
                    }
                }

                for (int i = 0; i < 26 && Inside(At(1, 25 + (50 * i), corner)); i++)
                {
                    FrameText(((char)('A' + i)).ToString(), At(1, 25 + (50 * i), corner), 1.3, TextHAlign.Center);
                }
            }

            // The title block.
            var block = scene.Schematic.TitleBlock;
            var frame = scene.Frame;
            Box(At(110, 34), At(2, 2));
            Segment(At(110, 5.5), At(2, 5.5));
            Segment(At(110, 8.5), At(2, 8.5));
            Segment(At(110, 12.5), At(2, 12.5));
            Segment(At(110, 18.5), At(2, 18.5));
            Segment(At(90, 8.5), At(90, 5.5));
            Segment(At(26, 8.5), At(26, 2));

            FrameText("Date: " + block.Date, At(87, 6.9));
            FrameText(frame.Application, At(109, 4.1));
            FrameText("Rev: " + block.Revision, At(24, 6.9), bold: true);
            FrameText("Size: " + scene.Schematic.Paper, At(109, 6.9));
            FrameText($"Id: {frame.Page}/{frame.PageCount}", At(24, 4.1));
            FrameText("Title: " + block.Title, At(109, 10.7), size: 2, bold: true, italic: true);
            FrameText("File: " + frame.FileName, At(109, 14.3));
            FrameText("Sheet: " + frame.SheetPath, At(109, 17));
            FrameText(block.Company ?? string.Empty, At(109, 20), bold: true);
            for (int i = 1; i <= 4; i++)
            {
                FrameText(block.Comment(i), At(109, 20 + (3 * i)));
            }
        }

        /// <summary>Drawing-sheet text: KiCad's 1.5 mm default, left-aligned and centred on its line unless said otherwise.</summary>
        private void FrameText(
            string value,
            Vector2D anchorNm,
            double size = 1.5,
            TextHAlign align = TextHAlign.Left,
            bool bold = false,
            bool italic = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            double height = size * Mm;
            double pen = bold ? height / 5 : FrameWidth;
            var style = new StrokeTextStyle(height, height, pen, align, TextVAlign.Center, 0, false, italic, 1.0);
            var geometry = scene.Layer(LayerStyle.Sch.Frame);
            float width = (float)(pen / Mm);
            StrokeTextLayout.Layout(StrokeFont.Default, value, anchorNm, style, (a, b) =>
                geometry.Lines.Add(new LinePrim(scene.ToScene(a), scene.ToScene(b), width, OutlineLoops.NoOwner)));
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
