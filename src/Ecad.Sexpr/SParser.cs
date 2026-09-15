namespace Ecad.Sexpr;

public sealed class SexprParseException(string message, int line, int column)
    : Exception($"{message} (line {line}, column {column})")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>Lossless S-expression parser: every byte of whitespace is kept as trivia.</summary>
public sealed class SParser
{
    private const int MaxDepth = 512;
    private const int MaxInternLength = 32;

    private readonly string _text;
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _pool;
    private int _pos;

    private SParser(string text)
    {
        _text = text;
        _pool = new Dictionary<string, string>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public static SDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SParser(text).ParseDocument();
    }

    private SDocument ParseDocument()
    {
        var doc = new SDocument();
        while (true)
        {
            string trivia = ReadTrivia();
            if (_pos >= _text.Length)
            {
                doc.TrailingTrivia = trivia;
                return doc;
            }

            if (_text[_pos] == ')')
            {
                throw Error("Unexpected ')'", _pos);
            }

            var node = ParseNode(0);
            node.LeadingTrivia = trivia;
            doc.AddRoot(node);
        }
    }

    private SNode ParseNode(int depth) => _text[_pos] == '(' ? ParseList(depth) : ParseAtom();

    private SList ParseList(int depth)
    {
        if (depth >= MaxDepth)
        {
            throw Error("Nesting too deep", _pos);
        }

        int open = _pos++;
        var list = new SList();
        while (true)
        {
            string trivia = ReadTrivia();
            if (_pos >= _text.Length)
            {
                throw Error("Unclosed '('", open);
            }

            if (_text[_pos] == ')')
            {
                _pos++;
                list.CloseTrivia = trivia;
                list.TrimExcess();
                return list;
            }

            var child = ParseNode(depth + 1);
            child.LeadingTrivia = trivia;
            list.AddParsed(child);
        }
    }

    private SAtom ParseAtom()
    {
        int start = _pos;
        if (_text[_pos] == '"')
        {
            int i = _pos + 1;
            while (true)
            {
                if (i >= _text.Length)
                {
                    throw Error("Unterminated string", start);
                }

                char c = _text[i];
                if (c == '\\')
                {
                    i += 2;
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    i++;
                }
            }

            _pos = i + 1;
            return new SAtom(Slice(start, _pos), SAtomKind.String);
        }

        while (_pos < _text.Length && !IsWhitespace(_text[_pos]) && _text[_pos] is not ('(' or ')'))
        {
            _pos++;
        }

        return new SAtom(Slice(start, _pos), SAtomKind.Symbol);
    }

    private string ReadTrivia()
    {
        int start = _pos;
        while (_pos < _text.Length && IsWhitespace(_text[_pos]))
        {
            _pos++;
        }

        return Slice(start, _pos);
    }

    private string Slice(int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return string.Empty;
        }

        var span = _text.AsSpan(start, length);
        if (length > MaxInternLength)
        {
            return span.ToString();
        }

        if (!_pool.TryGetValue(span, out var s))
        {
            s = span.ToString();
            _pool[s] = s;
        }

        return s;
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f' or '\v';

    private SexprParseException Error(string message, int position)
    {
        int line = 1, lineStart = 0;
        for (int i = 0; i < position && i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new SexprParseException(message, line, position - lineStart + 1);
    }
}
