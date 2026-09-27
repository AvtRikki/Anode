using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writing what a library symbol's pins, shapes and fields say — what the inspector does when a value is committed.
/// Every write goes through the atoms already there, or adds a word where KiCad 9 writes it, so an edit undone gives
/// the file back byte for byte.
/// </summary>
public static class SymbolWrites
{
    /// <summary>KiCad's electrical types for a pin, in the order its pin dialog offers them.</summary>
    public static readonly IReadOnlyList<string> PinTypes =
    [
        "input", "output", "bidirectional", "tri_state", "passive", "free", "unspecified",
        "power_in", "power_out", "open_collector", "open_emitter", "no_connect",
    ];

    /// <summary>KiCad's graphic styles for a pin, in the order its pin dialog offers them.</summary>
    public static readonly IReadOnlyList<string> PinShapes =
    [
        "line", "inverted", "clock", "inverted_clock", "input_low", "clock_low", "output_low", "edge_clock_high", "non_logic",
    ];

    /// <summary>How a shape may be filled, in the file's words.</summary>
    public static readonly IReadOnlyList<string> Fills = ["none", "outline", "background"];

    public static void SetPinType(SchPin pin, string type) => Atom(pin.Node, 1).SetSymbol(Checked(type, PinTypes));

    public static void SetPinShape(SchPin pin, string shape) => Atom(pin.Node, 2).SetSymbol(Checked(shape, PinShapes));

    public static void SetPinName(SchPin pin, string name) =>
        Atom(pin.Node.Find("name") ?? throw new KiCadFormatException("A pin has no name."), 1).SetString(name.Length == 0 ? "~" : name);

    public static void SetPinNumber(SchPin pin, string number) =>
        Atom(pin.Node.Find("number") ?? throw new KiCadFormatException("A pin has no number."), 1).SetString(number);

    public static void SetPinLength(SchPin pin, long nm) =>
        (pin.Node.Find("length") ?? throw new KiCadFormatException("A pin has no length.")).SetNm(1, Math.Max(0, nm));

    /// <summary>Which way a pin points from where a wire meets it: 0 right, 90 up, 180 left, 270 down.</summary>
    public static void SetPinAngle(SchPin pin, double degrees) =>
        (pin.Node.Find("at") ?? throw new KiCadFormatException("A pin has no place.")).SetAngle(3, KiCadNumber.Normalize360(degrees), omitWhenZero: false);

    /// <summary>
    /// Hides a pin or shows it. KiCad 9 writes a hidden pin as <c>(hide yes)</c> after its length and says nothing
    /// for a shown one; a pin written with <c>(hide no)</c> by another hand keeps its word and has it flipped.
    /// </summary>
    public static void SetPinHidden(SchPin pin, bool hidden) => SetWord(pin.Node, "hide", hidden, after: "length");

    /// <summary>
    /// Hides a field or shows it. A library writes the word inside the field's effects; one that carries it on the
    /// field itself, as newer sheets do, has it changed there.
    /// </summary>
    public static void SetFieldHidden(SchField field, bool hidden)
    {
        if (field.Node.Find("hide") is not null || field.Node.Find("effects") is not { } effects)
        {
            SetWord(field.Node, "hide", hidden, after: "at");
            return;
        }

        SetWord(effects, "hide", hidden, after: null);
    }

    public static void SetFieldValue(SchField field, string value) => Atom(field.Node, 2).SetString(value);

    public static void SetStrokeWidth(SchGraphic shape, long nm) =>
        (shape.Node.Find("stroke")?.Find("width") ?? throw new KiCadFormatException("A shape has no stroke width.")).SetNm(1, Math.Max(0, nm));

    public static void SetFill(SchGraphic shape, string fill)
    {
        string type = Checked(fill, Fills);
        if (shape.Node.Find("fill")?.Find("type") is { } written)
        {
            Atom(written, 1).SetSymbol(type);
            return;
        }

        var made = SchNodes.Adopt(SDocument.Parse($"(fill (type {type}))").Root);
        int at = shape.Node.Find("stroke") is { } stroke ? shape.Node.IndexOf(stroke) + 1 : shape.Node.Count;
        shape.Node.Insert(at, made);
    }

    /// <summary>
    /// Shows a symbol's pin numbers or names, or hides them: KiCad writes <c>(pin_numbers (hide yes))</c> and
    /// <c>(pin_names (offset …) (hide yes))</c>, and leaves the word out for what is shown.
    /// </summary>
    /// <param name="list"><c>pin_numbers</c> or <c>pin_names</c>.</param>
    public static void SetPinTextShown(LibSymbol symbol, string list, bool shown)
    {
        if (list is not ("pin_numbers" or "pin_names"))
        {
            throw new ArgumentException("Only pin numbers and pin names are shown or hidden.", nameof(list));
        }

        var node = symbol.Node.Find(list);
        if (node is null)
        {
            if (shown)
            {
                return;
            }

            node = SchNodes.Adopt(SDocument.Parse($"({list})").Root);
            symbol.Node.Insert(PlaceFor(symbol.Node, list), node);
        }

        SetWord(node, "hide", !shown, after: null);

        // A pin_numbers that says nothing any more is left out, as KiCad leaves it out.
        if (shown && list == "pin_numbers" && node.Count == 1)
        {
            symbol.Node.RemoveAt(symbol.Node.IndexOf(node));
        }
    }

    /// <summary>
    /// Where KiCad 9 writes a symbol's <c>pin_numbers</c> and <c>pin_names</c>: in that order, before the flags and
    /// the fields.
    /// </summary>
    private static int PlaceFor(SList symbol, string list)
    {
        string[] after = list == "pin_numbers"
            ? ["pin_names", "exclude_from_sim", "in_bom", "on_board", "property"]
            : ["exclude_from_sim", "in_bom", "on_board", "property"];

        if (list == "pin_names" && symbol.Find("pin_numbers") is { } numbers)
        {
            return symbol.IndexOf(numbers) + 1;
        }

        return symbol.Lists().FirstOrDefault(l => after.Contains(l.Head)) is { } before ? symbol.IndexOf(before) : symbol.Count;
    }

    /// <summary>
    /// Sets a yes-or-no word inside a list: changes one that is there, adds <c>(word yes)</c> where it belongs when
    /// it should be on, and takes it out when it should be off and KiCad would not write it.
    /// </summary>
    private static void SetWord(SList node, string word, bool on, string? after)
    {
        if (node.Find(word) is { } written)
        {
            if (!on && !IsExplicitNo(written))
            {
                node.RemoveAt(node.IndexOf(written));
            }
            else
            {
                Atom(written, 1).SetSymbol(on ? "yes" : "no");
            }

            return;
        }

        if (!on)
        {
            return;
        }

        var made = SchNodes.Adopt(SDocument.Parse($"({word} yes)").Root);
        int at = after is not null && node.Find(after) is { } anchor ? node.IndexOf(anchor) + 1 : node.Count;
        node.Insert(at, made);
    }

    /// <summary>A word written <c>(hide no)</c> is kept and flipped rather than removed, so its writer's style survives.</summary>
    private static bool IsExplicitNo(SList written) => written.Str(1) is "no";

    private static SAtom Atom(SList node, int index) =>
        node.AtomAt(index) ?? throw new KiCadFormatException($"({node.Head} ...) has nothing at position {index}.");

    private static string Checked(string value, IReadOnlyList<string> allowed) =>
        allowed.Contains(value, StringComparer.Ordinal) ? value : throw new ArgumentException($"\"{value}\" is not one of KiCad's words here.");
}
