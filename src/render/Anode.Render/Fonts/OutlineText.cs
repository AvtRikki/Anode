using System.Collections.Concurrent;
using Anode.Geometry;
using Anode.Kicad;
using SkiaSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFace = HarfBuzzSharp.Face;
using HbFont = HarfBuzzSharp.Font;

namespace Anode.Render.Fonts;

/// <summary>
/// Text in a typeface, as filled outlines — how KiCad draws text whose font names a face. Placed as KiCad places it
/// (<c>FONT::getLinePositions</c>, <c>OUTLINE_FONT::getTextAsGlyphs</c>): the em is 1.4 times the text height, the
/// first baseline sits one text height below the top, lines are 1.68 heights apart, glyphs are shaped by HarfBuzz so
/// kerning and ligatures come out the same, and superscripts, subscripts and overbars follow KiCad's proportions.
/// A font the file itself carries is used first (<see cref="Embed"/>); a face neither the file nor this machine has
/// is stood in for by one it has, as fontconfig does for KiCad; <see cref="Substitute"/> says which.
/// </summary>
public static class OutlineText
{
    /// <summary>One filled region: an outline and the holes inside it, in the caller's units.</summary>
    public sealed record Shape(Vector2D[] Outline, IReadOnlyList<Vector2D[]> Holes);

    /// <summary>KiCad's outline font size compensation: faces are sized on their full height, strokes on capitals.</summary>
    private const double EmPerHeight = 1.4;

    private const double InterlinePitch = 1.68;
    private const double FirstLineHeightFactor = 1.17;
    private const double OverbarHeight = 1.23;
    private const double SuperSubScale = 0.64;
    private const double SubscriptDrop = 0.25;
    private const double SuperscriptRise = 0.45;
    private const double TabColumns = 4 * 0.6;

    /// <summary>Glyphs are read at this size and scaled; large enough that their curves flatten smoothly.</summary>
    private const float Units = 1000f;

    private const int CurveSteps = 8;

    /// <summary>The slant KiCad gives a face that has no italic of its own.</summary>
    private static readonly (double Sin, double Cos) FakeSlant = (Math.Sin(12 * Math.PI / 180), Math.Cos(12 * Math.PI / 180));

