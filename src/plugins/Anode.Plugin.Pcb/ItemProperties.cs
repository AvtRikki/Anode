using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render.Fonts;
using Anode.Sdk;

namespace Anode.Plugin.Pcb;

/// <summary>Turns the selected board item into inspector rows: serif names, monospaced values.</summary>
internal static class ItemProperties
{
    /// <summary>
    /// The three lines above the blocks: the name, the kind as a chip beside it, and the layer it lives on — which
    /// the document completes with the file. What the object is called comes first; what it is, second.
    /// </summary>
    public static (string Title, string? Subtitle, string? Tag) Header(BoardItem item) => item switch
    {
        Footprint fp => (fp.Reference ?? fp.LibId, Tr.T(fp.IsOnBack ? "pcb.side.bottom" : "pcb.side.top"), Tr.T("pcb.item.footprint")),
        Pad pad => ($"{pad.Footprint.Reference ?? pad.Footprint.LibId}.{pad.Number}", string.Join(", ", pad.LayerNames), Tr.T("pcb.item.pad")),
        Segment s => (Tr.T("pcb.item.track"), s.LayerNames.FirstOrDefault(), NetTag(s.Net)),
        TrackArc a => (Tr.T("pcb.item.arc"), a.LayerNames.FirstOrDefault(), NetTag(a.Net)),
        Via v => (Tr.T("pcb.item.via"), string.Join(" → ", v.LayerNames), v.ViaType),
        Zone z => (z.Name ?? Tr.T(z.IsRuleArea ? "pcb.item.ruleArea" : "pcb.item.zone"),
            string.Join(", ", z.LayerNames), Tr.T(z.IsRuleArea ? "pcb.item.ruleArea" : "pcb.item.zone")),
        Shape sh => (Tr.T("pcb.item.graphic", Kind(sh.Kind)), sh.LayerNames.FirstOrDefault(), null),
        Text t => (t.Value, t.LayerName, t.FieldName is { } field ? Tr.T("pcb.item.textField", field) : Tr.T("pcb.item.text")),
        _ => (Tr.T("pcb.item.other"), null, null),
    };

    /// <summary>
    /// The board's inspector, in the blocks the design lays down. Values are computed — a board is read and moved,
    /// but its numbers are not yet written through this panel — except how a text is set, which is chosen here.
    /// </summary>
    public static IEnumerable<InspectorBlock> Blocks(BoardItem item, Action<string, Action>? edit = null)
    {
        switch (item)
        {
            case Footprint fp:
                yield return new InspectorBlock(Block("identity"),
                [
                    Computed("reference", fp.Reference ?? None),
                    Computed("value", fp.Value ?? None),
                    Computed("library", fp.LibId),
                ]);

                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("position", Pair(fp.Position)),
                    Computed("angle", Deg(fp.Orientation)),
                    Computed("side", Tr.T(fp.IsOnBack ? "pcb.side.bottom" : "pcb.side.top")),
                ]);

                if (fp.Pads.Count > 0)
                {
                    yield return new InspectorBlock(
                        Tr.T("pcb.block.connectionsOf", $"{fp.Pads.Count} {Tr.Plural("pcb.pad", fp.Pads.Count)}"),
                        [.. fp.Pads.Select((pad, i) => new InspectorRow(
                            // A pad that carries no number of its own is still the n-th pad of the part.
                            pad.Number is { Length: > 0 } number ? number : (i + 1).ToString(CultureInfo.InvariantCulture),
                            string.Empty)
                        {
                            // What it reaches goes to the right, as a connection reads: number here, net there.
                            Trailing = NetName(pad.Net),
                            IsUnresolved = pad.Net is null || pad.Net.IsUnconnected,
                        })])
                    {
                        IsConnections = true,
                    };
                }

                break;

            case Pad pad:
                yield return new InspectorBlock(Block("identity"),
                [
                    Computed("component", pad.Footprint.Reference ?? pad.Footprint.LibId),
                    Computed("number", pad.Number),
                    Computed("type", pad.Type.ToString()),
                    Computed("shape", pad.Shape.ToString()),
                ]);

