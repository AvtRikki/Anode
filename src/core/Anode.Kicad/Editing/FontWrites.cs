using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// Writes how a text is set — its face, weight and slant — into <c>(effects (font …))</c>, in KiCad's own order:
/// face, size, line spacing, thickness, bold, italic, colour. What is not set is not written, as KiCad leaves out a
/// text that is neither bold nor italic and one in the stroke font.
/// </summary>
public static class FontWrites
{
    /// <summary>The size KiCad gives a text that had none: 1.27 mm, one grid square.</summary>
    private const long DefaultSize = 1_270_000;

    private static readonly string[] Order = ["face", "size", "line_spacing", "thickness", "bold", "italic", "color"];

    /// <summary>The typeface, or null for KiCad's stroke font.</summary>
    public static void SetFace(SList item, string? face)
    {
        var font = Font(item);
        if (string.IsNullOrWhiteSpace(face))
        {
            Remove(font, "face");
            return;
        }

        if (font.Find("face") is { } existing && existing.AtomAt(1) is { } atom)
        {
            atom.SetString(face);
            return;
        }

        Insert(font, SDocument.Parse($"(face {SEscape.Quote(face)})").Root);
    }

    public static void SetBold(SList item, bool bold) => SetFlag(item, "bold", bold);

    public static void SetItalic(SList item, bool italic) => SetFlag(item, "italic", italic);

    private static void SetFlag(SList item, string head, bool on)
    {
        var font = Font(item);
        if (!on)
        {
            Remove(font, head);
            return;
        }

        if (font.Find(head) is { } existing)
        {
            if (existing.AtomAt(1) is { } atom)
            {
                atom.SetSymbol("yes");
            }

            return;
        }

        Insert(font, SDocument.Parse($"({head} yes)").Root);
    }

    /// <summary>The item's font list, made along with its effects when the item has neither.</summary>
    private static SList Font(SList item)
    {
        if (item.Find("effects") is not { } effects)
        {
            effects = SchNodes.Adopt(SDocument.Parse("(effects)").Root);
            item.Insert(item.Find("uuid") is { } uuid ? item.IndexOf(uuid) : item.Count, effects);
        }

        if (effects.Find("font") is { } font)
        {
            return font;
        }

        font = SchNodes.Adopt(SDocument.Parse($"(font (size {KiCadNumber.FormatMm(DefaultSize)} {KiCadNumber.FormatMm(DefaultSize)}))").Root);
        effects.Insert(0 + 1, font);
        return font;
    }

    /// <summary>Puts <paramref name="fresh"/> where KiCad writes it: before the first thing that comes after it.</summary>
    private static void Insert(SList font, SList fresh)
    {
        int rank = Array.IndexOf(Order, fresh.Head);
        var next = font.Lists().FirstOrDefault(l => Array.IndexOf(Order, l.Head) > rank);
        font.Insert(next is null ? font.Count : font.IndexOf(next), SchNodes.Adopt(fresh));
    }

    private static void Remove(SList font, string head)
    {
        if (font.Find(head) is { } gone)
        {
            font.Remove(gone);
        }
    }
}
