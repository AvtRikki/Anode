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

    /// <summary>
    /// The same words written as another kind of thing: a local label made global, a hierarchical one made into
    /// plain text. What is on the sheet stays where it is and looks as it did — the point, the angle and the font
    /// travel with it — because only what the words <em>mean</em> is being changed.
    ///
    /// A label that leaves the sheet carries the direction of its signal, and one that does not has nowhere to keep
    /// it, so a shape is taken from the old item when it had one and is otherwise the plainest that will do.
    /// </summary>
    /// <param name="to">The kind to write it as; null writes it as free text, which is no kind of label at all.</param>
    public static SchItem Relabel(SchItem item, SchLabelKind? to)
    {
        string words = item switch
        {
            SchLabel label => label.Text,
            SchText text => text.Text,
            _ => throw new NotSupportedException($"{item.GetType().Name} is not something with words on it."),
        };

        if (words.Length == 0)
        {
            throw new InvalidOperationException("There is nothing written on it to carry over.");
        }

        SchItem fresh = to is { } kind
            ? Label(kind, KicadText.Unescape(words), item.Position, item.Angle, Shape(item))
            : Text(KicadText.Unescape(words), item.Position, item.Angle);

        // The look is the designer's, not the kind's: whatever font, size and justification it had, it keeps.
        if (item.Node.Find("effects") is { } effects && fresh.Node.Find("effects") is { } theirs)
        {
            int at = fresh.Node.IndexOf(theirs);
            fresh.Node.RemoveAt(at);
            fresh.Node.Insert(at, Adopt(effects));
        }

        return fresh;
    }

    /// <summary>The direction an item says its signal goes, for one that says so at all.</summary>
    private static string Shape(SchItem item) =>
        item.Node.ChildString("shape") is { Length: > 0 } shape ? shape : "input";

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
    /// A child sheet: the rectangle that stands for it here, with the name it is called by and the file it reads.
    /// Its fields are placed as KiCad autoplaces them — the name above the top-left corner, the file below the
    /// bottom-left one — and it says so, which is what lets KiCad place them again when the sheet is resized.
    ///
    /// <paramref name="places"/> is where the sheet stands in the design and which page it is there: it appears once
    /// per path of the parent, and the page numbers are read from this. A sheet written without any is still read,
    /// but has no page of its own until the design is numbered.
    /// </summary>
    public static SchSheet Sheet(
        string name,
        string file,
        Vector2L at,
        Vector2L size,
        IReadOnlyList<(string Project, string Path, string Page)>? places = null)
    {
        if (name.Length == 0 || file.Length == 0)
        {
            throw new ArgumentException("A sheet needs a name and a file.");
        }

        if (size.X <= 0 || size.Y <= 0)
        {
            throw new ArgumentException("A sheet needs a size.", nameof(size));
        }

        const long height = SchSheets.TextHeightNm;
        var text = new StringBuilder("(sheet");
        text.Append($" (at {Mm(at.X)} {Mm(at.Y)}) (size {Mm(size.X)} {Mm(size.Y)})")
            .Append(" (exclude_from_sim no) (in_bom yes) (on_board yes) (dnp no) (fields_autoplaced yes)")
            .Append(" (stroke (width 0) (type solid)) (fill (color 0 0 0 0.0000))")
            .Append($" (uuid \"{Guid.NewGuid()}\")")
            .Append(Field("Sheetname", name, SchSheets.NamePosition(at, height), "bottom", height))
            .Append(Field("Sheetfile", file, SchSheets.FilePosition(at, size, height), "top", height));

        if (places is { Count: > 0 })
        {
            text.Append(" (instances");
            foreach (var project in places.GroupBy(p => p.Project, StringComparer.Ordinal))
            {
                text.Append($" (project {Quote(project.Key)}");
                foreach (var (_, path, page) in project)
                {
                    text.Append($" (path {Quote(path)} (page {Quote(page)}))");
                }

                text.Append(')');
            }

            text.Append(')');
        }

        return new SchSheet(Fresh(text.Append(')').ToString()));
    }

    /// <summary>
    /// A pin of a sheet symbol: the name it answers inside, the direction of the signal, and the point on the edge
    /// where a wire meets it. The edge has no word of its own in the file — it is the angle, and the name reads away
    /// from the sheet, which is what the justification says.
    /// </summary>
    public static SchSheetPin SheetPin(string name, string shape, Vector2L at, SheetSide side)
    {
        if (name.Length == 0)
        {
            throw new ArgumentException("A sheet pin needs a name.", nameof(name));
        }

        // Left 180, right 0, top 90, bottom 270, as KiCad's getSheetPinAngle writes them.
        (int angle, string justify) = side switch
        {
            SheetSide.Right => (0, "right"),
            SheetSide.Top => (90, "right"),
            SheetSide.Bottom => (270, "left"),
            _ => (180, "left"),
        };

        const long height = SchSheets.TextHeightNm;
        return new SchSheetPin(Fresh(
            $"(pin {Quote(name)} {shape} (at {Mm(at.X)} {Mm(at.Y)} {KiCadNumber.FormatAngle(angle)})"
            + $" (uuid \"{Guid.NewGuid()}\")"
            + $" (effects (font (size {Mm(height)} {Mm(height)})) (justify {justify})))"));
    }

    /// <summary>A sheet's own field: a property like any other, placed and justified by which one it is.</summary>
    private static string Field(string name, string value, Vector2L at, string vertical, long height) =>
        $" (property {Quote(name)} {Quote(value)} (at {Mm(at.X)} {Mm(at.Y)} 0)"
        + $" (effects (font (size {Mm(height)} {Mm(height)})) (justify left {vertical})))";

    private static string Mm(long nm) => KiCadNumber.FormatMm(nm);

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
