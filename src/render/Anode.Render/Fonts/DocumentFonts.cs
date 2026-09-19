using Anode.Kicad;

namespace Anode.Render.Fonts;

/// <summary>
/// The faces a document's texts are set in, and what KiCad would carry in the file for them. KiCad looks at the texts
/// a document owns outright — on a board its own texts, not those of its footprints, which carry their own; across a
/// schematic the free texts and labels of every sheet — and carries each face's file when its licence allows.
/// </summary>
public static class DocumentFonts
{
    /// <summary>A face, how it is set, and what will be done with it.</summary>
    /// <param name="File">The file that would be carried; null when the face is only stood in for.</param>
    public sealed record Use(string Face, bool Bold, bool Italic, FontFile? File);

    public static IReadOnlyList<Use> Of(Board board) =>
        Uses(board.Texts.Where(t => t.Footprint is null && t.FontFace is not null).Select(t => (t.FontFace!, t.IsBold, t.IsItalic)));

    public static IReadOnlyList<Use> Of(IEnumerable<Schematic> sheets) =>
        Uses(sheets.SelectMany(s => s.Texts.Select(t => t.Font).Concat(s.Labels.Select(l => l.Font)))
            .Where(f => f.Face is not null)
            .Select(f => (f.Face!, f.Bold, f.Italic)));

    /// <summary>The files KiCad would add on saving: those of <paramref name="uses"/> whose licence lets them travel.</summary>
    public static IReadOnlyList<(string Name, byte[] Data)> Carried(IEnumerable<Use> uses) =>
        [.. uses.Select(u => u.File).OfType<FontFile>().Where(f => f.MayTravel).DistinctBy(f => f.Name, StringComparer.Ordinal).Select(f => (f.Name, f.Data))];

    private static IReadOnlyList<Use> Uses(IEnumerable<(string Face, bool Bold, bool Italic)> faces) =>
        [.. faces
            .Distinct()
            .Select(f => new Use(f.Face, f.Bold, f.Italic, OutlineText.FileOf(f.Face, f.Bold, f.Italic)))];
}
