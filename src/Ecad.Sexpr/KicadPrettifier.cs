using System.Text;

namespace Ecad.Sexpr;

public enum KicadFormatMode
{
    Normal,

    /// <summary>Keeps font/stroke/fill/... lists on one line (schematic and symbol files).</summary>
    CompactTextProperties,

    /// <summary>Keeps each (lib ...) row on one line (fp-lib-table, sym-lib-table).</summary>
    LibraryTable,
}

/// <summary>
/// Port of KICAD_FORMAT::Prettify (common/io/kicad/kicad_io_utils.cpp). Kept deliberately close to the
/// original control flow so the output matches KiCad byte-for-byte.
/// </summary>
public static class KicadPrettifier
{
    private const char QuoteChar = '"';
    private const char IndentChar = '\t';
    private const int IndentSize = 1;
    private const int XySpecialCaseColumnLimit = 99;
    private const int ConsecutiveTokenWrapThreshold = 72;

    private static readonly string[] ShortFormTokens = ["font", "stroke", "fill", "teardrop", "offset", "rotate", "scale"];

    public static string Prettify(string source, KicadFormatMode mode = KicadFormatMode.Normal)
    {
        bool textSpecialCase = mode == KicadFormatMode.CompactTextProperties;
        bool libSpecialCase = mode == KicadFormatMode.LibraryTable;

        var formatted = new StringBuilder(source.Length + source.Length / 4);

        int listDepth = 0;
        int libDepth = 0;
        char lastNonWhitespace = '\0';
        bool inQuote = false;
        bool hasInsertedSpace = false;
        bool inMultiLineList = false;
        bool inXY = false;
        bool inShortForm = false;
        bool inLibRow = false;
        int shortFormDepth = 0;
        int column = 0;
        int backslashCount = 0;

        for (int cursor = 0; cursor < source.Length; cursor++)
        {
            char c = source[cursor];
            char next = NextNonWhitespace(source, cursor);

            if (IsWhitespace(c) && !inQuote)
            {
                if (!hasInsertedSpace
                    && listDepth > 0
                    && lastNonWhitespace != '('
                    && next != ')'
                    && next != '(')
                {
                    if (inXY || column < ConsecutiveTokenWrapThreshold)
                    {
                        formatted.Append(' ');
                        column++;
                    }
                    else if (inShortForm || inLibRow)
                    {
                        formatted.Append(' ');
                    }
                    else
                    {
                        formatted.Append('\n').Append(IndentChar, listDepth * IndentSize);
                        column = listDepth * IndentSize;
                        inMultiLineList = true;
                    }

                    hasInsertedSpace = true;
                }
            }
            else
            {
                hasInsertedSpace = false;

                if (c == '(' && !inQuote)
                {
                    bool currentIsXY = IsXY(source, cursor);
                    bool currentIsShortForm = textSpecialCase && IsToken(source, cursor, ShortFormTokens);
                    bool currentIsLib = libSpecialCase && IsToken(source, cursor, ["lib"]);

                    if (formatted.Length == 0)
                    {
                        formatted.Append('(');
                        column++;
                    }
                    else if (inXY && currentIsXY && column < XySpecialCaseColumnLimit)
                    {
                        formatted.Append(" (");
                        column += 2;
                    }
                    else if (inShortForm || inLibRow)
                    {
                        formatted.Append(" (");
                        column += 2;
                    }
                    else
                    {
                        formatted.Append('\n').Append(IndentChar, listDepth * IndentSize).Append('(');
                        column = listDepth * IndentSize + 1;
                    }

                    inXY = currentIsXY;

                    if (currentIsShortForm)
                    {
                        inShortForm = true;
                        shortFormDepth = listDepth;
                    }
                    else if (currentIsLib)
                    {
                        inLibRow = true;
                        libDepth = listDepth;
                    }

                    listDepth++;
                }
                else if (c == ')' && !inQuote)
                {
                    if (listDepth > 0)
                    {
                        listDepth--;
                    }

                    if (inShortForm)
                    {
                        formatted.Append(')');
                        column++;
                    }
                    else if (inLibRow && listDepth == libDepth)
                    {
                        formatted.Append(')');
                        inLibRow = false;
                    }
                    else if (lastNonWhitespace == ')' || inMultiLineList)
                    {
                        formatted.Append('\n').Append(IndentChar, listDepth * IndentSize).Append(')');
                        column = listDepth * IndentSize + 1;
                        inMultiLineList = false;
                    }
                    else
                    {
                        formatted.Append(')');
                        column++;
                    }

                    if (shortFormDepth == listDepth)
                    {
                        inShortForm = false;
                        shortFormDepth = 0;
                    }
                }
                else
                {
                    if (c == '\\')
                    {
                        backslashCount++;
                    }
                    else if (c == QuoteChar && (backslashCount & 1) == 0)
                    {
                        inQuote = !inQuote;
                    }

                    if (c != '\\')
                    {
                        backslashCount = 0;
                    }

                    formatted.Append(c);
                    column++;
                }

                lastNonWhitespace = c;
            }
        }

        formatted.Append('\n');
        return formatted.ToString();
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static char NextNonWhitespace(string s, int i)
    {
        while (i < s.Length && IsWhitespace(s[i]))
        {
            i++;
        }

        return i < s.Length ? s[i] : '\0';
    }

    private static bool IsXY(string s, int i) =>
        i + 3 < s.Length && s[i + 1] == 'x' && s[i + 2] == 'y' && s[i + 3] == ' ';

    private static bool IsToken(string s, int i, string[] tokens)
    {
        int start = i + 1, end = start;
        while (end < s.Length && char.IsAsciiLetter(s[end]))
        {
            end++;
        }

        var token = s.AsSpan(start, end - start);
        foreach (var t in tokens)
        {
            if (token.SequenceEqual(t))
            {
                return true;
            }
        }

        return false;
    }
}
