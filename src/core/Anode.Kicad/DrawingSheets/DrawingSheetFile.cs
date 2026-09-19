using Anode.Sexpr;

namespace Anode.Kicad.DrawingSheets;

/// <summary>Which corner of the area inside the margins a drawing-sheet position is measured from.</summary>
public enum WksCorner
{
    RightBottom,
    RightTop,
    LeftBottom,
    LeftTop,
}

/// <summary>Which pages an item is drawn on: every one, only the first, or all but the first.</summary>
public enum WksPages
{
    All,
    FirstOnly,
    NotOnFirst,
}

/// <summary>A position in millimetres from a corner, as a drawing sheet writes it: <c>(start 110 34 rbcorner)</c>.</summary>
public readonly record struct WksPoint(double X, double Y, WksCorner Corner = WksCorner.RightBottom);

/// <summary>The drawing sheet's defaults: text size and pens, and the margins that define its corners, in millimetres.</summary>
public sealed record WksSetup(
    double TextWidth = 1.5,
    double TextHeight = 1.5,
    double LineWidth = 0.15,
    double TextLineWidth = 0.15,
    double LeftMargin = 10,
    double RightMargin = 10,
    double TopMargin = 10,
    double BottomMargin = 10);

/// <summary>
/// One item of a drawing sheet. Every kind can repeat: copy <c>j</c> is moved by <c>j</c> times the increment, and
/// copies after the first are dropped where they would leave the area inside the margins.
/// </summary>
public abstract class WksItem
{
    public string Name { get; init; } = string.Empty;

    public WksPages Pages { get; init; }

    public WksPoint Start { get; init; }

    public int Repeat { get; init; } = 1;

    public double IncrementX { get; init; }

    public double IncrementY { get; init; }

    /// <summary>Pen in millimetres; zero means the sheet's default.</summary>
    public double LineWidth { get; init; }
}

/// <summary>A straight line, or with <see cref="IsRectangle"/> the rectangle whose opposite corners are the two points.</summary>
public sealed class WksLine : WksItem
{
    public WksPoint End { get; init; }

    public bool IsRectangle { get; init; }
}

/// <summary>A piece of text, which may name title-block fields and variables such as <c>${TITLE}</c>.</summary>
public sealed class WksText : WksItem
{
    public string Text { get; init; } = string.Empty;

    /// <summary>Glyph size in millimetres; zero means the sheet's default.</summary>
    public double Width { get; init; }

    public double Height { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>"left", "center" or "right".</summary>
    public string HorizontalAlign { get; init; } = "left";

    /// <summary>"top", "center" or "bottom".</summary>
    public string VerticalAlign { get; init; } = "center";

    public double Rotation { get; init; }

    /// <summary>How far the last character of a repeated label moves per copy: "1", "2", "3"… or "A", "B", "C"….</summary>
    public int IncrementLabel { get; init; } = 1;

    /// <summary>Longest the text may be, in millimetres; wider text is squeezed to fit. Zero means no limit.</summary>
    public double MaxLength { get; init; }

    public double MaxHeight { get; init; }

    /// <summary>An installed typeface to draw the text in, as <c>(font (face "Arial"))</c>; null for KiCad's stroke font.</summary>
    public string? Face { get; init; }

    /// <summary>The text's own colour, <c>(font (color r g b a))</c>, alpha from 0 to 1; null for the sheet's.</summary>
    public (byte R, byte G, byte B, double A)? Color { get; init; }
}

/// <summary>Filled outlines, placed at <see cref="WksItem.Start"/> and turned by <see cref="Rotation"/>: a logo, usually.</summary>
public sealed class WksPolygon : WksItem
{
    public double Rotation { get; init; }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines { get; init; } = [];
}

/// <summary>
/// An embedded picture, centred on <see cref="WksItem.Start"/>. KiCad keeps it as an image file; a PNG is drawn,
/// its size on the page following from its pixels and its resolution — pixels × 25.4 × scale / PPI millimetres.
/// </summary>
public sealed class WksBitmap : WksItem
{
    public double Scale { get; init; } = 1;

    /// <summary>The image file as stored; null when the item carried none that could be read.</summary>
    public byte[]? Image { get; init; }

    /// <summary>Width, height and resolution read from the image; null for anything but a PNG.</summary>
    public PngInfo? Png => Image is { } bytes ? PngInfo.Read(bytes) : null;