    private static readonly string[] MonospaceStandIns = ["Courier New", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Consolas"];

    private static readonly string[] SerifStandIns = ["Times New Roman", "Times", "Liberation Serif", "DejaVu Serif"];

    private static readonly ConcurrentDictionary<(string Face, bool Bold, bool Italic), Face> Faces = new(FaceKeyComparer.Instance);

    private static readonly ConcurrentDictionary<(Face Face, ushort Glyph), Vector2D[][]> Glyphs = new();

    /// <summary>Faces read from files' embedded fonts, for the life of the process, as fontconfig keeps them.</summary>
    private static readonly List<SKTypeface> Embedded = [];

    private static readonly HashSet<string> EmbeddedKeys = new(StringComparer.Ordinal);

    /// <summary>Whether this machine has <paramref name="face"/> itself, not a stand-in.</summary>
    public static bool IsInstalled(string face) => !Resolve(face, false, false).Substituted;

    /// <summary>The family drawn in place of <paramref name="face"/>, or null when this machine has the face.</summary>
    public static string? Substitute(string face) => Resolve(face, false, false) is { Substituted: true } f ? f.Family : null;

    /// <summary>Of <paramref name="faces"/>, those this machine lacks, each with the family drawn in its place.</summary>
    public static IReadOnlyList<(string Face, string StandIn)> StandIns(IEnumerable<string> faces) =>
        [.. faces.Distinct(StringComparer.OrdinalIgnoreCase).Select(f => (f, Substitute(f)!)).Where(p => p.Item2 is not null)];

    /// <summary>Sets <paramref name="text"/> about <paramref name="anchor"/>.</summary>
    /// <param name="fill">Receives each filled region of every glyph.</param>
    /// <param name="bar">Receives each overbar, a stroke as wide as <see cref="TextStyle.PenWidth"/>.</param>
    public static void Layout(
        string face,
        bool bold,
        string text,
        Vector2D anchor,
        in TextStyle style,
        Action<Shape> fill,
        Action<Vector2D, Vector2D> bar)
    {
        if (style.Height <= 0 || style.Width == 0)
        {
            return;
        }

        var font = Resolve(face, bold, style.Italic);
        string[] lines = TextMarkup.Lines(text);
        double interline = style.Height * InterlinePitch * font.LineHeightRatio * style.LineSpacing;
        double height = (style.Height * FirstLineHeightFactor) + ((lines.Length - 1) * interline);
        double top = style.Height - style.VAlign switch
        {
            TextVAlign.Center => height / 2,
            TextVAlign.Bottom => height,
            _ => 0,
        };

        // Mirrored about the anchor, then turned about it, as KiCad transforms each glyph point.
        var (sin, cos) = Transform2D.SinCos(style.AngleDegrees);
        bool mirrored = style.Mirrored;
        Vector2D Place(Vector2D p)
        {
            double dx = mirrored ? -p.X : p.X;
            return new Vector2D(anchor.X + (dx * cos) + (p.Y * sin), anchor.Y - (dx * sin) + (p.Y * cos));
        }

        for (int i = 0; i < lines.Length; i++)
        {
            double width = Run(font, lines[i], style, 0, 0, null, null);
            double left = style.HAlign switch
            {
                TextHAlign.Center => -width / 2,
                TextHAlign.Right => -width,
                _ => 0,
            };

            Run(font, lines[i], style, left, top + (i * interline),
                shape => fill(new Shape([.. shape.Outline.Select(Place)], [.. shape.Holes.Select(h => h.Select(Place).ToArray())])),
                (a, b) => bar(Place(a), Place(b)));
        }
    }

    /// <summary>Advance width of a single line, markup and tabs included, in the caller's units.</summary>
    public static double MeasureLine(string face, bool bold, string line, in TextStyle style) =>
        Run(Resolve(face, bold, style.Italic), line, style, 0, 0, null, null);

    /// <summary>
    /// Sets one line with its baseline at <paramref name="baseline"/>, unturned and unmirrored, starting at
    /// <paramref name="x"/>; answers how far it advanced. With no receivers it only measures.
    /// </summary>
    private static double Run(Face font, string line, in TextStyle style, double x, double baseline, Action<Shape>? fill, Action<Vector2D, Vector2D>? bar)
    {
        double start = x;
        double tab = Math.Round(style.Width * TabColumns);
        foreach (var (run, markup) in TextMarkup.Parse(line))
        {
            bool small = markup.HasFlag(TextMarkup.Style.Superscript) || markup.HasFlag(TextMarkup.Style.Subscript);
            double scale = small ? SuperSubScale : 1;
            double sx = EmPerHeight * style.Width * scale, sy = EmPerHeight * style.Height * scale;
            double shift = markup.HasFlag(TextMarkup.Style.Subscript) ? SubscriptDrop * sy
                : markup.HasFlag(TextMarkup.Style.Superscript) ? -SuperscriptRise * sy
                : 0;
            double runStart = x;

            string[] pieces = run.Split('\t');
            for (int p = 0; p < pieces.Length; p++)
            {
                if (p > 0 && tab > 0)
                {
                    // Locked to the next column of four space widths, as KiCad's tabs are.
                    x += tab - ((x - start) % tab);
                }

                x = SetRun(font, pieces[p], x, baseline + shift, sx, sy, fill);
            }

            if (bar is not null && markup.HasFlag(TextMarkup.Style.Overbar) && x > runStart)
            {
                double trim = style.Width * 0.1;
                double y = baseline - (style.Height * OverbarHeight);
                bar(new Vector2D(runStart + trim, y), new Vector2D(x - trim, y));
            }
        }

        return x - start;
    }

    /// <summary>Shapes one run with HarfBuzz and emits its glyphs; <paramref name="sx"/> and <paramref name="sy"/> are the em.</summary>
    private static double SetRun(Face font, string text, double x, double baseline, double sx, double sy, Action<Shape>? fill)
    {
        if (text.Length == 0)
        {
            return x;
        }

        using var buffer = new HbBuffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        font.Shaper.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        double perUnit = 1.0 / font.UnitsPerEm;
        for (int i = 0; i < infos.Length; i++)
        {
            if (fill is not null)
            {
                double gx = x + (positions[i].XOffset * perUnit * sx);
                double gy = baseline - (positions[i].YOffset * perUnit * sy);
                foreach (var shape in Group(Outline(font, (ushort)infos[i].Codepoint), gx, gy, sx / Units, sy / Units))
                {
                    fill(shape);
                }
            }

            x += positions[i].XAdvance * perUnit * sx;
        }

        return x;
    }

    /// <summary>A glyph's contours at <see cref="Units"/> to the em, y down, slanted if the face has no italic.</summary>
    private static Vector2D[][] Outline(Face font, ushort glyph) => Glyphs.GetOrAdd((font, glyph), key =>
    {
        using var skFont = new SKFont(key.Face.Typeface, Units);
        using var path = skFont.GetGlyphPath(key.Glyph);
        if (path is null)
        {
            return [];
        }

        return [.. Flatten(path).Select(c => c.Select(p => key.Face.FakeItalic
            ? new Vector2D((p.X * FakeSlant.Cos) - (p.Y * FakeSlant.Sin), p.Y)
            : new Vector2D(p.X, p.Y)).ToArray())];
    });

    /// <summary>
    /// Makes the fonts among <paramref name="files"/> available by their family, ahead of the faces this machine has
    /// — as KiCad adds a file's embedded fonts to fontconfig, for every text from then on. Each font is read once;
    /// answers how many were new.
    /// </summary>
    public static int Embed(IEnumerable<EmbeddedFile> files)
    {
        int added = 0;
        foreach (var file in files.Where(f => f.IsFont && f.HasData))
        {
            string key = file.Checksum ?? file.Name;
            lock (Embedded)
            {
                if (!EmbeddedKeys.Add(key))
                {
                    continue;
                }
            }

            if (file.Data is not { } data || SKTypeface.FromData(SKData.CreateCopy(data)) is not { } typeface
                || string.IsNullOrEmpty(typeface.FamilyName))
            {
                continue;
            }

            lock (Embedded)
            {
                Embedded.Add(typeface);
            }

            // What was resolved for this family before — most likely a stand-in — gives way.
            foreach (var resolved in Faces.Keys.Where(k => string.Equals(k.Face, typeface.FamilyName, StringComparison.OrdinalIgnoreCase)))
            {
                Faces.TryRemove(resolved, out _);
            }

            added++;
        }

        return added;
    }

    /// <summary>Whether <paramref name="face"/> is drawn from a font a file carried rather than one installed.</summary>
    public static bool IsEmbedded(string face) => Resolve(face, false, false).Embedded;

    private static Face Resolve(string face, bool bold, bool italic) => Faces.GetOrAdd((face, bold, italic), key =>
    {
        // fontconfig in KiCad: a name that says it is heavy is looked up bold, whatever the text asks.
        bool heavy = key.Bold || new[] { "bold", "heavy", "black", "thick", "dark" }
            .Any(w => key.Face.Contains(w, StringComparison.OrdinalIgnoreCase));
        var style = new SKFontStyle(
            heavy ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        // A font the file carries comes first; then the machine's. The font manager answers nothing for a family it
        // lacks, where the typeface factory would quietly hand back its default; so the lack is noticed here, and
        // the stand-in chosen deliberately.
        var embedded = FromFile(key.Face, style);
        var typeface = embedded ?? Match(key.Face, style);
        bool substituted = typeface is null;
        typeface ??= StandIn(key.Face, style);

        // A bold the family lacks is made up by KiCad with a one-pixel embolden at 1152 dpi — too slight to see, so
        // it is left out. A missing italic is slanted by 12°, which shows.
        bool fakeItalic = key.Italic && typeface.FontSlant == SKFontStyleSlant.Upright;
        return new Face(typeface, substituted, fakeItalic) { Embedded = embedded is not null };
    });

    /// <summary>The embedded face of <paramref name="family"/> closest to <paramref name="style"/>, if any.</summary>
    private static SKTypeface? FromFile(string family, SKFontStyle style)
    {
        lock (Embedded)
        {
            return Embedded
                .Where(t => string.Equals(t.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => (t.FontSlant == SKFontStyleSlant.Upright) == (style.Slant == SKFontStyleSlant.Upright) ? 0 : 1)
                .ThenBy(t => Math.Abs(t.FontWeight - style.Weight))
                .FirstOrDefault();
        }
    }

    private static SKTypeface? Match(string family, SKFontStyle style) =>
        SKFontManager.Default.MatchFamily(family, style) is { } found && !string.IsNullOrEmpty(found.FamilyName) ? found : null;

    /// <summary>
    /// What fontconfig would reach for: a monospaced face for a monospaced name, a serif for a serif, otherwise the
    /// system's own sans.
    /// </summary>
    private static SKTypeface StandIn(string face, SKFontStyle style)
    {
        bool Has(string part) => face.Contains(part, StringComparison.OrdinalIgnoreCase);
        string[] candidates = Has("Mono") || Has("Courier") || Has("Consol") ? MonospaceStandIns
            : (Has("Serif") && !Has("Sans")) || Has("Times") ? SerifStandIns
            : [];

        return candidates.Select(c => Match(c, style)).FirstOrDefault(t => t is not null)
            ?? Match(SKTypeface.Default.FamilyName, style)
            ?? SKTypeface.Default;
    }

    /// <summary>The path's contours as polygons, curves cut into short straight steps.</summary>
    private static List<List<SKPoint>> Flatten(SKPath path)
    {
        var contours = new List<List<SKPoint>>();
        List<SKPoint>? current = null;
        using var iterator = path.CreateRawIterator();
        var p = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    current = [p[0]];
                    contours.Add(current);
                    break;
                case SKPathVerb.Line:
                    current?.Add(p[1]);
                    break;
                case SKPathVerb.Quad:
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        current?.Add(new SKPoint((u * u * p[0].X) + (2 * u * t * p[1].X) + (t * t * p[2].X), (u * u * p[0].Y) + (2 * u * t * p[1].Y) + (t * t * p[2].Y)));
                    }

                    break;
                case SKPathVerb.Conic:
                    float w = iterator.ConicWeight();
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        float a = u * u, b = 2 * w * u * t, c = t * t, d = a + b + c;
                        current?.Add(new SKPoint(((a * p[0].X) + (b * p[1].X) + (c * p[2].X)) / d, ((a * p[0].Y) + (b * p[1].Y) + (c * p[2].Y)) / d));
                    }

                    break;
                case SKPathVerb.Cubic:
                    for (int s = 1; s <= CurveSteps; s++)
                    {
                        float t = s / (float)CurveSteps, u = 1 - t;
                        float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                        current?.Add(new SKPoint((a * p[0].X) + (b * p[1].X) + (c * p[2].X) + (d * p[3].X), (a * p[0].Y) + (b * p[1].Y) + (c * p[2].Y) + (d * p[3].Y)));
                    }

                    break;
            }
        }

