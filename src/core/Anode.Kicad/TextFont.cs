using Anode.Sexpr;

namespace Anode.Kicad;

/// <summary>
/// How a text is set, from <c>(effects (font (face "…") (thickness t) (bold yes) (italic yes)))</c> — the same
/// block on a sheet, in a symbol and on a board. No face means KiCad's own stroke font.
/// </summary>
/// <param name="Face">The typeface asked for, or null for the stroke font.</param>
/// <param name="Thickness">The stroke width written in the file, in nanometres; null or ≤ 1 means "work it out".</param>
public readonly record struct TextFont(string? Face, bool Bold, bool Italic, long? Thickness)
{
    public static TextFont Stroke => default;

    /// <summary>Reads the <c>font</c> of an <c>effects</c> list; both <c>(bold yes)</c> and a bare <c>bold</c>.</summary>
    public static TextFont Read(SList? effects)
    {
        if (effects?.Find("font") is not { } font)
        {
            return default;
        }

        string? face = font.ChildString("face");
        return new TextFont(
            string.IsNullOrWhiteSpace(face) ? null : face,
            font.ChildBool("bold") || font.HasSymbol("bold"),
            font.ChildBool("italic") || font.HasSymbol("italic"),
            font.ChildNm("thickness"));
    }

    /// <summary>Every face named anywhere under <paramref name="root"/>, each once, in the order first met.</summary>
    public static IReadOnlyList<string> FacesIn(SList root)
    {
        var faces = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root);
        return faces;

        void Walk(SList list)
        {
            foreach (var child in list.Lists())
            {
                if (child.Head == "face" && child.Str(1) is { Length: > 0 } face && !string.IsNullOrWhiteSpace(face))
                {
                    if (seen.Add(face))
                    {
                        faces.Add(face);
                    }
                }
                else
                {
                    Walk(child);
                }
            }
        }
    }
}