    /// <summary>Size on the page in millimetres; null when the image is not a PNG.</summary>
    public (double Width, double Height)? SizeMm => Png is { } png
        ? (png.Width * 25.4 * Scale / png.Ppi, png.Height * 25.4 * Scale / png.Ppi)
        : null;
}

/// <summary>What a PNG says about itself in its header: pixels, and pixels per inch (KiCad's 300 when it does not say).</summary>
public sealed record PngInfo(int Width, int Height, int Ppi)
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngInfo? Read(byte[] data)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return null;
        }

        int width = BigEndian(data, 16), height = BigEndian(data, 20), ppi = 300;
        for (int at = 8; at + 12 <= data.Length;)
        {
            int length = BigEndian(data, at);
            string type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);

            // Pixels per metre, unit 1; KiCad reads it through wx as pixels per centimetre and rounds the inch figure.
            if (type == "pHYs" && length >= 9 && at + 17 <= data.Length && data[at + 16] == 1)
            {
                int perMetre = BigEndian(data, at + 8);
                if (perMetre > 0)
                {
                    ppi = (int)Math.Round(perMetre / 100.0 * 2.54, MidpointRounding.AwayFromZero);
                }
            }

            if (type is "IDAT" or "IEND" || length < 0)
            {
                break;
            }

            at += 12 + length;
        }

        return width > 0 && height > 0 ? new PngInfo(width, height, Math.Max(1, ppi)) : null;
    }

    private static int BigEndian(byte[] data, int at) =>
        (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
}

/// <summary>
/// A KiCad drawing sheet (<c>.kicad_wks</c>): the frame and title block drawn around a schematic page or a board. The
/// default one is a drawing sheet like any other, written out in <see cref="DefaultText"/>, so a custom sheet and
/// the default go through the same reading and the same drawing.
/// </summary>
public sealed class DrawingSheetFile
{
    public WksSetup Setup { get; init; } = new();

    public IReadOnlyList<WksItem> Items { get; init; } = [];

    /// <summary>The file it was read from; null for the default.</summary>
    public string? Path { get; init; }

    public static DrawingSheetFile Default { get; } = Parse(DefaultText);

    public static DrawingSheetFile Load(string path) => Parse(File.ReadAllText(path)) is var sheet
        ? new DrawingSheetFile { Setup = sheet.Setup, Items = sheet.Items, Path = path }
        : throw new InvalidOperationException();

    public static DrawingSheetFile Parse(string text)
    {
        var root = SDocument.Parse(text).Root;
        if (root.Head is not ("kicad_wks" or "drawing_sheet" or "page_layout"))
        {
            throw new KiCadFormatException($"({root.Head} …) is not a drawing sheet.");
        }

        var setup = new WksSetup();
        var items = new List<WksItem>();
        foreach (var child in root.Lists())
        {
            switch (child.Head)
            {
                case "setup":
                    setup = ReadSetup(child);
                    break;
                case "line" or "rect":
                    items.Add(new WksLine
                    {
                        Name = child.ChildString("name") ?? string.Empty,
                        Pages = Pages(child),
                        Start = Point(child.Find("start")),
                        End = Point(child.Find("end")),
                        IsRectangle = child.Head == "rect",
                        Repeat = Repeat(child),
                        IncrementX = child.ChildDouble("incrx") ?? 0,
                        IncrementY = child.ChildDouble("incry") ?? 0,
                        LineWidth = child.ChildDouble("linewidth") ?? 0,
                    });
                    break;
                case "tbtext":
                    items.Add(ReadText(child));
                    break;
                case "polygon":
                    items.Add(new WksPolygon
                    {
                        Name = child.ChildString("name") ?? string.Empty,
                        Pages = Pages(child),
                        Start = Point(child.Find("pos")),
                        Rotation = child.ChildDouble("rotate") ?? 0,
                        Repeat = Repeat(child),
                        IncrementX = child.ChildDouble("incrx") ?? 0,
                        IncrementY = child.ChildDouble("incry") ?? 0,
                        LineWidth = child.ChildDouble("linewidth") ?? 0,
                        Outlines =
                        [
                            .. child.Lists().Where(l => l.Head == "pts").Select(pts => (IReadOnlyList<(double, double)>)
                            [
                                .. pts.Lists().Where(xy => xy.Head == "xy")
                                    .Select(xy => (Number(xy, 1), Number(xy, 2))),
                            ]),
                        ],
                    });
                    break;
                case "bitmap":
                    items.Add(new WksBitmap
                    {
                        Name = child.ChildString("name") ?? string.Empty,
                        Pages = Pages(child),
                        Start = Point(child.Find("pos")),
                        Scale = child.ChildDouble("scale") ?? 1,
                        Image = ImageOf(child),
                        Repeat = Repeat(child),
                        IncrementX = child.ChildDouble("incrx") ?? 0,
                        IncrementY = child.ChildDouble("incry") ?? 0,
                    });
                    break;
            }
        }

        return new DrawingSheetFile { Setup = setup, Items = items };
    }

