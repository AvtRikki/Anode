using Anode.Geometry;

namespace Anode.Kicad.Editing;

/// <summary>Which edge of a sheet symbol a pin stands on. KiCad keeps a pin on an edge; it never floats.</summary>
public enum SheetSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// Making a child sheet: the symbol that stands for it on the parent, the file it reads, and the pins on its edge.
///
/// A sheet is two things at once — a rectangle drawn here and a schematic of its own on disk — and the two are
/// written separately: this builds the parts, and whoever owns the file decides when they land in it.
/// </summary>
public static class SchSheets
{
    /// <summary>The default text of a sheet's name, file and pins, as KiCad starts one.</summary>
    public const long TextHeightNm = 1_270_000;

    /// <summary>
    /// How far a sheet's fields stand off its border: half KiCad's default 6-mil border and the four units it adds
    /// (<c>SCH_SHEET::AutoplaceFields</c>), which is what the numbers in its files come out of.
    /// </summary>
    private const long BorderMargin = 76_204;

    /// <summary>The edge of <paramref name="sheet"/> a point is nearest, and where on that edge it lands.</summary>
    public static Vector2L OnEdge(SchSheet sheet, Vector2L point, out SheetSide side)
    {
        var corner = sheet.Position;
        var size = sheet.Size;
        long right = corner.X + size.X, bottom = corner.Y + size.Y;

        long toLeft = Math.Abs(point.X - corner.X), toRight = Math.Abs(point.X - right);
        long toTop = Math.Abs(point.Y - corner.Y), toBottom = Math.Abs(point.Y - bottom);
        long nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

        // Ties go to the vertical edges, as a sheet is usually wider than it is tall and its pins run down the sides.
        if (nearest == toLeft)
        {
            side = SheetSide.Left;
            return new Vector2L(corner.X, Between(point.Y, corner.Y, bottom));
        }

        if (nearest == toRight)
        {
            side = SheetSide.Right;
            return new Vector2L(right, Between(point.Y, corner.Y, bottom));
        }

        if (nearest == toTop)
        {
            side = SheetSide.Top;
            return new Vector2L(Between(point.X, corner.X, right), corner.Y);
        }

        side = SheetSide.Bottom;
        return new Vector2L(Between(point.X, corner.X, right), bottom);
    }

    /// <summary>
    /// The sheet a point is on, within <paramref name="margin"/> of its border so that clicking the line itself
    /// counts. The last one wins, as the later a sheet is written the higher it is drawn.
    /// </summary>
    public static SchSheet? At(IEnumerable<SchSheet> sheets, Vector2L point, long margin = 0)
    {
        SchSheet? found = null;
        foreach (var sheet in sheets)
        {
            var corner = sheet.Position;
            var size = sheet.Size;
            if (point.X >= corner.X - margin && point.X <= corner.X + size.X + margin
                && point.Y >= corner.Y - margin && point.Y <= corner.Y + size.Y + margin)
            {
                found = sheet;
            }
        }

        return found;
    }

    /// <summary>
    /// Adds a pin to a sheet, in the place KiCad writes one: after the name and file, before the instances. The
    /// node is the sheet's own, so this belongs inside a command that snapshots it.
    /// </summary>
    public static SchSheetPin AddPin(SchSheet sheet, string name, string shape, Vector2L at, SheetSide side)
    {
        var pin = SchNodes.SheetPin(name, shape, at, side);

        int index = sheet.Node.Count;
        for (int i = 0; i < sheet.Node.Count; i++)
        {
            if (sheet.Node[i] is Sexpr.SList { Head: "instances" })
            {
                index = i;
                break;
            }
        }

        sheet.Node.Insert(index, pin.Node);
        sheet.AfterRestore();
        return pin;
    }

    /// <summary>
    /// An empty schematic for a child sheet to read, in the format of the sheet it hangs under: the same version and
    /// paper, so a design does not end up written two ways at once.
    /// </summary>
    public static Schematic NewSheet(Schematic parent) =>
        Schematic.Parse(
            $"(kicad_sch (version {parent.Version}) (generator \"anode\") (uuid \"{Guid.NewGuid()}\")"
            + $" (paper {Sexpr.SEscape.Quote(parent.Paper)})\n\t(embedded_fonts no)\n)\n");

    /// <summary>Where the name of a sheet stands: above its top-left corner, as KiCad places it.</summary>
    internal static Vector2L NamePosition(Vector2L at, long textHeight) =>
        new(at.X, at.Y - BorderMargin - (long)Math.Round(textHeight * 0.5));

    /// <summary>Where the file of a sheet stands: below its bottom-left corner.</summary>
    internal static Vector2L FilePosition(Vector2L at, Vector2L size, long textHeight) =>
        new(at.X, at.Y + size.Y + BorderMargin + (long)Math.Round(textHeight * 0.4));

    private static long Between(long value, long low, long high) => Math.Clamp(value, low, high);
}
