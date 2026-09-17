using System.Text;
using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// New items for a sheet. Each one is written as text and parsed back, so the tree that lands in the file is the
/// same tree the reader would have produced — and then its whitespace is dropped, which tells the writer to lay the
/// item out KiCad-style at whatever depth it is attached.
/// </summary>
public static class SchNodes
{
    /// <summary>A wire (or a bus) through the given sheet points.</summary>
    public static SchWire Wire(IReadOnlyList<Vector2L> points, bool bus = false)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("A wire needs at least two points.", nameof(points));
        }

        var text = new StringBuilder();
        text.Append('(').Append(bus ? "bus" : "wire").Append(" (pts");
        foreach (var point in points)
        {
            text.Append(" (xy ").Append(KiCadNumber.FormatMm(point.X)).Append(' ').Append(KiCadNumber.FormatMm(point.Y)).Append(')');
        }

        text.Append(") (stroke (width 0) (type default)) (uuid \"").Append(Guid.NewGuid()).Append("\"))");
        return new SchWire(Fresh(text.ToString()));
    }

    /// <summary>A connection dot, with KiCad's "use the default" zeros.</summary>
    public static SchJunction Junction(Vector2L at) =>
        new(Fresh($"(junction (at {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)}) (diameter 0) (color 0 0 0 0) (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>
    /// A net name written on the sheet. The kind decides the head KiCad writes and whether a shape belongs with it:
    /// a local label is just a name, while the ones that leave the sheet carry the direction of the signal.
    /// </summary>
    public static SchLabel Label(SchLabelKind kind, string text, Vector2L at, double angle = 0, string shape = "input")
    {
        string head = kind switch
        {
            SchLabelKind.Global => "global_label",
            SchLabelKind.Hierarchical => "hierarchical_label",
            SchLabelKind.NetClassFlag => "netclass_flag",
            _ => "label",
        };

        string direction = kind is SchLabelKind.Global or SchLabelKind.Hierarchical ? $" (shape {shape})" : string.Empty;
        string length = kind is SchLabelKind.NetClassFlag ? " (length 2.54) (shape round)" : string.Empty;

        return new SchLabel(Fresh(
            $"({head} {Quote(text)}{direction}{length} (at {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)} {KiCadNumber.FormatAngle(angle)})"
            + $" (effects (font (size 1.27 1.27))) (uuid \"{Guid.NewGuid()}\"))"));
    }

    /// <summary>The little diagonal that takes a wire off a bus. The size is the step, and it carries the direction.</summary>
    public static SchBusEntry BusEntry(Vector2L at, Vector2L size) =>
        new(Fresh(
            $"(bus_entry (at {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)})"
            + $" (size {KiCadNumber.FormatMm(size.X)} {KiCadNumber.FormatMm(size.Y)})"
            + $" (stroke (width 0) (type default)) (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>Free text on the sheet — a note, not a net name.</summary>
    public static SchText Text(string text, Vector2L at, double angle = 0) =>
        new(Fresh(
            $"(text {Quote(text)} (at {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)} {KiCadNumber.FormatAngle(angle)})"
            + $" (effects (font (size 1.27 1.27))) (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>A line or a run of them, drawn on the sheet rather than wired.</summary>
    public static SchGraphic Polyline(IReadOnlyList<Vector2L> points)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("A polyline needs at least two points.", nameof(points));
        }

        var text = new StringBuilder("(polyline (pts");
        foreach (var point in points)
        {
            text.Append(" (xy ").Append(KiCadNumber.FormatMm(point.X)).Append(' ').Append(KiCadNumber.FormatMm(point.Y)).Append(')');
        }

        text.Append(')').Append(Outline).Append($" (uuid \"{Guid.NewGuid()}\"))");
        return new SchGraphic(Fresh(text.ToString()));
    }

    /// <summary>A rectangle by two opposite corners.</summary>
    public static SchGraphic Rectangle(Vector2L start, Vector2L end) =>
        new(Fresh(
            $"(rectangle (start {KiCadNumber.FormatMm(start.X)} {KiCadNumber.FormatMm(start.Y)})"
            + $" (end {KiCadNumber.FormatMm(end.X)} {KiCadNumber.FormatMm(end.Y)}){Outline} (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>A circle by its centre and radius; a schematic circle stores the radius, unlike a board one.</summary>
    public static SchGraphic Circle(Vector2L center, long radius) =>
        new(Fresh(
            $"(circle (center {KiCadNumber.FormatMm(center.X)} {KiCadNumber.FormatMm(center.Y)})"
            + $" (radius {KiCadNumber.FormatMm(radius)}){Outline} (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>What every sheet graphic carries: the default stroke, and no fill.</summary>
    private const string Outline = " (stroke (width 0) (type default)) (fill (type none))";

    /// <summary>Text as KiCad writes it: quoted, with quotes and backslashes escaped.</summary>
    private static string Quote(string text) => SEscape.Quote(text);

    /// <summary>The cross that says a pin is meant to stay unconnected.</summary>
    public static SchNoConnect NoConnect(Vector2L at) =>
        new(Fresh($"(no_connect (at {KiCadNumber.FormatMm(at.X)} {KiCadNumber.FormatMm(at.Y)}) (uuid \"{Guid.NewGuid()}\"))"));

    /// <summary>
    /// A subtree from somewhere else — a definition copied out of a library — made ready to live in this file: its
    /// own copy, with the whitespace of its old home dropped so the writer lays it out where it now sits.
    /// </summary>
    public static SList Adopt(SList node)
    {
        var copy = node.CloneList();
        Strip(copy);
        return copy;
    }

    private static SList Fresh(string text)
    {
        var root = SDocument.Parse(text).Root;
        Strip(root);
        return root;
    }

    /// <summary>Whitespace of a node built here means nothing; null lets the writer indent it where it ends up.</summary>
    private static void Strip(SNode node)
    {
        node.LeadingTrivia = null;
        if (node is SList list)
        {
            list.CloseTrivia = null;
            foreach (var child in list)
            {
                Strip(child);
            }
        }
    }
}
