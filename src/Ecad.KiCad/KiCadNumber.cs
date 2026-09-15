using System.Globalization;
using Ecad.Sexpr;

namespace Ecad.KiCad;

/// <summary>Number formatting as KiCad writes board files.</summary>
public static class KiCadNumber
{
    /// <summary>Millimetres with up to six decimals and no trailing zeros, exact for any nanometre value.</summary>
    public static string FormatMm(long nm)
    {
        if (nm == 0)
        {
            return "0";
        }

        ulong abs = nm < 0 ? (ulong)(-(nm + 1)) + 1 : (ulong)nm;
        ulong whole = abs / 1_000_000;
        ulong fraction = abs % 1_000_000;
        string text = fraction == 0
            ? whole.ToString(CultureInfo.InvariantCulture)
            : whole.ToString(CultureInfo.InvariantCulture) + "." + fraction.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');

        return nm < 0 ? "-" + text : text;
    }

    public static string FormatAngle(double degrees) => SNumber.Format(Math.Round(degrees, 6));

    /// <summary>[0, 360).</summary>
    public static double Normalize360(double degrees)
    {
        degrees %= 360;
        if (degrees < 0)
        {
            degrees += 360;
        }

        return Math.Abs(degrees - 360) < 1e-9 || Math.Abs(degrees) < 1e-9 ? 0 : degrees;
    }

    /// <summary>(-180, 180], as KiCad keeps footprint orientation.</summary>
    public static double Normalize180(double degrees)
    {
        degrees = Normalize360(degrees);
        return degrees > 180 ? degrees - 360 : degrees;
    }
}
