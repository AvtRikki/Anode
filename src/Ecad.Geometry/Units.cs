namespace Ecad.Geometry;

public static class Units
{
    public const long NmPerMm = 1_000_000;

    public static long MmToNm(double mm) => (long)Math.Round(mm * NmPerMm, MidpointRounding.AwayFromZero);

    public static double NmToMm(long nm) => (double)nm / NmPerMm;
}