                yield return new InspectorBlock(Block("electrics"), [Computed("net", NetName(pad.Net))]);

                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("position", Pair(pad.BoardPosition)),
                    Computed("size", Pair(pad.Size, " × ")),
                    Computed("angle", Deg(pad.Orientation)),
                    .. pad.Drill is { } drill
                        ? new[] { Computed("drill", drill.IsOval ? Pair(drill.Size, " × ") : Mm(drill.Size.X)) }
                        : [],
                    Computed("layers", string.Join(", ", pad.LayerNames)),
                ]);

                break;

            case Segment s:
                yield return new InspectorBlock(Block("electrics"), [Computed("net", NetName(s.Net))]);
                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("width", Mm(s.Width)),
                    Computed("layer", string.Join(", ", s.LayerNames)),
                    Computed("length", Mm((long)(s.End - s.Start).Length)),
                    Computed("start", Pair(s.Start)),
                    Computed("end", Pair(s.End)),
                ]);

                break;

            case TrackArc a:
                yield return new InspectorBlock(Block("electrics"), [Computed("net", NetName(a.Net))]);
                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("width", Mm(a.Width)),
                    Computed("layer", string.Join(", ", a.LayerNames)),
                    Computed("start", Pair(a.Start)),
                    Computed("middle", Pair(a.Mid)),
                    Computed("end", Pair(a.End)),
                ]);

                break;

            case Via v:
                yield return new InspectorBlock(Block("electrics"),
                [
                    Computed("net", NetName(v.Net)),
                    Computed("type", v.ViaType),
                ]);

                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("drill", Mm(v.Drill)),
                    Computed("diameter", Mm(v.Size)),
                    Computed("position", Pair(v.Position)),
                    Computed("layers", string.Join(" → ", v.LayerNames)),
                ]);

                break;

            case Zone z:
                yield return new InspectorBlock(Block("electrics"), [Computed("net", NetName(z.Net))]);
                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("layers", string.Join(", ", z.LayerNames)),
                    Computed("polygons", z.FilledPolygons.Count().ToString(CultureInfo.InvariantCulture)),
                ]);

                break;

            case Shape sh:
                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("kind", Kind(sh.Kind)),
                    Computed("layer", string.Join(", ", sh.LayerNames)),
                    Computed("stroke", Mm(sh.StrokeWidth)),
                    Computed("filled", Tr.T(sh.IsFilled ? "pcb.value.yes" : "pcb.value.no")),
                ]);

                break;

            case Text t:
                yield return new InspectorBlock(Block("identity"), [Computed("text", t.Value)]);
                if (Typography(t, edit) is { } type)
                {
                    yield return type;
                }

                yield return new InspectorBlock(Block("geometry"),
                [
                    Computed("position", Pair(t.BoardPosition)),
                    Computed("angle", Deg(t.BoardAngle)),
                    Computed("height", Mm(t.Size.Y)),
                    Computed("layer", t.LayerName ?? None),
                ]);

                break;
        }
    }

    /// <summary>
    /// How a text is set: its face — the stroke font, or any this machine or the board itself has — and whether it
    /// is bold, italic or both, the four KiCad offers as one choice. Null for anything but a text.
    /// </summary>
    public static InspectorBlock? Typography(BoardItem item, Action<string, Action>? edit)
    {
        if (item is not Text text || edit is null)
        {
            return null;
        }

        var font = TextFont.Read(text.Node.Find("effects"));
        string stroke = Tr.T("pcb.property.strokeFont");

        return new InspectorBlock(Block("typography"),
        [
            new InspectorRow(Tr.T("pcb.property.font"), font.Face ?? stroke)
            {
                Choices = [stroke, .. OutlineText.Families()],
                Commit = v => edit(Tr.T("pcb.property.font"), () => FontWrites.SetFace(text.Node, v == stroke ? null : v)),
            },
            new InspectorRow(Tr.T("pcb.property.style"), Style(font.Bold, font.Italic))
            {
                Choices = [.. Styles.Select(s => Tr.T($"pcb.style.{s.Key}"))],
                Commit = v =>
                {
                    if (Styles.FirstOrDefault(s => Tr.T($"pcb.style.{s.Key}") == v) is { Key: not null } picked)
                    {
                        edit(Tr.T("pcb.property.style"), () =>
                        {
                            FontWrites.SetBold(text.Node, picked.Bold);
                            FontWrites.SetItalic(text.Node, picked.Italic);
                        });
                    }
                },
            },
        ]);
    }

    private static readonly (string Key, bool Bold, bool Italic)[] Styles =
        [("normal", false, false), ("bold", true, false), ("italic", false, true), ("boldItalic", true, true)];

    private static string Style(bool bold, bool italic) =>
        Tr.T($"pcb.style.{Styles.First(s => s.Bold == bold && s.Italic == italic).Key}");

    private static string Block(string key) => Tr.T($"pcb.block.{key}");

    private static InspectorRow Computed(string nameKey, string value) => new(Tr.T($"pcb.property.{nameKey}"), value);

    /// <summary>A pair of millimetres, as the inspector writes it: no unit, because a pair is always millimetres.</summary>
    private static string Pair(Vector2L p, string separator = " / ") =>
        $"{Units.NmToMm(p.X).ToString("0.####", CultureInfo.InvariantCulture)}{separator}"
        + $"{Units.NmToMm(p.Y).ToString("0.####", CultureInfo.InvariantCulture)}";

    public static IEnumerable<PropertyItem> For(BoardItem item)
    {
        switch (item)
        {
            case Footprint fp:
                yield return Row("reference", fp.Reference ?? None);
                yield return Row("value", fp.Value ?? None);
                yield return Row("library", fp.LibId);
                yield return Row("position", Point(fp.Position));
                yield return Row("angle", Deg(fp.Orientation));
                yield return Row("side", Tr.T(fp.IsOnBack ? "pcb.side.bottom" : "pcb.side.top"));
                yield return Row("pads", fp.Pads.Count.ToString(CultureInfo.InvariantCulture));
                break;

            case Pad pad:
                yield return Row("component", pad.Footprint.Reference ?? pad.Footprint.LibId);
                yield return Row("number", pad.Number);
                yield return Row("type", pad.Type.ToString());
                yield return Row("shape", pad.Shape.ToString());
                yield return Row("position", Point(pad.BoardPosition));
                yield return Row("size", Point(pad.Size, " × "));
                yield return Row("angle", Deg(pad.Orientation));
                if (pad.Drill is { } drill)
                {
                    yield return Row("drill", drill.IsOval ? Point(drill.Size, " × ") : Mm(drill.Size.X));
                }

                yield return Row("layers", string.Join(", ", pad.LayerNames));
                yield return Row("net", NetName(pad.Net));
                break;

            case Segment s:
                yield return Row("start", Point(s.Start));
                yield return Row("end", Point(s.End));
                yield return Row("width", Mm(s.Width));
                yield return Row("length", Mm((long)(s.End - s.Start).Length));
                yield return Row("layer", string.Join(", ", s.LayerNames));
                yield return Row("net", NetName(s.Net));
                break;

            case TrackArc a:
                yield return Row("start", Point(a.Start));
                yield return Row("middle", Point(a.Mid));
                yield return Row("end", Point(a.End));
                yield return Row("width", Mm(a.Width));
                yield return Row("layer", string.Join(", ", a.LayerNames));
                yield return Row("net", NetName(a.Net));
                break;

            case Via v:
                yield return Row("position", Point(v.Position));
                yield return Row("diameter", Mm(v.Size));
                yield return Row("drill", Mm(v.Drill));
                yield return Row("type", v.ViaType);
                yield return Row("layers", string.Join(" → ", v.LayerNames));
                yield return Row("net", NetName(v.Net));
                break;

            case Zone z:
                if (z.Name is { } name)
                {
                    yield return Row("name", name);
                }

                yield return Row("layers", string.Join(", ", z.LayerNames));
                yield return Row("net", NetName(z.Net));
                yield return Row("polygons", z.FilledPolygons.Count().ToString(CultureInfo.InvariantCulture));
                break;

            case Shape sh:
                yield return Row("kind", Kind(sh.Kind));
                if (sh.ToBoard != Transform2D.Identity)
                {
                    yield return Row("owner", Tr.T("pcb.value.component"));
                }

                yield return Row("layer", string.Join(", ", sh.LayerNames));
                yield return Row("stroke", Mm(sh.StrokeWidth));
                yield return Row("filled", Tr.T(sh.IsFilled ? "pcb.value.yes" : "pcb.value.no"));
                break;

            case Text t:
                yield return Row("text", t.Value);
                yield return Row("position", Point(t.BoardPosition));
                yield return Row("angle", Deg(t.BoardAngle));
                yield return Row("height", Mm(t.Size.Y));
                yield return Row("layer", t.LayerName ?? None);
                break;
        }

        if (item.Uuid is { } uuid)
        {
            yield return Row("uuid", uuid);
        }
    }

    private static string None => Tr.T("pcb.value.none");

    private static PropertyItem Row(string nameKey, string value) => new(Tr.T($"pcb.property.{nameKey}"), value);

    private static string Kind(ShapeKind kind) => Tr.T($"pcb.shape.{kind.ToString().ToLowerInvariant()}");

    private static string Mm(long nm) => $"{Units.NmToMm(nm).ToString("0.####", CultureInfo.InvariantCulture)} {Tr.T("pcb.units.mm")}";

    private static string Point(Vector2L p, string separator = ", ") =>
        $"{Units.NmToMm(p.X).ToString("0.####", CultureInfo.InvariantCulture)}{separator}"
        + $"{Units.NmToMm(p.Y).ToString("0.####", CultureInfo.InvariantCulture)} {Tr.T("pcb.units.mm")}";

    private static string Deg(double degrees) => $"{degrees.ToString("0.##", CultureInfo.InvariantCulture)}°";

    private static string NetName(Net? net) => net is null || net.IsUnconnected ? None : net.Name;

    private static string? NetTag(Net? net) => net is null || net.IsUnconnected ? null : net.Name;
}
