namespace Anode.Render;

/// <summary>Which set of layer colours the scene is drawn with.</summary>
public enum InkSet
{
    /// <summary>Print inks on a light sheet.</summary>
    Print,

    /// <summary>KiCad's classic dark palette.</summary>
    Classic,
}

/// <summary>Default palette (close to KiCad's classic dark theme) and draw order.</summary>
public static class LayerStyle
{
    /// <summary>Pseudo-layer for plated holes of pads and vias.</summary>
    public const string PlatedHoles = "#PlatedHoles";

    /// <summary>Pseudo-layer for non-plated holes.</summary>
    public const string NonPlatedHoles = "#NonPlatedHoles";

    /// <summary>Pseudo-layer for the board body: the sheet inside the board outline, drawn under everything.</summary>
    public const string BoardBody = "#BoardBody";

    /// <summary>Schematic layers. A schematic is one drawing, but the parts of it are coloured and ordered apart.</summary>
    public static class Sch
    {
        public const string Sheet = "#SchSheet";

        /// <summary>The frame around the drawing and its title block — KiCad's drawing sheet.</summary>
        public const string Frame = "#SchFrame";
        public const string Wire = "#SchWire";
        public const string Bus = "#SchBus";
        public const string Symbol = "#SchSymbol";
        public const string SymbolFill = "#SchSymbolFill";
        public const string Pin = "#SchPin";
        public const string PinText = "#SchPinText";
        public const string Field = "#SchField";
        public const string Label = "#SchLabel";
        public const string Text = "#SchText";
        public const string Junction = "#SchJunction";
        public const string NoConnect = "#SchNoConnect";
    }

    /// <summary>
    /// Print inks on a light sheet (the Anode kit): the drawing reads as an impression, not as neon on black.
    /// <see cref="InkSet.Classic"/> keeps KiCad's dark palette for comparison.
    /// </summary>
    public static InkSet Inks { get; set; } = InkSet.Print;

    /// <summary>Behind the sheet: the desk the board lies on. The canvas is light in both interface themes.</summary>
    public static ColorRgba Background => Inks == InkSet.Print ? ColorRgba.Rgb(0xe9, 0xe6, 0xe5) : ColorRgba.Rgb(0, 16, 35);

    public static ColorRgba Grid => Inks == InkSet.Print ? new ColorRgba(0x20, 0x1e, 0x1d, 52) : new ColorRgba(132, 132, 132, 90);

    public static ColorRgba Selection => Inks == InkSet.Print ? ColorRgba.Rgb(0x00, 0x88, 0xb0) : ColorRgba.Rgb(255, 255, 255);

