using Anode.Sexpr;

namespace Anode.Kicad.Editing;

/// <summary>
/// The fonts a board or a sheet carries in itself, as KiCad keeps them. <c>(embedded_fonts yes)</c> asks for the
/// fonts its texts use to be carried in the file; on every save KiCad then adds any that is missing — never
/// replacing one already there under the same name — and with <c>no</c> it drops them all. The files sit at the
/// root, in <c>(embedded_files …)</c> right after the flag, in the order of their names.
/// </summary>
public static class EmbeddedFonts
{
    /// <summary>Whether the file asks for its fonts to be embedded; null when it predates the setting.</summary>
    public static bool? Wanted(SList root) => root.Find("embedded_fonts") is { } flag ? flag.AtomAt(1)?.Value == "yes" : null;

    /// <summary>Sets the flag, adding it at the end of the file when the file has none.</summary>
    public static void SetWanted(SList root, bool wanted)
    {
        string value = wanted ? "yes" : "no";
        if (root.Find("embedded_fonts") is { } flag && flag.AtomAt(1) is { } atom)
        {
            atom.SetSymbol(value);
            return;
        }

        var fresh = SchNodes.Adopt(SDocument.Parse($"(embedded_fonts {value})").Root);
        root.Insert(root.Find("embedded_files") is { } files ? root.IndexOf(files) : root.Count, fresh);
    }

    /// <summary>The names of the fonts carried at the root.</summary>
    public static IReadOnlyList<string> Names(SList root) =>
        [.. Files(root).Where(f => f.Find("type")?.AtomAt(1)?.Value == "font").Select(f => f.ChildString("name")).OfType<string>()];

    /// <summary>
    /// Brings the carried fonts in line with the flag, as KiCad does before it writes: with <c>yes</c> each of
    /// <paramref name="fonts"/> not yet carried under its name is added; with <c>no</c> every carried font goes.
    /// A file without the flag is left alone. Answers whether anything changed.
    /// </summary>
    public static bool Sync(SList root, IEnumerable<(string Name, byte[] Data)> fonts)
    {
        switch (Wanted(root))
        {
            case true:
                bool added = false;
                foreach (var (name, data) in fonts.DistinctBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (Files(root).Any(f => f.ChildString("name") == name))
                    {
                        continue;
                    }

                    var (encoded, checksum) = EmbeddedFile.Encode(data);
                    Add(root, name, "font", encoded, checksum);
                    added = true;
                }

                return added;

            case false:
                var fontFiles = Files(root).Where(f => f.Find("type")?.AtomAt(1)?.Value == "font").ToList();
                if (fontFiles.Count == 0)
                {
                    return false;
                }

                var block = root.Find("embedded_files")!;
                foreach (var file in fontFiles)
                {
                    block.Remove(file);
                }

                if (!block.Lists().Any())
                {
                    root.Remove(block);
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Adds one file to the root's <c>(embedded_files …)</c>, creating it after the flag if need be, in name order,
    /// laid out as KiCad writes it: the first line of data beside <c>(data</c>, the rest one tab further in.
    /// </summary>
    public static void Add(SList root, string name, string type, string encoded, string checksum)
    {
        if (root.Find("embedded_files") is not { } block)
        {
            block = SchNodes.Adopt(SDocument.Parse("(embedded_files)").Root);
            root.Insert(root.Find("embedded_fonts") is { } flag ? root.IndexOf(flag) + 1 : root.Count, block);
        }

        var lines = new List<string>();
        for (int first = 0; first < encoded.Length; first += 76)
        {
            int length = Math.Min(76, encoded.Length - first);
            lines.Add((first == 0 ? "|" : string.Empty) + encoded.Substring(first, length) + (first + length == encoded.Length ? "|" : string.Empty));
        }

        var file = SchNodes.Adopt(SDocument.Parse(
            $"(file (name {SEscape.Quote(name)}) (type {type}) (data {string.Join(' ', lines)}) (checksum {SEscape.Quote(checksum)}))").Root);

        // Depths as they will stand: the root's children at one tab, so the file's own at three and its data at four.
        var data = file.Find("data")!;
        for (int i = 2; i < data.Count; i++)
        {
            data[i].LeadingTrivia = "\n\t\t\t\t";
        }

        data.CloseTrivia = "\n\t\t\t";

        var next = block.Lists().FirstOrDefault(f => f.Head == "file" && string.CompareOrdinal(f.ChildString("name"), name) > 0);
        block.Insert(next is null ? block.Count : block.IndexOf(next), file);
    }

    private static IEnumerable<SList> Files(SList root) =>
        root.Find("embedded_files")?.Lists().Where(f => f.Head == "file") ?? [];
}
