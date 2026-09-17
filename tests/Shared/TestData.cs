namespace Anode.Tests;

/// <summary>Locates KiCad fixtures downloaded by tools/fetch-fixtures.sh.</summary>
internal static class TestData
{
    public const string SkipReason = "No KiCad fixtures; run tools/fetch-fixtures.sh";

    private static readonly Lazy<string> RepoRootLazy = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Anode.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Anode.slnx) not found.");
    });

    public static string RepoRoot => RepoRootLazy.Value;

    public static string KiCadDir => Path.Combine(RepoRootLazy.Value, "test-data", "kicad");

    /// <summary>A board to open in end-to-end tests: a real demo if it was fetched, otherwise any fixture.</summary>
    public static string? AnyBoard()
    {
        string preferred = Path.Combine(KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_pcb");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Directory.Exists(KiCadDir)
            ? Directory.EnumerateFiles(KiCadDir, "*.kicad_pcb", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    /// <summary>Relative paths of fixtures with the given extensions; a single empty entry if none exist.</summary>
    public static TheoryData<string> Files(params string[] extensions)
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(KiCadDir))
        {
            foreach (var file in Directory.EnumerateFiles(KiCadDir, "*", SearchOption.AllDirectories)
                         .Where(f => extensions.Contains(Path.GetExtension(f)))
                         .Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetRelativePath(KiCadDir, file));
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    /// <summary>A schematic to open in end-to-end tests, preferring a demo with a hierarchy.</summary>
    public static string? AnySchematic()
    {
        string preferred = Path.Combine(KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Directory.Exists(KiCadDir)
            ? Directory.EnumerateFiles(KiCadDir, "*.kicad_sch", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    /// <summary>A symbol library to read, preferring the one the demos ship.</summary>
    public static string? AnySymbolLibrary()
    {
        string preferred = Path.Combine(KiCadDir, "demos", "cm5_minima", "CM5IO.kicad_sym");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Directory.Exists(KiCadDir)
            ? Directory.EnumerateFiles(KiCadDir, "*.kicad_sym", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    public static string FullPath(string relative) => Path.Combine(KiCadDir, relative);
}
