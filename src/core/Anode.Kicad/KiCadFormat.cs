namespace Anode.Kicad;

/// <summary>Board file format versions (the <c>(version YYYYMMDD)</c> header) relevant to this reader.</summary>
public static class KiCadFormat
{
    public const int KiCad8 = 20240108;
    public const int KiCad9 = 20241229;
    public const int KiCad10 = 20260206;

    /// <summary>Items reference nets by name; no top-level (net code "name") table.</summary>
    public const int NetNamesOnly = 20251028;

    /// <summary>Footprints use (transform (translate)(rotate)(scale)) and children are stored in library frame.</summary>
    public const int FootprintAffineTransform = 20260616;

    /// <summary>Bold text stores the base pen width and multiplies it when drawing.</summary>
    public const int BoldIsStrokeMultiplier = 20260826;

    /// <summary>Pen width multiplier for bold stroke text (KiCad BOLD_STROKE_MULTIPLIER).</summary>
    public const double BoldStrokeMultiplier = 1.6;

    /// <summary>Newest version this code was checked against.</summary>
    public const int NewestKnown = 20260901;

    public const int OldestSupported = KiCad8;
}
