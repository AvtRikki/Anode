namespace Anode.Render.Fonts;

/// <summary>Where and how large a text is set, whichever font draws it.</summary>
/// <param name="Width">Glyph width in board units (KiCad text size X).</param>
/// <param name="Height">Glyph height in board units (KiCad text size Y).</param>
/// <param name="PenWidth">Stroke width in board units: every stroke of the stroke font, only the overbars of an outline face.</param>
/// <param name="AngleDegrees">Counter-clockwise on screen, already adjusted for keep-upright.</param>
public readonly record struct TextStyle(
    double Width,
    double Height,
    double PenWidth,
    TextHAlign HAlign = TextHAlign.Center,
    TextVAlign VAlign = TextVAlign.Center,
    double AngleDegrees = 0,
    bool Mirrored = false,
    bool Italic = false,
    double LineSpacing = 1);
