using System.Globalization;

namespace Anode.Sexpr;

public static class SNumber
{
    /// <summary>Shortest invariant representation without exponent, trailing zeros or "-0".</summary>
    public static string Format(double value)
    {
        string s = value.ToString("0.##########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }
}
