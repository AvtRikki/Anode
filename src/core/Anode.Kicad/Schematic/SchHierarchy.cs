using System.Text;
using Anode.Sexpr;

namespace Anode.Kicad;

public enum HierarchyProblem { Unreadable, Cycle, DepthLimit, InstanceLimit }

public sealed record HierarchyDiagnostic(string File, HierarchyProblem Problem, string? Detail = null);

/// <summary>
/// One place a sheet file appears in a design. The same file can appear several times — that is what a reused sheet
/// is — and each appearance is told apart by its path: the root sheet's uuid, then the uuid of every sheet symbol
/// on the way down, which is exactly the key KiCad files the designators under.
/// </summary>
/// <param name="Name">The sheet's name as the sheet symbol above it gives it; the root is named after its file.</param>
/// <param name="Depth">Zero for the root.</param>
/// <param name="Trail">The names on the way down, as KiCad prints a sheet's path: "/" for the root, "/amp/" below it.</param>
public sealed record SheetInstance(string File, string Path, string Name, int Depth, string Trail = "/")
{
    /// <summary>The path of the sheet this one hangs under; null for the root.</summary>
    public string? Parent { get; init; }

    /// <summary>
    /// The sheet symbol that placed it, on the parent's sheet: its pins are where this sheet's hierarchical labels
    /// reach the design above. Null for the root, which nothing placed.
    /// </summary>
    public SchSheet? Placement { get; init; }
}

/// <summary>The appearances of every sheet in a design, walked down from its root.</summary>
public static class SchHierarchy
{
    /// <summary>Deeper than any real design; it only exists so a sheet that places itself cannot recurse forever.</summary>
    private const int MaxDepth = 32;
    private const int MaxInstances = 10_000;

    /// <summary>
    /// Paths are compared the way the filesystem compares them, so a sheet placing itself under another spelling of
    /// its own name is still the same file and still a cycle.
    /// </summary>
    private static readonly StringComparer FilePaths =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Every sheet instance under <paramref name="rootFile"/>, root first, each parent before its children. Each file
    /// is read once however often it appears. A sheet whose file is missing or unreadable is left out rather than
    /// stopping the walk, as the project tree shows it separately.
    /// </summary>
    /// <param name="open">
    /// Where a sheet comes from, when not straight from disk: an open tab has edits the file does not have yet. Null
    /// from it falls back to reading the file.
    /// </param>
    public static IReadOnlyList<SheetInstance> Walk(string rootFile, Func<string, Schematic?>? open = null,
        Action<HierarchyDiagnostic>? report = null, CancellationToken cancellationToken = default)
    {
        var loaded = new Dictionary<string, Schematic?>(FilePaths);
        var found = new List<SheetInstance>();
        var ancestors = new HashSet<string>(FilePaths);
        bool exhausted = false;

        string root = Path.GetFullPath(rootFile);
        if (Load(root) is { Uuid: { Length: > 0 } uuid } sheet)
        {
            Visit(root, sheet, "/" + uuid, Path.GetFileNameWithoutExtension(root), 0, "/", null, null);
        }

        return found;

        void Visit(string file, Schematic sheet, string path, string name, int depth, string trail, string? parent, SchSheet? placement)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ancestors.Add(file))
            {
                report?.Invoke(new(file, HierarchyProblem.Cycle));
                return;
            }

            try
            {
                if (found.Count >= MaxInstances)
                {
                    exhausted = true;
                    report?.Invoke(new(file, HierarchyProblem.InstanceLimit));
                    return;
                }

                found.Add(new SheetInstance(file, path, name, depth, trail) { Parent = parent, Placement = placement });
                if (depth >= MaxDepth)
                {
                    if (sheet.Sheets.Count > 0)
                    {
                        report?.Invoke(new(file, HierarchyProblem.DepthLimit));
                    }

                    return;
                }

                foreach (var child in sheet.Sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (exhausted)
                    {
                        break;
                    }

                    if (child.Uuid is not { Length: > 0 } id || child.SheetFile is not { Length: > 0 } relative)
                    {
                        continue;
                    }

                    string target;
                    try
                    {
                        target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, relative));
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                        report?.Invoke(new(relative, HierarchyProblem.Unreadable, ex.Message));
                        continue;
                    }

                    if (Load(target) is { } next)
                    {
                        string childName = child.SheetName ?? relative;
                        Visit(target, next, path + "/" + id, childName, depth + 1, trail + childName + "/", path, child);
                    }
                }
            }
            finally
            {
                ancestors.Remove(file);
            }
        }

        Schematic? Load(string file)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!loaded.TryGetValue(file, out var sheet))
            {
                try
                {
                    sheet = open?.Invoke(file) ?? Schematic.Load(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KiCadFormatException
                    or SexprParseException or DecoderFallbackException)
                {
                    sheet = null;
                    report?.Invoke(new(file, HierarchyProblem.Unreadable, ex.Message));
                }

                loaded[file] = sheet;
            }

            return sheet;
        }
    }
}
