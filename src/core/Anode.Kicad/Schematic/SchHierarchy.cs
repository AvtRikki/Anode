namespace Anode.Kicad;

/// <summary>
/// One place a sheet file appears in a design. The same file can appear several times — that is what a reused sheet
/// is — and each appearance is told apart by its path: the root sheet's uuid, then the uuid of every sheet symbol
/// on the way down, which is exactly the key KiCad files the designators under.
/// </summary>
/// <param name="Name">The sheet's name as the sheet symbol above it gives it; the root is named after its file.</param>
/// <param name="Depth">Zero for the root.</param>
public sealed record SheetInstance(string File, string Path, string Name, int Depth);

/// <summary>The appearances of every sheet in a design, walked down from its root.</summary>
public static class SchHierarchy
{
    /// <summary>Deeper than any real design; it only exists so a sheet that places itself cannot recurse forever.</summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// Every sheet instance under <paramref name="rootFile"/>, root first, each parent before its children. Each file
    /// is read once however often it appears. A sheet whose file is missing or unreadable is left out rather than
    /// stopping the walk, as the project tree shows it separately.
    /// </summary>
    /// <param name="open">
    /// Where a sheet comes from, when not straight from disk: an open tab has edits the file does not have yet. Null
    /// from it falls back to reading the file.
    /// </param>
    public static IReadOnlyList<SheetInstance> Walk(string rootFile, Func<string, Schematic?>? open = null)
    {
        var loaded = new Dictionary<string, Schematic?>(StringComparer.Ordinal);
        var found = new List<SheetInstance>();

        string root = Path.GetFullPath(rootFile);
        if (Load(root) is { Uuid: { Length: > 0 } uuid } sheet)
        {
            Visit(root, sheet, "/" + uuid, Path.GetFileNameWithoutExtension(root), 0);
        }

        return found;

        void Visit(string file, Schematic sheet, string path, string name, int depth)
        {
            found.Add(new SheetInstance(file, path, name, depth));
            if (depth >= MaxDepth)
            {
                return;
            }

            foreach (var child in sheet.Sheets)
            {
                if (child.Uuid is not { Length: > 0 } id || child.SheetFile is not { Length: > 0 } relative)
                {
                    continue;
                }

                string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, relative));
                if (Load(target) is { } next)
                {
                    Visit(target, next, path + "/" + id, child.SheetName ?? relative, depth + 1);
                }
            }
        }

        Schematic? Load(string file)
        {
            if (!loaded.TryGetValue(file, out var sheet))
            {
                try
                {
                    sheet = open?.Invoke(file) ?? (File.Exists(file) ? Schematic.Load(file) : null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException)
                {
                    sheet = null;
                }

                loaded[file] = sheet;
            }

            return sheet;
        }
    }
}
