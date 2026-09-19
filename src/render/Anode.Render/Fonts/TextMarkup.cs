using System.Text;

namespace Anode.Render.Fonts;

/// <summary>KiCad's text markup — <c>~{overbar}</c>, <c>^{superscript}</c>, <c>_{subscript}</c> — shared by both fonts.</summary>
internal static class TextMarkup
{
    [Flags]
    public enum Style
    {
        None = 0,
        Overbar = 1,
        Superscript = 2,
        Subscript = 4,
    }

    /// <summary>
    /// The lines of a text as KiCad splits them (<c>wxStringSplit</c>): at each line break, with a trailing break
    /// ending the last line rather than opening an empty one.
    /// </summary>
    public static string[] Lines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return [.. lines];
    }

    /// <summary>Splits KiCad markup into runs; unbalanced markup is kept literally.</summary>
    public static List<(string Run, Style Style)> Parse(string line)
    {
        var runs = new List<(string, Style)>();
        var plain = new StringBuilder();
        int i = 0;
        while (i < line.Length)
        {
            if (i + 1 < line.Length && line[i + 1] == '{' && line[i] is '~' or '^' or '_')
            {
                int close = line.IndexOf('}', i + 2);
                if (close > 0)
                {
                    if (plain.Length > 0)
                    {
                        runs.Add((plain.ToString(), Style.None));
                        plain.Clear();
                    }

                    var style = line[i] switch
                    {
                        '~' => Style.Overbar,
                        '^' => Style.Superscript,
                        _ => Style.Subscript,
                    };
                    runs.Add((line[(i + 2)..close], style));
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(line[i]);
            i++;
        }

        if (plain.Length > 0 || runs.Count == 0)
        {
            runs.Add((plain.ToString(), Style.None));
        }

        return runs;
    }
}
