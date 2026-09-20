using System.Text;

namespace Anode.Kicad;

/// <summary>
/// KiCad's escaping of text that its own syntax would otherwise claim. A label may not carry a "/" — that is the
/// hierarchy separator — so the file says <c>{slash}</c>; the same for a handful of other characters. Two labels,
/// one reading <c>VPP/MCLR</c> and one <c>VPP{slash}MCLR</c>, name the same net and are shown the same way.
///
/// Port of <c>UnescapeString</c> (common/string_utils.cpp), markup and all: <c>~{…}</c>, <c>^{…}</c>, <c>_{…}</c>
/// and <c>${…}</c> keep their braces, since those are the text's own markup rather than an escape.
/// </summary>
public static class KicadText
{
    /// <summary>The text as KiCad shows it: every <c>{token}</c> it knows put back as the character it stands for.</summary>
    public static string Unescape(string text)
    {
        if (text.Length <= 2 || !text.Contains('{', StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        char previous = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '{')
            {
                result.Append(c);
                previous = c;
                continue;
            }

            // The token runs to its matching brace; braces inside it nest.
            var token = new StringBuilder();
            int depth = 1;
            bool terminated = false;
            for (i++; i < text.Length; i++)
            {
                c = text[i];
                depth += c == '{' ? 1 : c == '}' ? -1 : 0;
                if (depth <= 0)
                {
                    terminated = true;
                    break;
                }

                token.Append(c);
            }

            string inside = token.ToString();
            if (!terminated)
            {
                result.Append('{').Append(Unescape(inside));
            }
            else if (previous is '$' or '~' or '^' or '_')
            {
                result.Append('{').Append(Unescape(inside)).Append('}');
            }
            else if (Tokens.TryGetValue(inside, out string? character))
            {
                result.Append(character);
            }
            else
            {
                result.Append('{').Append(Unescape(inside)).Append('}');
            }

            previous = c;
        }

        return result.ToString();
    }

    private static readonly Dictionary<string, string> Tokens = new(StringComparer.Ordinal)
    {
        ["dblquote"] = "\"",
        ["quote"] = "'",
        ["lt"] = "<",
        ["gt"] = ">",
        ["backslash"] = "\\",
        ["slash"] = "/",
        ["bar"] = "|",
        ["comma"] = ",",
        ["colon"] = ":",
        ["space"] = " ",
        ["dollar"] = "$",
        ["tab"] = "\t",
        ["return"] = "\n",
        ["brace"] = "{",
    };
}
