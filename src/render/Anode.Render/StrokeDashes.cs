using System.Numerics;

namespace Anode.Render;

/// <summary>
/// Cutting a line into the dashes its style asks for. The lengths are KiCad's, which are ISO 128-2's: a dash is
/// eleven times the line's width, a dot a fifth of it, and the gap between them four times it
/// (<c>RENDER_SETTINGS::GetDashLength</c> and its neighbours, with KiCad's correction of one).
///
/// The cutting is done here, in the world the drawing lives in, rather than by each backend: a dash is a length on
/// the sheet, not on the screen, so it belongs with the geometry and looks the same however the scene is painted.
/// </summary>
public static class StrokeDashes
{
    /// <summary>The style KiCad writes for a line drawn the ordinary way.</summary>
    public const string Solid = "solid";

    /// <summary>
    /// The pattern a style asks for as lengths that alternate — drawn, skipped, drawn, skipped — or null for a line
    /// that is simply drawn. A width of nothing has no pattern either: every length would be nothing.
    /// </summary>
    public static float[]? Pattern(string? style, float width)
    {
        if (width <= 0)
        {
            return null;
        }

        float dash = 11 * width, dot = 0.2f * width, gap = 4 * width;
        return style switch
        {
            "dash" => [dash, gap],
            "dot" => [dot, gap],
            "dash_dot" => [dash, gap, dot, gap],
            "dash_dot_dot" => [dash, gap, dot, gap, dot, gap],
            _ => null,
        };
    }

    /// <summary>
    /// How many times a pattern may repeat along one line. A line whose dashes are finer than this cannot be told
    /// from a solid one on any screen, and cutting it would cost hundreds of thousands of pieces to draw something
    /// that looks exactly like the line we started with.
    /// </summary>
    private const int MostRepeats = 1000;

    /// <summary>
    /// The pieces of the line that are actually drawn. A line shorter than its first dash is drawn whole: a dashed
    /// line too short to show a dash still has to be visible, and KiCad draws one too. So is a line whose dashes are
    /// too fine to tell apart, which is what it would look like anyway.
    /// </summary>
    public static IEnumerable<(Vector2 From, Vector2 To)> Cut(Vector2 a, Vector2 b, float[] pattern)
    {
        float length = Vector2.Distance(a, b);
        if (length <= 0 || pattern.Length == 0 || length <= pattern[0])
        {
            yield return (a, b);
            yield break;
        }

        float cycle = 0;
        foreach (float run in pattern)
        {
            cycle += run;
        }

        if (cycle <= 0 || length / cycle > MostRepeats)
        {
            yield return (a, b);
            yield break;
        }

        var along = (b - a) / length;
        float at = 0;
        for (int step = 0; at < length; step++)
        {
            float next = at + pattern[step % pattern.Length];

            // A step that does not move along the line would go round for ever. It happens for real: a dash a
            // fraction of a nanometre long, on a line millimetres away from where it started, is lost entirely in
            // the rounding — so the rest of the line is drawn as it stands rather than cut into pieces that never
            // arrive.
            if (!(next > at))
            {
                yield return (a + (along * at), b);
                yield break;
            }

            if (step % 2 == 0)
            {
                yield return (a + (along * at), a + (along * Math.Min(next, length)));
            }

            at = next;
        }
    }
}
