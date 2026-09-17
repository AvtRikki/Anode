using System.Text.Json;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The libraries this application was told to remember, kept where the workbench keeps its other per-user files.
/// They are offered in every project, which is the point: a library of one's own parts should not have to be added
/// to each design separately.
///
/// Losing this list must never stop a sheet from opening, so every read and write answers with something usable.
/// </summary>
internal sealed class SymbolLibraryList(string dataDirectory, string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(dataDirectory, "symbol-libraries.json");

    public IReadOnlyList<string> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Remembers a library, keeping the order they were added in; a repeat changes nothing.</summary>
    public IReadOnlyList<string> Add(string path)
    {
        string full = Path.GetFullPath(path);
        var libraries = Load().ToList();
        if (libraries.Any(l => Same(l, full)))
        {
            return libraries;
        }

        libraries.Add(full);
        Save(libraries);
        return libraries;
    }

    public IReadOnlyList<string> Remove(string path)
    {
        string full = Path.GetFullPath(path);
        var libraries = Load().Where(l => !Same(l, full)).ToList();
        Save(libraries);
        return libraries;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private void Save(IReadOnlyList<string> libraries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(libraries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A remembered library is a convenience; failing to remember must not break placing one now.
        }
    }
}
