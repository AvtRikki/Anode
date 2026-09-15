namespace Ecad.Tests;

/// <summary>Locates KiCad fixtures downloaded by tools/fetch-fixtures.sh.</summary>
internal static class TestData
{
    public const string SkipReason = "No KiCad fixtures; run tools/fetch-fixtures.sh";

    private static readonly Lazy<string> RepoRootLazy = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ecad.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Ecad.slnx) not found.");
    });

    public static string KiCadDir => Path.Combine(RepoRootLazy.Value, "test-data", "kicad");

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

    public static string FullPath(string relative) => Path.Combine(KiCadDir, relative);
}
