using System.Globalization;

namespace Ecad.Sexpr;

public enum SAtomKind : byte
{
    /// <summary>Unquoted token: keyword, number, enum value.</summary>
    Symbol,

    /// <summary>Double-quoted string with KiCad escapes.</summary>
    String,
}

public sealed class SAtom : SNode
{
    private string _raw;

    internal SAtom(string raw, SAtomKind kind)
    {
        _raw = raw;
        Kind = kind;
    }

    public SAtomKind Kind { get; private set; }

    /// <summary>Token text exactly as written to the file (quotes and escapes included).</summary>
    public string Raw => _raw;

    /// <summary>Logical value: symbol text, or the unescaped string contents.</summary>
    public string Value => Kind == SAtomKind.String ? SEscape.Unquote(_raw) : _raw;

    public bool IsSymbol(string text) => Kind == SAtomKind.Symbol && _raw == text;

    public bool TryGetDouble(out double value) =>
        double.TryParse(Kind == SAtomKind.String ? Value : _raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public double AsDouble() =>
        TryGetDouble(out var v) ? v : throw new FormatException($"Atom '{_raw}' is not a number.");

    public static SAtom Symbol(string text)
    {
        SEscape.ValidateSymbol(text);
        return new SAtom(text, SAtomKind.Symbol);
    }

    public static SAtom String(string value) => new(SEscape.Quote(value), SAtomKind.String);

    public static SAtom Number(double value) => new(SNumber.Format(value), SAtomKind.Symbol);

    /// <summary>Replaces the value in place; surrounding whitespace is kept.</summary>
    public void SetSymbol(string text)
    {
        SEscape.ValidateSymbol(text);
        _raw = text;
        Kind = SAtomKind.Symbol;
    }

    public void SetString(string value)
    {
        _raw = SEscape.Quote(value);
        Kind = SAtomKind.String;
    }

    public void SetNumber(double value)
    {
        _raw = SNumber.Format(value);
        Kind = SAtomKind.Symbol;
    }
}