    private static WksSetup ReadSetup(SList setup)
    {
        var size = setup.Find("textsize");
        return new WksSetup(
            size is null ? 1.5 : Number(size, 1),
            size is null ? 1.5 : Number(size, 2),
            setup.ChildDouble("linewidth") ?? 0.15,
            setup.ChildDouble("textlinewidth") ?? 0.15,
            setup.ChildDouble("left_margin") ?? 10,
            setup.ChildDouble("right_margin") ?? 10,
            setup.ChildDouble("top_margin") ?? 10,
            setup.ChildDouble("bottom_margin") ?? 10);
    }

    private static WksText ReadText(SList node)
    {
        double width = 0, height = 0, pen = node.ChildDouble("linewidth") ?? 0;
        bool bold = false, italic = false;
        string? face = null;
        (byte, byte, byte, double)? color = null;
        if (node.Find("font") is { } font)
        {
            face = font.ChildString("face") is { Length: > 0 } named ? named : null;
            if (font.Find("color") is { } rgba)
            {
                color = (Byte(rgba, 1), Byte(rgba, 2), Byte(rgba, 3), Math.Clamp(rgba.AtomAt(4)?.TryGetDouble(out double a) == true ? a : 1, 0, 1));
            }

            bold = Atoms(font).Any(a => a.IsSymbol("bold"));
            italic = Atoms(font).Any(a => a.IsSymbol("italic"));
            if (font.Find("size") is { } size)
            {
                width = Number(size, 1);
                height = Number(size, 2);
            }

            pen = font.ChildDouble("linewidth") ?? pen;
        }

        // "center" centres both ways; the others set one axis each.
        string horizontal = "left", vertical = "center";
        foreach (var word in Atoms(node.Find("justify")).Select(a => a.Value))
        {
            switch (word)
            {
                case "center":
                    (horizontal, vertical) = ("center", "center");
                    break;
                case "left" or "right":
                    horizontal = word;
                    break;
                case "top" or "bottom":
                    vertical = word;
                    break;
            }
        }

        return new WksText
        {
            Text = ConvertLegacyCodes(node.Str(1) ?? string.Empty),
            Name = node.ChildString("name") ?? string.Empty,
            Pages = Pages(node),
            Start = Point(node.Find("pos")),
            Repeat = Repeat(node),
            IncrementX = node.ChildDouble("incrx") ?? 0,
            IncrementY = node.ChildDouble("incry") ?? 0,
            IncrementLabel = (int)(node.ChildDouble("incrlabel") ?? 1),
            Width = width,
            Height = height,
            Bold = bold,
            Italic = italic,
            HorizontalAlign = horizontal,
            VerticalAlign = vertical,
            Rotation = node.ChildDouble("rotate") ?? 0,
            MaxLength = node.ChildDouble("maxlen") ?? 0,
            MaxHeight = node.ChildDouble("maxheight") ?? 0,
            LineWidth = pen,
            Face = face,
            Color = color,
        };
    }

    private static byte Byte(SList node, int index) => (byte)Math.Clamp((int)Number(node, index), 0, 255);

    /// <summary>
    /// The percent codes of older drawing sheets, as the variables they became — KiCad converts every text it reads
    /// this way, whatever the file's version. The comments shift by one: %C0 is the first comment. An unknown code
    /// is dropped along with its percent sign, as KiCad drops it.
    /// </summary>
    public static string ConvertLegacyCodes(string text)
    {
        if (!text.Contains('%'))
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                result.Append(text[i]);
                continue;
            }

            if (++i >= text.Length)
            {
                break;
            }

