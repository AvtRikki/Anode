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
    /// <summary>
    /// The index for the project a sheet belongs to. Libraries come from three places, in the order they are
    /// searched: the project's own table (and anything lying beside it), then <paramref name="alsoOffer"/> — the
    /// libraries this application was told to remember — then whatever KiCad itself has installed.
    /// </summary>
    /// <param name="alsoOffer">Paths of .kicad_sym files to offer besides; unreadable ones are simply not listed.</param>
    public static SymbolIndex For(string? sheetPath, IEnumerable<string>? alsoOffer = null)
    {
        string? project = ProjectFolder(sheetPath);
        var installed = Rows(Installed().Concat(alsoOffer ?? []));

        if (project is null)
        {
            return SymbolIndex.Build(null, Global() ?? installed, Variables(), null);
        }

        var variables = Variables();
        variables["KIPRJMOD"] = project;

        var table = Table(Path.Combine(project, "sym-lib-table"));
        return SymbolIndex.Build(
            WithLooseLibraries(project, table, variables),
            Merge(Global(), installed),
            Variables(),
            project);
    }

    /// <summary>
    /// Adds a library to the project's own table, writing the file when there is none yet. Returns the nickname it
    /// was given, or null when the project already names that very file.
    ///
    /// The table is KiCad's own file and is written through the lossless tree, so the rows already in it come back
    /// exactly as they were.
    /// </summary>
    public static string? AddToProjectTable(string projectFolder, string libraryPath)
    {
        string path = Path.Combine(projectFolder, "sym-lib-table");
        var table = Table(path);
        string full = Path.GetFullPath(libraryPath);

        var variables = Variables();
        variables["KIPRJMOD"] = projectFolder;

        foreach (var entry in table?.Entries ?? [])
        {
            if (FullPath(entry.Resolve(variables), projectFolder) is { } already
                && already.Equals(full, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        string nickname = Nickname(Path.GetFileNameWithoutExtension(libraryPath), table);

        // A library inside the project folder is named relative to it, so the project can be moved whole.
        string uri = full.StartsWith(Path.GetFullPath(projectFolder) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "${KIPRJMOD}/" + Path.GetRelativePath(projectFolder, full).Replace(Path.DirectorySeparatorChar, '/')
            : full;

        string row = $"\t(lib (name \"{nickname}\")(type \"KiCad\")(uri \"{uri}\")(options \"\")(descr \"\"))";
        string text = table is null
            ? "(sym_lib_table\n\t(version 7)\n" + row + "\n)\n"
            : Insert(File.ReadAllText(path), row);

        File.WriteAllText(path, text);
        return nickname;
    }

    /// <summary>A name no row of the table already uses.</summary>
    private static string Nickname(string wanted, SymLibTable? table)
    {
        if (table?.Find(wanted) is null)
        {
            return wanted;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{wanted}-{i}";
            if (table.Find(candidate) is null)
            {
                return candidate;
            }
        }
    }

    /// <summary>Puts a row before the closing bracket, leaving every line already written untouched.</summary>
    private static string Insert(string table, string row)
    {
        int close = table.LastIndexOf(')');
        return close < 0 ? table : table[..close] + row + "\n" + table[close..];
    }

    /// <summary>A table naming each of these files, nicknamed by file.</summary>
    private static SymLibTable? Rows(IEnumerable<string> files)
    {
        var rows = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        foreach (string file in files.Where(File.Exists))
        {
            string nickname = Path.GetFileNameWithoutExtension(file);
            if (seen.Add(nickname))
            {
                rows.Add($"\t(lib (name \"{nickname}\")(type \"KiCad\")(uri \"{file}\")(options \"\")(descr \"\"))");
            }
        }

        return rows.Count == 0 ? null : SymLibTable.Parse("(sym_lib_table\n\t(version 7)\n" + string.Join("\n", rows) + "\n)");
    }

    /// <summary>Both tables as one, the first keeping its nicknames where they collide.</summary>
    private static SymLibTable? Merge(SymLibTable? first, SymLibTable? second)
    {
        if (first is null || second is null)
        {
            return first ?? second;
        }

        var rows = new List<string>();
        foreach (var entry in first.Entries.Concat(second.Entries))
        {
            rows.Add($"\t(lib (name \"{entry.Name}\")(type \"{entry.Type}\")(uri \"{entry.Uri}\")(options \"{entry.Options}\")(descr \"{entry.Description}\"))");
        }

        return SymLibTable.Parse("(sym_lib_table\n\t(version 7)\n" + string.Join("\n", rows) + "\n)");
    }

    /// <summary>Symbol libraries of a KiCad installed where KiCad installs itself.</summary>
    public static IEnumerable<string> Installed()
    {
        foreach (string folder in InstalledFolders().Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.kicad_sym").Order(StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> InstalledFolders()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/KiCad/KiCad.app/Contents/SharedSupport/symbols";
            yield return "/Library/Application Support/kicad/symbols";
        }
        else if (OperatingSystem.IsWindows())
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(Path.Combine(programs, "KiCad")))
            {
                foreach (string version in Directory.EnumerateDirectories(Path.Combine(programs, "KiCad")).OrderDescending(StringComparer.Ordinal))
                {
                    yield return Path.Combine(version, "share", "kicad", "symbols");
                }
            }
        }
        else
        {
            yield return "/usr/share/kicad/symbols";
            yield return "/usr/local/share/kicad/symbols";
        }
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