        foreach (var contour in contours.Where(c => c.Count > 1 && c[0] == c[^1]))
        {
            contour.RemoveAt(contour.Count - 1);
        }

        return [.. contours.Where(c => c.Count >= 3)];
    }

    /// <summary>
    /// A glyph's contours, placed, sorted into shapes by how deeply each is nested: an outline at even depth, a hole
    /// at odd — the inside of an "o", the island in a "®" — each hole belonging to the smallest outline around it.
    /// </summary>
    private static IEnumerable<Shape> Group(Vector2D[][] glyph, double x, double y, double sx, double sy)
    {
        var contours = glyph.Select(c => c.Select(p => new Vector2D(x + (p.X * sx), y + (p.Y * sy))).ToArray()).ToList();
        int n = contours.Count;
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i != j && Inside(contours[i][0], contours[j]))
                {
                    depth[i]++;
                }
            }
        }

        var holes = Enumerable.Range(0, n).ToDictionary(i => i, _ => new List<Vector2D[]>());
        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 1)
            {
                int parent = Enumerable.Range(0, n)
                    .Where(j => depth[j] == depth[i] - 1 && Inside(contours[i][0], contours[j]))
                    .OrderBy(j => Math.Abs(Area(contours[j])))
                    .DefaultIfEmpty(-1)
                    .First();
                if (parent >= 0)
                {
                    holes[parent].Add(contours[i]);
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 == 0)
            {
                yield return new Shape(contours[i], holes[i]);
            }
        }
    }

    private static bool Inside(Vector2D p, Vector2D[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)
                && p.X < ((polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y)) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Area(Vector2D[] polygon)
    {
        double sum = 0;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            sum += (polygon[j].X * polygon[i].Y) - (polygon[i].X * polygon[j].Y);
        }

        return sum / 2;
    }

    /// <summary>A face as it will be drawn: its typeface, a HarfBuzz font over the same file, and what was made up.</summary>
    private sealed class Face
    {
        public Face(SKTypeface typeface, bool substituted, bool fakeItalic)
        {
            Typeface = typeface;
            Substituted = substituted;
            FakeItalic = fakeItalic;
            Family = typeface.FamilyName;
            UnitsPerEm = Math.Max(1, typeface.UnitsPerEm);

            // HarfBuzz reads the font file in place for as long as the face lives: it gets Skia's copy, native memory
            // that never moves, released when HarfBuzz lets go of it.
            using var stream = typeface.OpenStream(out int index);
            var data = SKData.Create(stream);
            using var blob = new HarfBuzzSharp.Blob(data.Data, (int)data.Size, HarfBuzzSharp.MemoryMode.ReadOnly, data.Dispose);
            using var hbFace = new HbFace(blob, index);
            Shaper = new HbFont(hbFace);
            Shaper.SetScale(UnitsPerEm, UnitsPerEm);

            // KiCad scales the interline by the face's line height over its em in whole units — an integer division,
            // so 1 for nearly every face.
            using var font = new SKFont(typeface, UnitsPerEm);
            var metrics = font.Metrics;
            int lineHeight = (int)Math.Round(metrics.Descent - metrics.Ascent + metrics.Leading);
            LineHeightRatio = lineHeight / UnitsPerEm;
        }

        public SKTypeface Typeface { get; }

        public HbFont Shaper { get; }

        public string Family { get; }

        public bool Substituted { get; }

        public bool FakeItalic { get; }

        /// <summary>Read from a font a file carried.</summary>
        public bool Embedded { get; init; }

        public int UnitsPerEm { get; }

        public int LineHeightRatio { get; }
    }

    /// <summary>Face names compared as fontconfig compares them: without regard to case.</summary>
    private sealed class FaceKeyComparer : IEqualityComparer<(string Face, bool Bold, bool Italic)>
    {
        public static readonly FaceKeyComparer Instance = new();

        public bool Equals((string Face, bool Bold, bool Italic) x, (string Face, bool Bold, bool Italic) y) =>
            string.Equals(x.Face, y.Face, StringComparison.OrdinalIgnoreCase) && x.Bold == y.Bold && x.Italic == y.Italic;

        public int GetHashCode((string Face, bool Bold, bool Italic) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Face), obj.Bold, obj.Italic);
    }
}
