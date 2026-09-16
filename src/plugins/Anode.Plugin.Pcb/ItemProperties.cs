using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Pcb;

/// <summary>Turns the selected board item into inspector rows: serif names, monospaced values.</summary>
internal static class ItemProperties
{
    /// <summary>Heading, subtitle and tag of the inspector card.</summary>
    public static (string Title, string? Subtitle, string? Tag) Header(BoardItem item) => item switch
    {
        Footprint fp => (fp.Reference ?? fp.LibId, fp.Value is { } v && v != fp.Reference ? $"{v} · {fp.LibId}" : fp.LibId,
            Tr.T(fp.IsOnBack ? "pcb.side.bottom" : "pcb.side.top")),
        Pad pad => ($"{pad.Footprint.Reference ?? pad.Footprint.LibId}.{pad.Number}", Tr.T("pcb.item.pad"), NetTag(pad.Net)),
        Segment s => (Tr.T("pcb.item.track"), s.LayerNames.FirstOrDefault(), NetTag(s.Net)),
        TrackArc a => (Tr.T("pcb.item.arc"), a.LayerNames.FirstOrDefault(), NetTag(a.Net)),
        Via v => (Tr.T("pcb.item.via"), v.ViaType, NetTag(v.Net)),
        Zone z => (Tr.T(z.IsRuleArea ? "pcb.item.ruleArea" : "pcb.item.zone"), z.Name, NetTag(z.Net)),
        Shape sh => (Tr.T("pcb.item.graphic", Kind(sh.Kind)), sh.LayerNames.FirstOrDefault(), null),
        Text t => (t.FieldName is { } field ? Tr.T("pcb.item.textField", field) : Tr.T("pcb.item.text"), t.Value, t.LayerName),
        _ => (Tr.T("pcb.item.other"), null, null),
    };

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
