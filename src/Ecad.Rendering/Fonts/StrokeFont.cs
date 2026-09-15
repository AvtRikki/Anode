using System.IO.Compression;
using Ecad.Geometry;

namespace Ecad.Rendering.Fonts;

/// <summary>A decoded stroke glyph in font units: height 1 is the nominal text height, Y down, baseline near 0.</summary>
public sealed class StrokeGlyph(Vector2D[][] strokes, double advance)
{
    /// <summary>Polylines; each drawn with the text pen.</summary>
    public Vector2D[][] Strokes { get; } = strokes;

    /// <summary>Horizontal advance in font units (multiplied by the text width).</summary>
    public double Advance { get; } = advance;
}

/// <summary>
/// KiCad's newstroke font (Hershey-style encoding). Line N of the embedded table is code point U+0020 + N;
/// glyphs are decoded on first use.
/// </summary>
public sealed class StrokeFont
{
    /// <summary>One font unit in the table is 1/21 of the glyph height.</summary>
    private const double Scale = 1.0 / 21.0;

    /// <summary>Moves the origin to the baseline (KiCad's FONT_OFFSET).</summary>
    private const int BaselineOffset = -8;

    private const int FirstCodePoint = 0x20;

    private static readonly Lazy<StrokeFont> DefaultFont = new(LoadEmbedded);

    private readonly string[] _encoded;
    private readonly StrokeGlyph?[] _glyphs;

    public StrokeFont(string[] encodedGlyphs)
    {
        _encoded = encodedGlyphs;
        _glyphs = new StrokeGlyph?[encodedGlyphs.Length];
    }

    public static StrokeFont Default => DefaultFont.Value;

    public int GlyphCount => _encoded.Length;

    /// <summary>Glyph for a code point; unknown characters fall back to '?'.</summary>
    public StrokeGlyph GetGlyph(int codePoint)
    {
        int index = codePoint - FirstCodePoint;
        if ((uint)index >= (uint)_encoded.Length || _encoded[index].Length < 2)
        {
            index = '?' - FirstCodePoint;
        }

        return _glyphs[index] ?? Interlocked.CompareExchange(ref _glyphs[index], Decode(_encoded[index]), null) ?? _glyphs[index]!;
    }

    public double SpaceAdvance => GetGlyph(' ').Advance;

    internal static StrokeGlyph Decode(string encoded)
    {
        double startX = (encoded[0] - 'R') * Scale;
        double endX = (encoded[1] - 'R') * Scale;

        var strokes = new List<Vector2D[]>();
        var current = new List<Vector2D>();
        for (int i = 2; i + 1 < encoded.Length; i += 2)
        {
            if (encoded[i] == ' ' && encoded[i + 1] == 'R')
            {
                Flush();
                continue;
            }

            current.Add(new Vector2D(
                (encoded[i] - 'R') * Scale - startX,
                (encoded[i + 1] - 'R' + BaselineOffset) * Scale));
        }

        Flush();
        return new StrokeGlyph([.. strokes], endX - startX);

        void Flush()
        {
            if (current.Count > 0)
            {
                strokes.Add([.. current]);
                current.Clear();
            }
        }
    }

    private static StrokeFont LoadEmbedded()
    {
        using var stream = typeof(StrokeFont).Assembly.GetManifestResourceStream("Ecad.Rendering.Fonts.newstroke.txt.gz")
            ?? throw new InvalidOperationException("Embedded newstroke font is missing.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        var lines = new List<string>(66_000);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return new StrokeFont([.. lines]);
    }
}
