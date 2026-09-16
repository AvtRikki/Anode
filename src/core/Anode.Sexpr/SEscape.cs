using System.Text;

namespace Anode.Sexpr;

/// <summary>Quoting rules compatible with KiCad's OUTPUTFORMATTER::Quotes and DSNLEXER.</summary>
public static class SEscape
{
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    public static string Unquote(string raw)
    {
        ReadOnlySpan<char> s = raw.AsSpan(1, raw.Length - 2);
        if (s.IndexOf('\\') < 0)
        {
            return s.ToString();
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }

            char e = s[++i];
            switch (e)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'x' when i + 2 < s.Length && IsHex(s[i + 1]) && IsHex(s[i + 2]):
                    sb.Append((char)Convert.ToInt32(s.Slice(i + 1, 2).ToString(), 16));
                    i += 2;
                    break;
                case >= '0' and <= '7':
                    int start = i, len = 1;
                    while (len < 3 && start + len < s.Length && s[start + len] is >= '0' and <= '7')
                    {
                        len++;
                    }

                    sb.Append((char)Convert.ToInt32(s.Slice(start, len).ToString(), 8));
                    i = start + len - 1;
                    break;
                default: sb.Append(e); break;
            }
        }

        return sb.ToString();
    }

    public static void ValidateSymbol(string text)
    {
        if (text.Length == 0 || text.AsSpan().IndexOfAny(" \t\r\n()\"") >= 0)
        {
            throw new ArgumentException($"'{text}' is not a valid unquoted symbol.", nameof(text));
        }
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}
