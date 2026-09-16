using System.Globalization;
using Anode.Geometry;
using Anode.Kicad;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>Turns the selected sheet item into inspector rows.</summary>
internal static class SchItemProperties
{
    public static (string Title, string? Subtitle, string? Tag) Header(SchItem item) => item switch
    {
        SymbolInstance symbol => (symbol.Reference ?? symbol.LibId, symbol.Value, symbol.IsDnp ? Tr.T("sch.property.dnp") : null),
        SchWire wire => (Tr.T(wire.IsBus ? "sch.item.bus" : "sch.item.wire"), Length(wire), null),
        SchBusEntry => (Tr.T("sch.item.bus"), null, null),
        SchJunction => (Tr.T("sch.item.junction"), null, null),
        SchNoConnect => (Tr.T("sch.item.noConnect"), null, null),
        SchLabel label => (label.Text, Tr.T(label.Kind switch
        {
            SchLabelKind.Global => "sch.item.globalLabel",
            SchLabelKind.Hierarchical => "sch.item.hierarchicalLabel",
            SchLabelKind.NetClassFlag => "sch.item.netclassFlag",
            _ => "sch.item.label",
        }), null),
        SchText text => (Tr.T("sch.item.text"), Shorten(text.Text), null),
        SchSheet sheet => (sheet.SheetName ?? Tr.T("sch.item.sheet"), sheet.SheetFile, null),
        SchGraphic graphic => (Tr.T("sch.item.graphic"), graphic.Kind.ToString().ToLowerInvariant(), null),
        _ => (Tr.T("sch.item.other"), null, null),
    };

    public static IEnumerable<PropertyItem> For(SchItem item)
    {
        switch (item)
        {
            case SymbolInstance symbol:
                yield return Row("reference", symbol.Reference ?? None);
                yield return Row("value", symbol.Value ?? None);
                yield return Row("library", symbol.LibId);
                if (symbol.Footprint is { Length: > 0 } footprint)
                {
                    yield return Row("footprint", footprint);
                }

                yield return Row("position", Point(symbol.Position));
                yield return Row("angle", Deg(symbol.Angle));
                yield return Row("unit", symbol.Unit.ToString(CultureInfo.InvariantCulture));
                if (symbol.Mirror is { } mirror)
                {
                    yield return Row("mirror", mirror);
                }

                yield return Row("dnp", Tr.T(symbol.IsDnp ? "sch.value.yes" : "sch.value.no"));
                if (symbol.Definition is { } definition)
                {
                    yield return Row("pins", definition.PinsOf(symbol.Unit, symbol.BodyStyle).Count().ToString(CultureInfo.InvariantCulture));
                }

                break;

            case SchWire wire:
                var points = wire.Points;
                if (points.Length > 0)
                {
                    yield return Row("position", Point(points[0]));
                    yield return Row("size", Point(points[^1]));
                    yield return Row("length", Mm((long)Total(points)));
                }

                break;

            case SchLabel label:
                yield return Row("text", label.Text);
                yield return Row("position", Point(label.Position));
                yield return Row("angle", Deg(label.Angle));
                if (label.Kind == SchLabelKind.Hierarchical || label.Kind == SchLabelKind.Global)
                {
                    yield return Row("shape", label.Shape);
                }

                break;

            case SchText text:
                yield return Row("text", text.Text);
                yield return Row("position", Point(text.Position));
                yield return Row("angle", Deg(text.Angle));
                break;

            case SchSheet sheet:
                yield return Row("name", sheet.SheetName ?? None);
                yield return Row("file", sheet.SheetFile ?? None);
                yield return Row("position", Point(sheet.Position));
                yield return Row("size", Point(sheet.Size, " × "));
                yield return Row("pins", sheet.Pins.Count.ToString(CultureInfo.InvariantCulture));
                break;

            case SchJunction junction:
                yield return Row("position", Point(junction.Position));
                yield return Row("size", Mm(junction.Diameter));
                break;

            case SchNoConnect noConnect:
                yield return Row("position", Point(noConnect.Position));
                break;

            case SchGraphic graphic:
                yield return Row("shape", graphic.Kind.ToString().ToLowerInvariant());
                yield return Row("position", Point(graphic.Kind == SchShapeKind.Circle ? graphic.Center : graphic.Start));
                break;
        }

        if (item.Uuid is { } uuid)
        {
            yield return Row("uuid", uuid);
        }
    }

    private static string None => Tr.T("sch.value.none");

    private static PropertyItem Row(string nameKey, string value) => new(Tr.T($"sch.property.{nameKey}"), value);

    private static string? Length(SchWire wire) => wire.Points is { Length: > 1 } points ? Mm((long)Total(points)) : null;

    private static double Total(Vector2L[] points)
    {
        double sum = 0;
        for (int i = 1; i < points.Length; i++)
        {
            sum += (points[i] - points[i - 1]).Length;
        }

        return sum;
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..39] + "…";

    private static string Mm(long nm) => $"{Units.NmToMm(nm).ToString("0.###", CultureInfo.InvariantCulture)} {Tr.T("sch.units.mm")}";

    private static string Point(Vector2L p, string separator = ", ") =>
        $"{Units.NmToMm(p.X).ToString("0.###", CultureInfo.InvariantCulture)}{separator}"
        + $"{Units.NmToMm(p.Y).ToString("0.###", CultureInfo.InvariantCulture)} {Tr.T("sch.units.mm")}";

    private static string Deg(double degrees) => $"{degrees.ToString("0.##", CultureInfo.InvariantCulture)}°";
}
