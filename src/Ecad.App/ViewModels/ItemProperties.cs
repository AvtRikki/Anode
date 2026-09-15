using System.Globalization;
using Ecad.Geometry;
using Ecad.KiCad;

namespace Ecad.App.ViewModels;

/// <summary>Read-only property rows for the selected board item.</summary>
internal static class ItemProperties
{
    public static IEnumerable<PropertyRow> For(BoardItem item)
    {
        switch (item)
        {
            case Pad pad:
                yield return Row("Type", "Pad");
                yield return Row("Footprint", pad.Footprint.Reference ?? pad.Footprint.LibId);
                yield return Row("Number", pad.Number);
                yield return Row("Pad type", pad.Type.ToString());
                yield return Row("Shape", pad.Shape.ToString());
                yield return Row("Position", Point(pad.BoardPosition));
                yield return Row("Size", Point(pad.Size, " × "));
                yield return Row("Orientation", Deg(pad.Orientation));
                if (pad.Drill is { } drill)
                {
                    yield return Row("Drill", drill.IsOval ? Point(drill.Size, " × ") : Mm(drill.Size.X));
                }

                yield return Row("Layers", string.Join(", ", pad.LayerNames));
                yield return Row("Net", NetName(pad.Net));
                break;

            case Segment s:
                yield return Row("Type", "Track");
                yield return Row("Start", Point(s.Start));
                yield return Row("End", Point(s.End));
                yield return Row("Width", Mm(s.Width));
                yield return Row("Length", Mm((long)(s.End - s.Start).Length));
                yield return Row("Layer", string.Join(", ", s.LayerNames));
                yield return Row("Net", NetName(s.Net));
                break;

            case TrackArc a:
                yield return Row("Type", "Arc track");
                yield return Row("Start", Point(a.Start));
                yield return Row("Mid", Point(a.Mid));
                yield return Row("End", Point(a.End));
                yield return Row("Width", Mm(a.Width));
                yield return Row("Layer", string.Join(", ", a.LayerNames));
                yield return Row("Net", NetName(a.Net));
                break;

            case Via v:
                yield return Row("Type", "Via");
                yield return Row("Position", Point(v.Position));
                yield return Row("Size", Mm(v.Size));
                yield return Row("Drill", Mm(v.Drill));
                yield return Row("Via type", v.ViaType);
                yield return Row("Layers", string.Join(" → ", v.LayerNames));
                yield return Row("Net", NetName(v.Net));
                break;

            case Zone z:
                yield return Row("Type", z.IsRuleArea ? "Rule area" : "Zone");
                if (z.Name is { } name)
                {
                    yield return Row("Name", name);
                }

                yield return Row("Layers", string.Join(", ", z.LayerNames));
                yield return Row("Net", NetName(z.Net));
                yield return Row("Fill polygons", z.FilledPolygons.Count().ToString(CultureInfo.InvariantCulture));
                break;

            case Shape sh:
                yield return Row("Type", $"Graphic {sh.Kind.ToString().ToLowerInvariant()}");
                if (sh.ToBoard != Transform2D.Identity)
                {
                    yield return Row("Owner", "Footprint");
                }

                yield return Row("Layer", string.Join(", ", sh.LayerNames));
                yield return Row("Stroke width", Mm(sh.StrokeWidth));
                yield return Row("Filled", sh.IsFilled ? "yes" : "no");
                break;

            case Text t:
                yield return Row("Type", t.FieldName is { } field ? $"Text ({field})" : "Text");
                yield return Row("Text", t.Value);
                yield return Row("Position", Point(t.BoardPosition));
                yield return Row("Angle", Deg(t.BoardAngle));
                yield return Row("Height", Mm(t.Size.Y));
                yield return Row("Layer", t.LayerName ?? "");
                break;
        }

        if (item.Uuid is { } uuid)
        {
            yield return Row("UUID", uuid);
        }
    }

    private static PropertyRow Row(string name, string value) => new(name, value);

    private static string Mm(long nm) => $"{Units.NmToMm(nm).ToString("0.####", CultureInfo.InvariantCulture)} mm";

    private static string Point(Vector2L p, string separator = ", ") =>
        $"{Units.NmToMm(p.X).ToString("0.####", CultureInfo.InvariantCulture)}{separator}{Units.NmToMm(p.Y).ToString("0.####", CultureInfo.InvariantCulture)} mm";

    private static string Deg(double degrees) => $"{degrees.ToString("0.##", CultureInfo.InvariantCulture)}°";

    private static string NetName(Net? net) => net is null || net.IsUnconnected ? "—" : net.Name;
}
