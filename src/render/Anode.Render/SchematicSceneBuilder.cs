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
        // Fonts the sheet carries, its symbols' included, before any text is set.
        OutlineText.Embed(EmbeddedFile.In(schematic.Document.Root));
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

        // What the sheet hides is drawn, but on layers that are off: showing it is then a switch, not a redrawing.
        foreach (string hidden in (string[])[LayerStyle.Sch.HiddenField, LayerStyle.Sch.HiddenPin])
        {
            scene.Layer(hidden).IsVisible = false;
        }

        scene.Commit();
        return scene;
    }

    /// <summary>
    /// Draws the frame and title block again — after the title block was edited, or the tab turned to another place
    /// in the design. Only the frame's own layer is rebuilt.
    /// </summary>
    /// <summary>
    /// One symbol of a library on its own, as the symbol editor shows it: the body of one unit and body style, its
    /// pins, its fields, and a cross at its origin. A library draws a symbol with Y upward, so it is drawn through a
    /// flip — the scene's own points are sheet-like, Y down, and whoever writes back into the library turns Y over.
    /// </summary>
    /// <param name="shown">The symbol whose fields are drawn.</param>
    /// <param name="body">Whose body is drawn: the symbol itself, or the one it is derived from.</param>
    public static SchematicScene BuildSymbol(LibSymbol shown, LibSymbol body, int unit, int bodyStyle)
    {
        // The scene belongs to a sheet; a symbol on its own stands on an empty one, which has nothing to add.
        var host = Schematic.Parse("(kicad_sch (version 20250114) (generator \"anode\") (paper \"A4\"))");
        var scene = new SchematicScene(host, Vector2L.Zero);
        new Builder(scene).AddLibrarySymbol(shown, body, unit, bodyStyle);

        foreach (string hidden in (string[])[LayerStyle.Sch.HiddenField, LayerStyle.Sch.HiddenPin])
        {
            scene.Layer(hidden).IsVisible = false;
        }

        scene.Commit();

        // What "zoom to fit" shows: the symbol with a margin, never less than a small square about the origin.
        var bounds = scene.Bounds.IsEmpty ? new RectD(-5, -5, 5, 5) : scene.Bounds.Union(-5, -5).Union(5, 5);
        scene.BoardOutline = bounds.Inflate(5);
        return scene;
    }

    /// <summary>The flip a library symbol is drawn through: its Y runs up, the scene's down.</summary>
    public static Transform2D LibraryToScene { get; } = Transform2D.Scale(1, -1);

    public static void RedrawFrame(SchematicScene scene)
    {
        DrawingSheetLayers.Clear(LayerStyle.Sch.Frame, scene.Layers, scene.Layer);
        new Builder(scene).AddDrawingSheet(DrawingSheet.PaperOf(scene.Schematic.Root));
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

    private sealed class Builder(SchematicScene scene)
    {
        private const double Mm = Units.NmPerMm;

        /// <summary>
        /// How the lines being drawn just now are styled. It is a field rather than an argument because every line
        /// of an item shares it, and threading it through every helper would put it in the way of everything else.
        /// </summary>
        private string _style = StrokeDashes.Solid;

        /// <summary>Draws <paramref name="body"/> in an item's own stroke style, and puts the style back after.</summary>
        private void Styled(string style, Action body)
        {
            string previous = _style;
            _style = style;
            try
            {
                body();
            }
            finally
            {
                _style = previous;
            }
        }

        /// <summary>
        /// A picture on the sheet, drawn about the point it stands at — KiCad centres one on its position, and how
        /// big it is comes from the picture itself: its pixels at its own resolution, times the scale beside it.
        /// A picture we cannot measure is not drawn at all, rather than drawn at a size we invented.
        /// </summary>
        public void AddImage(SchImage image, int owner)
        {
            if (image is not { Data: { } data, Size: { } size })
            {
                return;
            }

            var centre = image.Position.ToDouble();
            var corner = scene.ToScene(new Vector2D(centre.X - (size.Width / 2.0), centre.Y - (size.Height / 2.0)));
            var opposite = scene.ToScene(new Vector2D(centre.X + (size.Width / 2.0), centre.Y + (size.Height / 2.0)));

            var bounds = new RectD(
                Math.Min(corner.X, opposite.X),
                Math.Min(corner.Y, opposite.Y),
                Math.Max(corner.X, opposite.X),
                Math.Max(corner.Y, opposite.Y));

            scene.Layer(LayerStyle.Sch.Symbol).Images.Add(new ImagePrim(bounds, data, owner));
            scene.GrowOwner(owner, bounds);
        }

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
            DrawingSheet.Draw(
                paper,
                scene.Schematic.TitleBlock,
                scene.Schematic.Paper,
                scene.Frame,
                new DrawingSheetLayers(LayerStyle.Sch.Frame, scene.Layer, scene.ToScene));
        }

        public void Add(SchItem item)
        {
            switch (item)
            {
                case SchWire wire:
                    Styled(wire.StrokeStyle, () => AddWire(wire, scene.AddOwner(wire)));
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
                case SchText { IsBox: true } box:
                    AddTextBox(box, scene.AddOwner(box));
                    break;
                case SchText text:
                    Text(LayerStyle.Sch.Text, text.Shown, text.Position.ToDouble(), text.TextHeight, text.Font, text.Angle,
                        text.Alignment, scene.AddOwner(text));
                    break;
                case SchGraphic graphic:
                    Styled(graphic.StrokeStyle, () =>
                        AddGraphic(graphic, Transform2D.Identity, LayerStyle.Sch.Symbol, scene.AddOwner(graphic)));
                    break;
                case SymbolInstance symbol:
                    AddSymbol(symbol, scene.AddOwner(symbol));
                    break;
                case SchImage image:
                    AddImage(image, scene.AddOwner(image));
                    break;
                case SchRuleArea area:
                    AddRuleArea(area, scene.AddOwner(area));
                    break;
                case SchTable table:
                    AddTable(table, scene.AddOwner(table));
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
            Text(LayerStyle.Sch.Label, label.Shown, label.Position.ToDouble(), label.TextHeight, label.Font, label.Angle, label.Alignment, owner);
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
                    // A shape inside a part is styled like any other: how it is drawn should not depend on whether
                    // it was drawn on the sheet or in the library it came from.
                    Styled(graphic.StrokeStyle, () => AddGraphic(graphic, t, LayerStyle.Sch.Symbol, owner));
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

                // A field the file hides is drawn on the hidden layer, which is off until somebody asks for it.
                if (value.Length > 0)
                {
                    Text(field.IsHidden ? LayerStyle.Sch.HiddenField : LayerStyle.Sch.Field, value,
                        field.Position.ToDouble(), field.TextHeight, field.Font, field.Angle, field.Alignment, owner);
                }
            }
        }

        public void AddLibrarySymbol(LibSymbol shown, LibSymbol body, int unit, int bodyStyle)
        {
            var t = LibraryToScene;

            // KiCad marks a symbol's origin with a small cross: it is where the part is held when placed.
            const long arm = 1_270_000;
            Line(LayerStyle.Sch.Frame, new Vector2D(-arm, 0), new Vector2D(arm, 0), 0, OutlineLoops.NoOwner);
            Line(LayerStyle.Sch.Frame, new Vector2D(0, -arm), new Vector2D(0, arm), 0, OutlineLoops.NoOwner);

            foreach (var graphic in body.GraphicsOf(unit, bodyStyle))
            {
                Styled(graphic.StrokeStyle, () => AddGraphic(graphic, t, LayerStyle.Sch.Symbol, scene.AddOwner(graphic)));
            }

            foreach (var pin in body.PinsOf(unit, bodyStyle))
            {
                AddPin(pin, body, t, scene.AddOwner(pin));
            }

            // Fields stand where the library puts them, turned over like everything else; their text stays upright.
            foreach (var field in shown.Fields)
            {
                if (field.Value.Length > 0)
                {
                    Text(field.IsHidden ? LayerStyle.Sch.HiddenField : LayerStyle.Sch.Field, field.Value,
                        t.Apply(field.Position.ToDouble()), field.TextHeight, field.Font, field.Angle, field.Alignment, scene.AddOwner(field));
                }
            }
        }

        private void AddPin(SchPin pin, LibSymbol definition, Transform2D toSheet, int owner)
        {
            // A hidden pin is drawn on its own layer rather than left out, so that showing it is a switch and not a
            // redrawing of the sheet. The layer is off until somebody asks for it.
            string wire = pin.IsHidden ? LayerStyle.Sch.HiddenPin : LayerStyle.Sch.Pin;
            string words = pin.IsHidden ? LayerStyle.Sch.HiddenPin : LayerStyle.Sch.PinText;

            var root = toSheet.Apply(pin.Position.ToDouble());
            var tip = toSheet.Apply(pin.EndPoint.ToDouble());
            LineScene(wire, root, tip, SymbolWidth, owner);

            // Text reads left to right whatever the pin direction: horizontal pins keep 0°, vertical ones turn 90°.
            bool vertical = Math.Abs(tip.Y - root.Y) > Math.Abs(tip.X - root.X);
            double angle = vertical ? 90 : 0;

            if (definition.ShowPinNumbers && pin.Number.Length > 0)
            {
                var middle = new Vector2D((root.X + tip.X) / 2, (root.Y + tip.Y) / 2);
                var offset = vertical ? new Vector2D(-pin.NumberHeight * 0.7, 0) : new Vector2D(0, -pin.NumberHeight * 0.7);
                TextScene(words, pin.Number, middle + offset, pin.NumberHeight, pin.NumberFont, angle, ("center", "center"), owner);
            }

            if (definition.ShowPinNames && pin.Name is { Length: > 0 } name && name != "~")
            {
                double dx = Math.Sign(root.X - tip.X) * definition.PinNameOffset;
                double dy = Math.Sign(root.Y - tip.Y) * definition.PinNameOffset;
                var anchor = new Vector2D(root.X + dx, root.Y + dy);
                var alignment = vertical
                    ? (dy > 0 ? ("left", "center") : ("right", "center"))
                    : (dx > 0 ? ("left", "center") : ("right", "center"));
                TextScene(words, name, anchor, pin.NameHeight, pin.NameFont, angle, alignment, owner);
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
                if (field.Value.Length > 0)
                {
                    Text(field.IsHidden ? LayerStyle.Sch.HiddenField : LayerStyle.Sch.Field, field.Value,
                        field.Position.ToDouble(), field.TextHeight, field.Font, field.Angle, field.Alignment, owner);
                }
            }

            foreach (var pin in sheet.Pins)
            {
                Text(LayerStyle.Sch.Label, pin.Name, pin.Position.ToDouble(), pin.TextHeight, pin.Font, pin.Angle, ("left", "center"), owner);
            }
        }

        /// <summary>
        /// A table: the text of every cell, and the lines between them. KiCad draws the lines cell by cell rather
        /// than from the grid — each cell draws its own right and bottom edge unless it reaches the table's edge —
        /// which is what makes a cell spanning two columns leave out the line it spans.
        ///
        /// The first row's lines are the border's rather than the separators', when the table asks for a header.
        /// </summary>
        private void AddTable(SchTable table, int owner)
        {
            if (table.Cells.Count == 0)
            {
                return;
            }

            long right = table.Cells.Max(c => c.Position.X + c.Size.X);
            long bottom = table.Cells.Max(c => c.Position.Y + c.Size.Y);
            long left = table.Cells.Min(c => c.Position.X);
            long top = table.Cells.Min(c => c.Position.Y);

            foreach (var cell in table.Cells)
            {
                AddTableCell(cell, owner);
            }

            long borderWidth = table.BorderWidth > 0 ? table.BorderWidth : SymbolWidth;
            long separatorWidth = table.SeparatorWidth > 0 ? table.SeparatorWidth : SymbolWidth;

            foreach (var cell in table.Cells)
            {
                var corner = cell.Position;
                var far = new Vector2L(corner.X + cell.Size.X, corner.Y + cell.Size.Y);
                bool header = table.HasHeaderSeparator && corner.Y == top;

                if (far.X < right && (header || table.SeparatesColumns))
                {
                    Styled(header ? table.BorderStyle : table.SeparatorStyle, () =>
                        Line(LayerStyle.Sch.Text, new Vector2D(far.X, corner.Y), far.ToDouble(),
                            header ? borderWidth : separatorWidth, owner));
                }

                if (far.Y < bottom && (header || table.SeparatesRows))
                {
                    Styled(header ? table.BorderStyle : table.SeparatorStyle, () =>
                        Line(LayerStyle.Sch.Text, new Vector2D(corner.X, far.Y), far.ToDouble(),
                            header ? borderWidth : separatorWidth, owner));
                }
            }

            if (table.HasBorder)
            {
                Vector2D[] corners = [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
                Styled(table.BorderStyle, () =>
                    Outline(LayerStyle.Sch.Text, corners, borderWidth, false, Transform2D.Identity, owner));
            }
        }

        /// <summary>
        /// A note in a box of its own: the box is drawn round it, in its own stroke, and the words are laid inside
        /// by the margins. Drawing only the words would leave the box off the sheet, which is most of what one is.
        /// </summary>
        private void AddTextBox(SchText box, int owner)
        {
            var corner = box.Position.ToDouble();
            var size = box.Size.ToDouble();
            if (size.X != 0 && size.Y != 0)
            {
                Vector2D[] corners =
                [
                    corner, new(corner.X + size.X, corner.Y),
                    new(corner.X + size.X, corner.Y + size.Y), new(corner.X, corner.Y + size.Y),
                ];

                long width = box.StrokeWidth > 0 ? box.StrokeWidth : SymbolWidth;
                Styled(box.StrokeStyle, () => Outline(LayerStyle.Sch.Text, corners, width, false, Transform2D.Identity, owner));
            }

            if (box.Shown.Length > 0)
            {
                InBox(LayerStyle.Sch.Text, box.Shown, box.Position, box.Size, box.Margins, box.Alignment,
                    box.TextHeight, box.Font, box.Angle, owner);
            }
        }

        /// <summary>
        /// Words laid inside a box by its margins and its justification: at the left margin when they read from the
        /// left, at the right margin when from the right, in the middle when centred — and the same down the box.
        /// </summary>
        private void InBox(
            string layer,
            string words,
            Vector2L corner,
            Vector2L size,
            (long Left, long Top, long Right, long Bottom) margins,
            (string Horizontal, string Vertical) alignment,
            long height,
            TextFont font,
            double angle,
            int owner)
        {
            double x = alignment.Horizontal switch
            {
                "right" => corner.X + size.X - margins.Right,
                "center" => corner.X + (size.X / 2.0),
                _ => corner.X + margins.Left,
            };

            double y = alignment.Vertical switch
            {
                "bottom" => corner.Y + size.Y - margins.Bottom,
                "center" => corner.Y + (size.Y / 2.0),
                _ => corner.Y + margins.Top,
            };

            Text(layer, words, new Vector2D(x, y), height, font, angle, alignment, owner);
        }

        /// <summary>
        /// The text of one cell, kept inside the box by its own margins and put where its justification asks: at the
        /// left margin when it reads from the left, at the right margin when from the right, in the middle when
        /// centred — and the same down the box.
        /// </summary>
        private void AddTableCell(SchTableCell cell, int owner)
        {
            if (cell.Shown.Length == 0)
            {
                return;
            }

            InBox(LayerStyle.Sch.Text, cell.Shown, cell.Position, cell.Size, cell.Margins, cell.Alignment,
                cell.TextHeight, cell.Font, cell.Angle, owner);
        }

        /// <summary>
        /// An area the design rules are told about. Its outline is a closed shape — the file lists its corners once
        /// and means the boundary to come back round, so drawing it as an open polyline would leave a gap along the
        /// side that matters most.
        /// </summary>
        private void AddRuleArea(SchRuleArea area, int owner)
        {
            if (area.Outline is not { } outline)
            {
                return;
            }

            long width = outline.StrokeWidth > 0 ? outline.StrokeWidth : SymbolWidth;
            Styled(outline.StrokeStyle, () =>
            {
                if (outline.Kind is SchShapeKind.Polyline or SchShapeKind.Bezier)
                {
                    var shape = Array.ConvertAll(outline.Points, p => p.ToDouble());
                    Outline(
                        LayerStyle.Sch.RuleArea,
                        outline.Kind == SchShapeKind.Bezier ? BezierMath.Tessellate(shape) : shape,
                        width,
                        outline.IsFilled,
                        Transform2D.Identity,
                        owner);
                    return;
                }

                AddGraphic(outline, Transform2D.Identity, LayerStyle.Sch.RuleArea, owner);
            });
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
                    // A curve is drawn as the curve: its four points are the frame it hangs in, and only the first
                    // and last are on it, so joining them all with straight lines draws the wrong shape.
                    var points = graphic.Kind == SchShapeKind.Bezier
                        ? BezierMath.Tessellate(Array.ConvertAll(graphic.Points, p => p.ToDouble()))
                        : Array.ConvertAll(graphic.Points, p => p.ToDouble());
                    if (graphic.IsFilled && points.Length >= 3)
                    {
                        Polygon(layer, points, toSheet, owner);
                    }

                    Polyline(layer, points, width, toSheet, owner);
                    break;

                case SchShapeKind.Ellipse:
                case SchShapeKind.EllipseArc:
                    var ellipse = Ellipse(graphic);
                    if (graphic.IsFilled && graphic.Kind == SchShapeKind.Ellipse)
                    {
                        Polygon(layer, ellipse, toSheet, owner);
                    }

                    Polyline(layer, ellipse, width, toSheet, owner);
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

            // A dashed line is cut here rather than at every call site, so nothing that draws a line has to know.
            if (StrokeDashes.Pattern(_style, width) is { } pattern)
            {
                foreach (var (from, to) in StrokeDashes.Cut(p, q, pattern))
                {
                    geometry.Lines.Add(new LinePrim(from, to, width, owner));
                }
            }
            else
            {
                geometry.Lines.Add(new LinePrim(p, q, width, owner));
            }

            // The whole line is what the item covers, gaps and all; a selection must not end at the last dash.
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

        /// <summary>
        /// An ellipse as a run of points: KiCad turns one by its rotation and, for an arc, draws only the sweep
        /// between its two angles. A whole ellipse comes back round to where it started, so the run closes.
        /// </summary>
        private static Vector2D[] Ellipse(SchGraphic graphic)
        {
            const int steps = 64;
            double major = graphic.MajorRadius, minor = graphic.MinorRadius;
            var centre = graphic.Center.ToDouble();
            double turn = graphic.RotationAngle * Math.PI / 180;
            double cos = Math.Cos(turn), sin = Math.Sin(turn);

            bool whole = graphic.Kind == SchShapeKind.Ellipse;
            var (from, to) = whole ? (0.0, 360.0) : graphic.SweepAngles;
            if (!whole && to <= from)
            {
                to += 360;
            }

            var points = new Vector2D[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double angle = (from + ((to - from) * i / steps)) * Math.PI / 180;
                double x = major * Math.Cos(angle), y = minor * Math.Sin(angle);
                points[i] = new Vector2D(centre.X + (x * cos) - (y * sin), centre.Y + (x * sin) + (y * cos));
            }

            return points;
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

        private void Text(string layer, string value, Vector2D anchorNm, long heightNm, TextFont font, double angle,
            (string Horizontal, string Vertical) alignment, int owner) =>
            TextScene(layer, value, anchorNm, heightNm, font, angle, alignment, owner);

        private void TextScene(string layer, string value, Vector2D anchorNm, long heightNm, TextFont font, double angle,
            (string Horizontal, string Vertical) alignment, int owner)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // KiCad's pen for schematic text: as written, or a fifth of the size when bold, an eighth otherwise, and
            // never more than a quarter of it.
            double height = heightNm > 0 ? heightNm : 1_270_000;
            double pen = font.Thickness is > 1 ? font.Thickness.Value : height / (font.Bold ? 5 : 8);
            var style = new TextStyle(
                height,
                height,
                Math.Min(pen, height / 4),
                alignment.Horizontal switch { "left" => TextHAlign.Left, "right" => TextHAlign.Right, _ => TextHAlign.Center },
                alignment.Vertical switch { "top" => TextVAlign.Top, "bottom" => TextVAlign.Bottom, _ => TextVAlign.Center },
                // Schematic text is never upside down: 180° reads as 0°, 270° as 90°.
                angle % 180,
                false,
                font.Italic,
                1.0);

            TextShapes.Emit(scene.Layer(layer), value, anchorNm, style, font, scene.ToScene, b => scene.GrowOwner(owner, b), owner);
        }
    }
}