            switch (text[i])
            {
                case '%': result.Append('%'); break;
                case 'D': result.Append("${ISSUE_DATE}"); break;
                case 'R': result.Append("${REVISION}"); break;
                case 'K': result.Append("${KICAD_VERSION}"); break;
                case 'Z': result.Append("${PAPER}"); break;
                case 'S': result.Append("${#}"); break;
                case 'N': result.Append("${##}"); break;
                case 'F': result.Append("${FILENAME}"); break;
                case 'L': result.Append("${LAYER}"); break;
                case 'P': result.Append("${SHEETPATH}"); break;
                case 'Y': result.Append("${COMPANY}"); break;
                case 'T': result.Append("${TITLE}"); break;
                case 'C':
                    if (++i < text.Length && text[i] is >= '0' and <= '8')
                    {
                        result.Append("${COMMENT").Append((char)(text[i] + 1)).Append('}');
                    }

                    break;
            }
        }

        return result.ToString();
    }

    private static WksPoint Point(SList? node)
    {
        if (node is null)
        {
            return default;
        }

        var corner = WksCorner.RightBottom;
        foreach (var atom in Atoms(node).Skip(3))
        {
            corner = atom.Value switch
            {
                "ltcorner" => WksCorner.LeftTop,
                "lbcorner" => WksCorner.LeftBottom,
                "rtcorner" => WksCorner.RightTop,
                _ => WksCorner.RightBottom,
            };
        }

        return new WksPoint(Number(node, 1), Number(node, 2), corner);
    }

    private static WksPages Pages(SList node)
    {
        var option = node.Find("option");
        return Atoms(option).Any(a => a.IsSymbol("page1only")) ? WksPages.FirstOnly
            : Atoms(option).Any(a => a.IsSymbol("notonpage1")) ? WksPages.NotOnFirst
            : WksPages.All;
    }

    /// <summary>KiCad reads a repeat count between 1 and 100.</summary>
    private static int Repeat(SList node) => Math.Clamp((int)(node.ChildDouble("repeat") ?? 1), 1, 100);

    private static IEnumerable<SAtom> Atoms(SList? list) => list?.OfType<SAtom>() ?? [];

    /// <summary>
    /// The image bytes of a bitmap: base64 in quoted lines under <c>(data …)</c>, as KiCad writes today, or the older
    /// <c>(pngdata (data "89 50 4E …") …)</c> with each byte as two hex digits.
    /// </summary>
    private static byte[]? ImageOf(SList bitmap)
    {
        try
        {
            if (bitmap.Find("data") is { } data)
            {
                return Convert.FromBase64String(string.Concat(Atoms(data).Skip(1).Select(a => a.Value)));
            }

            if (bitmap.Find("pngdata") is { } legacy)
            {
                var bytes = new List<byte>();
                foreach (var line in legacy.Lists().Where(l => l.Head == "data"))
                {
                    foreach (string pair in (line.Str(1) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        bytes.Add(Convert.ToByte(pair, 16));
                    }
                }

                return [.. bytes];
            }
        }
        catch (FormatException)
        {
        }

        return null;
    }

    private static double Number(SList node, int index) =>
        node.AtomAt(index)?.TryGetDouble(out double value) == true ? value : 0;

    /// <summary>
    /// The default drawing sheet, as a .kicad_wks: KiCad's default layout — a double border with a 50 mm scale
    /// round it, and the title block in the bottom-right corner.
    /// </summary>
    public const string DefaultText = """
        (kicad_wks (version 20210606) (generator "anode")
          (setup (textsize 1.5 1.5) (linewidth 0.15) (textlinewidth 0.15)
            (left_margin 10) (right_margin 10) (top_margin 10) (bottom_margin 10))
          (rect (start 110 34) (end 2 2))
          (rect (start 0 0 ltcorner) (end 0 0) (repeat 2) (incrx 2) (incry 2))
          (line (start 50 2 ltcorner) (end 50 0 ltcorner) (repeat 30) (incrx 50))
          (tbtext "1" (pos 25 1 ltcorner) (font (size 1.3 1.3)) (repeat 100) (incrx 50))
          (line (start 50 2 lbcorner) (end 50 0 lbcorner) (repeat 30) (incrx 50))
          (tbtext "1" (pos 25 1 lbcorner) (font (size 1.3 1.3)) (repeat 100) (incrx 50))
          (line (start 0 50 ltcorner) (end 2 50 ltcorner) (repeat 30) (incry 50))
          (tbtext "A" (pos 1 25 ltcorner) (font (size 1.3 1.3)) (justify center) (repeat 100) (incry 50))
          (line (start 0 50 rtcorner) (end 2 50 rtcorner) (repeat 30) (incry 50))
          (tbtext "A" (pos 1 25 rtcorner) (font (size 1.3 1.3)) (justify center) (repeat 100) (incry 50))
          (tbtext "Date: ${ISSUE_DATE}" (pos 87 6.9))
          (line (start 110 5.5) (end 2 5.5))
          (tbtext "${KICAD_VERSION}" (pos 109 4.1))
          (line (start 110 8.5) (end 2 8.5))
          (tbtext "Rev: ${REVISION}" (pos 24 6.9) (font bold))
          (tbtext "Size: ${PAPER}" (pos 109 6.9))
          (tbtext "Id: ${#}/${##}" (pos 24 4.1))
          (line (start 110 12.5) (end 2 12.5))
          (tbtext "Title: ${TITLE}" (pos 109 10.7) (font (size 2 2) bold italic))
          (tbtext "File: ${FILENAME}" (pos 109 14.3))
          (line (start 110 18.5) (end 2 18.5))
          (tbtext "Sheet: ${SHEETPATH}" (pos 109 17))
          (tbtext "${COMPANY}" (pos 109 20) (font bold))
          (tbtext "${COMMENT1}" (pos 109 23))
          (tbtext "${COMMENT2}" (pos 109 26))
          (tbtext "${COMMENT3}" (pos 109 29))
          (tbtext "${COMMENT4}" (pos 109 32))
          (line (start 90 8.5) (end 90 5.5))
          (line (start 26 8.5) (end 26 2)))
        """;
}
