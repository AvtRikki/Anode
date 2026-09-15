namespace Ecad.Rendering;

/// <summary>Default palette (close to KiCad's classic dark theme) and draw order.</summary>
public static class LayerStyle
{
    /// <summary>Pseudo-layer for plated holes of pads and vias.</summary>
    public const string PlatedHoles = "#PlatedHoles";

    /// <summary>Pseudo-layer for non-plated holes.</summary>
    public const string NonPlatedHoles = "#NonPlatedHoles";

    public static readonly ColorRgba Background = ColorRgba.Rgb(0, 16, 35);
    public static readonly ColorRgba Grid = new(132, 132, 132, 90);
    public static readonly ColorRgba Selection = ColorRgba.Rgb(255, 255, 255);

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
    };

    private static readonly string[] BackOrder = ["B.Adhes", "B.Paste", "B.SilkS", "B.Mask", "B.Fab", "B.CrtYd"];
    private static readonly string[] FrontOrder = ["F.Mask", "F.Paste", "F.Adhes", "F.SilkS", "F.Fab", "F.CrtYd"];

    public static ColorRgba ColorFor(string layerName)
    {
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
