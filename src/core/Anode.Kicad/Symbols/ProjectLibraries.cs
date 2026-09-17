namespace Anode.Kicad;

/// <summary>
/// Finding the libraries a sheet can draw from. KiCad keeps a table beside the project and another with the
/// installation; only the first is ever certain, since the machine reading a design may have no KiCad on it.
///
/// So the project's own table is read when it exists, and any <c>.kicad_sym</c> lying in the project folder is
/// offered besides — a library sitting next to the design is plainly meant to be used, table or no table.
/// </summary>
public static class ProjectLibraries
{
    /// <summary>The index for the project a sheet belongs to; empty when it can draw from nothing.</summary>
    public static SymbolIndex For(string? sheetPath)
    {
        string? project = ProjectFolder(sheetPath);
        if (project is null)
        {
            return SymbolIndex.Build(null, Global(), Variables(), null);
        }

        var variables = Variables();
        variables["KIPRJMOD"] = project;

        var table = Table(Path.Combine(project, "sym-lib-table"));
        return SymbolIndex.Build(WithLooseLibraries(project, table, variables), Global(), Variables(), project);
    }

    /// <summary>The folder holding the project file, walking up from the sheet; the sheet's own folder otherwise.</summary>
    public static string? ProjectFolder(string? sheetPath)
    {
        if (string.IsNullOrEmpty(sheetPath))
        {
            return null;
        }

        var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(sheetPath)) ?? string.Empty);
        for (var at = folder; at is not null; at = at.Parent)
        {
            if (at.Exists && at.EnumerateFiles("*.kicad_pro").Any())
            {
                return at.FullName;
            }
        }

        return folder.Exists ? folder.FullName : null;
    }

    private static SymLibTable? Table(string path)
    {
        try
        {
            return File.Exists(path) ? SymLibTable.Load(path) : null;
        }
        catch (Exception ex) when (ex is IOException or KiCadFormatException or UnauthorizedAccessException)
        {
            // A table that will not read must not stop the libraries that would have.
            return null;
        }
    }

    /// <summary>
    /// The project's table, plus a row for every <c>.kicad_sym</c> in the folder it does not already claim.
    /// Nicknamed by file, which is how KiCad nicknames one by default.
    ///
    /// What counts as already claimed is the <em>file</em>, not the nickname: a table naming parts.kicad_sym "Own"
    /// has claimed it, and offering it again as "parts" would show every symbol in it twice.
    /// </summary>
    private static SymLibTable? WithLooseLibraries(string project, SymLibTable? table, IReadOnlyDictionary<string, string> variables)
    {
        var files = Directory.EnumerateFiles(project, "*.kicad_sym").Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            return table;
        }

        var rows = new List<string>();
        var claimed = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var entry in table?.Entries ?? [])
        {
            rows.Add($"\t(lib (name \"{entry.Name}\")(type \"{entry.Type}\")(uri \"{entry.Uri}\")(options \"{entry.Options}\")(descr \"{entry.Description}\"))");
            if (FullPath(entry.Resolve(variables), project) is { } already)
            {
                claimed.Add(already);
            }
        }

        foreach (string file in files)
        {
            string nickname = Path.GetFileNameWithoutExtension(file);
            if (table?.Find(nickname) is null && FullPath(file, project) is { } path && !claimed.Contains(path))
            {
                rows.Add($"\t(lib (name \"{nickname}\")(type \"KiCad\")(uri \"${{KIPRJMOD}}/{Path.GetFileName(file)}\")(options \"\")(descr \"\"))");
            }
        }

        return SymLibTable.Parse("(sym_lib_table\n\t(version 7)\n" + string.Join("\n", rows) + "\n)");
    }

    /// <summary>An absolute path, or null when the uri is not one this reader can compare.</summary>
    private static string? FullPath(string uri, string project)
    {
        if (uri.Length == 0 || uri.Contains("${", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(uri) ? uri : Path.Combine(project, uri));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>The installation's table, if this machine has one where KiCad puts it.</summary>
    private static SymLibTable? Global()
    {
        foreach (string path in GlobalPaths())
        {
            if (Table(path) is { } table)
            {
                return table;
            }
        }

        return null;
    }

    private static IEnumerable<string> GlobalPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Preferences", "kicad")
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kicad")
                : Path.Combine(home, ".config", "kicad");

        if (!Directory.Exists(root))
        {
            yield break;
        }

        // KiCad keeps one folder per version; the newest is the one to believe.
        foreach (string version in Directory.EnumerateDirectories(root).OrderDescending(StringComparer.Ordinal))
        {
            yield return Path.Combine(version, "sym-lib-table");
        }

        yield return Path.Combine(root, "sym-lib-table");
    }

    /// <summary>The variables a uri may name, taken from the environment where KiCad would have set them.</summary>
    private static Dictionary<string, string> Variables()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && name.Contains("KICAD", StringComparison.OrdinalIgnoreCase) && entry.Value is string value)
            {
                variables[name] = value;
            }
        }

        return variables;
    }
}