    /// <summary>Six inks (kit 1.1): two coppers, mask, silk, the board body and a grey for inner layers.</summary>
    private static readonly Dictionary<string, ColorRgba> PrintNamed = new(StringComparer.Ordinal)
    {
        // Copper is ink on paper, not paint: a pour must not bury the silkscreen and the outline under it.
        ["F.Cu"] = new(0xd6, 0x00, 0x6c, 215),
        ["B.Cu"] = new(0x00, 0x88, 0xb0, 195),
        ["F.Mask"] = new(0xed, 0xbb, 0x00, 150),
        ["B.Mask"] = new(0xed, 0xbb, 0x00, 110),
        ["F.Paste"] = new(0x9b, 0x97, 0x97, 150),
        ["B.Paste"] = new(0x9b, 0x97, 0x97, 110),
        ["F.SilkS"] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        ["B.SilkS"] = new(0x20, 0x1e, 0x1d, 150),
        ["Edge.Cuts"] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        ["Margin"] = new(0xd6, 0x00, 0x6c, 120),
        ["F.CrtYd"] = new(0x20, 0x1e, 0x1d, 90),
        ["B.CrtYd"] = new(0x20, 0x1e, 0x1d, 70),
        ["F.Fab"] = new(0x20, 0x1e, 0x1d, 110),
        ["B.Fab"] = new(0x20, 0x1e, 0x1d, 90),
        ["Dwgs.User"] = new(0x20, 0x1e, 0x1d, 130),
        ["Cmts.User"] = new(0x00, 0x88, 0xb0, 130),
        [PlatedHoles] = ColorRgba.Rgb(0xf8, 0xf4, 0xf4),
        [NonPlatedHoles] = ColorRgba.Rgb(0xf8, 0xf4, 0xf4),
        [BoardBody] = ColorRgba.Rgb(0xf8, 0xf4, 0xf4),

        // A schematic is an impression too: dark strokes on the sheet, cyan for what carries a name.
        [Sch.Sheet] = ColorRgba.Rgb(0xf8, 0xf4, 0xf4),
        [Sch.Frame] = new(0x20, 0x1e, 0x1d, 170),
        [Sch.Wire] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        [Sch.Bus] = ColorRgba.Rgb(0x00, 0x88, 0xb0),
        [Sch.Symbol] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        [Sch.SymbolFill] = new(0xed, 0xbb, 0x00, 60),
        [Sch.Pin] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        [Sch.PinText] = new(0x20, 0x1e, 0x1d, 150),
        [Sch.Field] = new(0x20, 0x1e, 0x1d, 190),
        [Sch.Label] = ColorRgba.Rgb(0x00, 0x88, 0xb0),
        [Sch.Text] = new(0x20, 0x1e, 0x1d, 200),
        [Sch.Junction] = ColorRgba.Rgb(0x20, 0x1e, 0x1d),
        [Sch.NoConnect] = ColorRgba.Rgb(0xd6, 0x00, 0x6c),
    };

    /// <summary>Inner copper is one grey; the drawing stays readable when a board has more than four layers.</summary>
    private static readonly ColorRgba PrintInner = ColorRgba.Rgb(0xda, 0xd6, 0xd6);

    private static readonly ColorRgba[] InnerCopper =
    [
        ColorRgba.Rgb(127, 200, 127), ColorRgba.Rgb(206, 125, 44), ColorRgba.Rgb(79, 203, 203), ColorRgba.Rgb(219, 98, 139),
        ColorRgba.Rgb(167, 165, 198), ColorRgba.Rgb(40, 204, 217), ColorRgba.Rgb(232, 178, 167), ColorRgba.Rgb(242, 237, 161),
        ColorRgba.Rgb(141, 203, 129), ColorRgba.Rgb(237, 124, 51), ColorRgba.Rgb(91, 195, 235), ColorRgba.Rgb(247, 111, 142),
        ColorRgba.Rgb(167, 165, 198), ColorRgba.Rgb(40, 204, 217), ColorRgba.Rgb(232, 178, 167), ColorRgba.Rgb(242, 237, 161),
    ];

    private static readonly Dictionary<string, ColorRgba> Named = new(StringComparer.Ordinal)
    {
        ["F.Cu"] = ColorRgba.Rgb(200, 52, 52),
        ["B.Cu"] = ColorRgba.Rgb(77, 127, 196),
        ["F.Adhes"] = ColorRgba.Rgb(132, 0, 132),
        ["B.Adhes"] = ColorRgba.Rgb(0, 0, 132),
        ["F.Paste"] = new(180, 160, 154, 150),
        ["B.Paste"] = new(0, 194, 194, 150),
        ["F.SilkS"] = ColorRgba.Rgb(242, 237, 161),
        ["B.SilkS"] = ColorRgba.Rgb(232, 178, 167),
        ["F.Mask"] = new(216, 100, 255, 100),
        ["B.Mask"] = new(2, 255, 238, 100),
        ["Dwgs.User"] = ColorRgba.Rgb(194, 194, 194),
        ["Cmts.User"] = ColorRgba.Rgb(89, 148, 220),
        ["Eco1.User"] = ColorRgba.Rgb(180, 219, 210),
        ["Eco2.User"] = ColorRgba.Rgb(216, 200, 82),
        ["Edge.Cuts"] = ColorRgba.Rgb(208, 210, 205),
        ["Margin"] = ColorRgba.Rgb(255, 38, 226),
        ["F.CrtYd"] = ColorRgba.Rgb(255, 38, 226),
        ["B.CrtYd"] = ColorRgba.Rgb(38, 233, 255),
        ["F.Fab"] = ColorRgba.Rgb(175, 175, 175),
        ["B.Fab"] = ColorRgba.Rgb(88, 93, 132),
        [PlatedHoles] = ColorRgba.Rgb(227, 183, 46),
        [NonPlatedHoles] = ColorRgba.Rgb(26, 196, 210),

        // KiCad's own drawing-sheet colour.
        [Sch.Frame] = ColorRgba.Rgb(132, 0, 0),
    };

