using Anode.Geometry;
using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>A pin and the hierarchical label inside the sheet it answers to.</summary>
public sealed record SheetPinPair(SchSheetPin Pin, SchLabel Label);

/// <summary>
/// How the pins of a sheet symbol stand against the hierarchical labels inside the sheet it reads — what KiCad's
/// Sync Sheet Pins dialog shows, in its three lists, and one more.
/// </summary>
/// <param name="Matched">Pins with a label of the same name and the same shape.</param>
/// <param name="ShapeDiffers">
/// Pins with a label of the same name but another shape: an input outside that is an output inside. KiCad lists
/// both halves as unmatched; they are kept together here, since the fix is to make one agree with the other and
/// the net is already joined.
/// </param>
/// <param name="LabelsWithoutPin">Labels inside with no pin outside: signals the sheet offers that nothing takes.</param>
/// <param name="PinsWithoutLabel">Pins outside that name nothing inside: wires that end at the sheet's edge.</param>
public sealed record SheetPinMatch(
    IReadOnlyList<SheetPinPair> Matched,
    IReadOnlyList<SheetPinPair> ShapeDiffers,
    IReadOnlyList<SchLabel> LabelsWithoutPin,
    IReadOnlyList<SchSheetPin> PinsWithoutLabel)
{
    /// <summary>Everything agrees: the ERC has nothing to say about this sheet.</summary>
    public bool IsInStep => ShapeDiffers.Count == 0 && LabelsWithoutPin.Count == 0 && PinsWithoutLabel.Count == 0;
}

/// <summary>
/// Bringing a sheet symbol's pins in step with the hierarchical labels inside the sheet: KiCad's Sync Sheet Pins
/// (<c>sync_sheet_pin/</c>), Import Sheet Pins and Cleanup Sheet Pins.
///
/// Only the pins are written — they belong to the sheet on screen. The labels are in another file, and changing
/// that from here would edit a sheet nobody has open.
/// </summary>
public static class SchSheetSync
{
    /// <summary>
    /// How far apart pins are put when they are added: 100 mil, two steps of KiCad's schematic grid, which leaves a
    /// pin's name clear of the next one's at the default text size.
    /// </summary>
    public const long PinPitchNm = 2_540_000;

    /// <summary>
    /// The pins of <paramref name="sheet"/> against the labels of <paramref name="child"/>. Names are compared as
    /// shown, as the ERC compares them, so <c>A{slash}B</c> and <c>A/B</c> are one name. Labels of one name count
    /// once, as KiCad's dialog counts them, and are listed in KiCad's order of names.
    /// </summary>
    public static SheetPinMatch Compare(SchSheet sheet, Schematic child)
    {
        var labels = child.Labels
            .Where(l => l.Kind == SchLabelKind.Hierarchical)
            .GroupBy(l => l.Shown, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(l => l.Shown, KicadOrder.Instance)
            .ToList();

        var byName = labels.ToDictionary(l => l.Shown, StringComparer.Ordinal);
        var matched = new List<SheetPinPair>();
        var differs = new List<SheetPinPair>();
        var pinless = new List<SchSheetPin>();
        var answered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pin in sheet.Pins)
        {
            string name = KicadText.Unescape(pin.Name);
            if (!byName.TryGetValue(name, out var label))
            {
                pinless.Add(pin);
                continue;
            }

            answered.Add(name);
            (string.Equals(pin.Shape, label.Shape, StringComparison.Ordinal) ? matched : differs).Add(new SheetPinPair(pin, label));
        }

        return new SheetPinMatch(matched, differs, [.. labels.Where(l => !answered.Contains(l.Shown))], pinless);
    }

    /// <summary>
    /// Adds a pin for each label, on the sheet's edge, and answers the labels there was no room for.
    ///
    /// KiCad places imported pins one by one under the pointer. Here they are put down in one go where a reader
    /// looks for them: what comes out of the sheet on its right edge, everything else on its left, top to bottom at
    /// <see cref="PinPitchNm"/> in the order the labels are listed, into the first places no pin already takes. A
    /// side that is full passes the rest to the other side; a sheet too small for them all keeps them out rather than
    /// piling pins on top of each other.
    /// </summary>
    public static IReadOnlyList<SchLabel> AddPins(SchSheet sheet, IReadOnlyList<SchLabel> labels)
    {
        var left = Free(sheet, SheetSide.Left);
        var right = Free(sheet, SheetSide.Right);
        var unplaced = new List<SchLabel>();

        foreach (var label in labels)
        {
            bool outward = string.Equals(label.Shape, "output", StringComparison.Ordinal);
            var (first, second, firstSide, secondSide) = outward
                ? (right, left, SheetSide.Right, SheetSide.Left)
                : (left, right, SheetSide.Left, SheetSide.Right);

            var side = first.Count > 0 ? firstSide : secondSide;
            var slots = first.Count > 0 ? first : second;
            if (slots.Count == 0)
            {
                unplaced.Add(label);
                continue;
            }

            var at = slots.Dequeue();

            // The label's own words, escapes and all, so the pin and the label spell the name the same way.
            SchSheets.AddPin(sheet, label.Text, label.Shape, at, side);
        }

        return unplaced;
    }

    /// <summary>Takes the pins off the sheet: KiCad's Cleanup Sheet Pins, for the pins that name nothing inside.</summary>
    public static void RemovePins(SchSheet sheet, IReadOnlyList<SchSheetPin> pins)
    {
        var going = pins.Select(p => p.Node).ToHashSet();
        for (int i = sheet.Node.Count - 1; i >= 0; i--)
        {
            if (sheet.Node[i] is SList node && going.Contains(node))
            {
                sheet.Node.RemoveAt(i);
            }
        }

        sheet.AfterRestore();
    }

    /// <summary>
    /// Makes a pin answer a label: its name and its shape become the label's — KiCad's "use the label as a template".
    /// Where the pin stands and how it reads are left as they were.
    /// </summary>
    public static void Adopt(SchSheetPin pin, SchLabel label)
    {
        (pin.Node.AtomAt(1) ?? throw new KiCadFormatException("A sheet pin has no name.")).SetString(label.Text);
        (pin.Node.AtomAt(2) ?? throw new KiCadFormatException("A sheet pin has no shape.")).SetSymbol(label.Shape);
    }

    /// <summary>
    /// The places down one side of the sheet where a pin could go, top to bottom, less those a pin already takes.
    /// The first is one pitch below the corner, as a pin in the corner would have its name run into the border.
    /// </summary>
    private static Queue<Vector2L> Free(SchSheet sheet, SheetSide side)
    {
        long x = side == SheetSide.Left ? sheet.Position.X : sheet.Position.X + sheet.Size.X;
        long top = sheet.Position.Y;
        long bottom = sheet.Position.Y + sheet.Size.Y;

        var taken = sheet.Pins.Where(p => p.Position.X == x).Select(p => p.Position.Y).ToList();
        var free = new Queue<Vector2L>();
        for (long y = top + PinPitchNm; y <= bottom - PinPitchNm; y += PinPitchNm)
        {
            if (taken.All(t => Math.Abs(t - y) >= PinPitchNm))
            {
                free.Enqueue(new Vector2L(x, y));
            }
        }

        return free;
    }
}