    private static readonly string[] BackOrder = ["B.Adhes", "B.Paste", "B.SilkS", "B.Mask", "B.Fab", "B.CrtYd"];
    private static readonly string[] FrontOrder = ["F.Mask", "F.Paste", "F.Adhes", "F.SilkS", "F.Fab", "F.CrtYd"];

    public static ColorRgba ColorFor(string layerName)
    {
        if (Inks == InkSet.Print)
        {
            return PrintNamed.TryGetValue(layerName, out var ink) ? ink
                : TryInnerIndex(layerName, out _) ? PrintInner
                : new ColorRgba(0x20, 0x1e, 0x1d, 110);
        }

        if (Named.TryGetValue(layerName, out var color))
        {
            return color;
        }

        if (TryInnerIndex(layerName, out int inner))
        {
            return InnerCopper[(inner - 1) % InnerCopper.Length];
        }

        // User.1 ... User.N and anything unknown get a stable pastel from the name hash.
        uint h = (uint)StringComparer.Ordinal.GetHashCode(layerName);
        return ColorRgba.Rgb((byte)(120 + h % 120), (byte)(120 + (h >> 8) % 120), (byte)(120 + (h >> 16) % 120));
    }

    /// <summary>Sort key: back side, copper bottom-up, front side, documentation, holes last.</summary>
    public static int DrawOrder(string layerName)
    {
        int back = Array.IndexOf(BackOrder, layerName);
        if (back >= 0)
        {
            return back;
        }

        if (layerName == "B.Cu")
        {
            return 100;
        }

        if (TryInnerIndex(layerName, out int inner))
        {
            return 1_000 - inner;
        }

        if (layerName == "F.Cu")
        {
            return 1_000;
        }

        int front = Array.IndexOf(FrontOrder, layerName);
        if (front >= 0)
        {
            return 2_000 + front;
        }

        return layerName switch
        {
            BoardBody or Sch.Sheet => -1_000,
            Sch.SymbolFill => -500,
            Sch.Frame => 5,
            Sch.Wire => 10,
            Sch.Bus => 11,
            Sch.Symbol => 20,
            Sch.Pin => 21,
            Sch.PinText => 22,
            Sch.Field => 30,
            Sch.Text => 31,
            Sch.Label => 32,
            Sch.Junction => 40,
            Sch.NoConnect => 41,
            PlatedHoles => 9_000,
            NonPlatedHoles => 9_001,
            "Edge.Cuts" => 8_000,
            "Margin" => 7_999,
            _ => 3_000,
        };
    }

    public static bool IsCopper(string layerName) => layerName.EndsWith(".Cu", StringComparison.Ordinal);

    private static bool TryInnerIndex(string name, out int index)
    {
        index = 0;
        return name.StartsWith("In", StringComparison.Ordinal) && name.EndsWith(".Cu", StringComparison.Ordinal)
               && int.TryParse(name.AsSpan(2, name.Length - 5), out index) && index > 0;
    }
}
