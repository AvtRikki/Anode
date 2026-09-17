using System.Text.Json;

namespace Anode.Plugin.Schematic;

/// <summary>
/// Which library sources a project has been told not to show. Kept per project, because the answer is a property of
/// the design rather than of the machine: the same library may be wanted in one project and in the way in another.
///
/// A switched-off source is not a removed one — the row stays in the list with its tick cleared — so this remembers
/// only the nicknames, and forgetting them is harmless.
/// </summary>
internal sealed class DisabledSources(string dataDirectory, string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(dataDirectory, "symbol-sources.json");

    /// <summary>Nicknames this project does not want offered.</summary>
    public IReadOnlyCollection<string> For(string? project)
    {
        if (string.IsNullOrEmpty(project))
        {
            return [];
        }

        return Load().TryGetValue(Key(project), out var disabled) ? disabled : [];
    }

    public void Set(string? project, string nickname, bool enabled)
    {
        if (string.IsNullOrEmpty(project))
        {
            return;
        }

        var all = Load();
        var disabled = all.TryGetValue(Key(project), out var stored) ? stored.ToList() : [];

        if (enabled)
        {
            disabled.RemoveAll(n => string.Equals(n, nickname, StringComparison.Ordinal));
        }
        else if (!disabled.Contains(nickname, StringComparer.Ordinal))
        {
            disabled.Add(nickname);
        }

        if (disabled.Count == 0)
        {
            all.Remove(Key(project));
        }
        else
        {
            all[Key(project)] = disabled;
        }

        Save(all);
    }

    private static string Key(string project) => Path.GetFullPath(project);

    private Dictionary<string, List<string>> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_filePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, List<string>> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering the choice is a convenience; failing to remember must not stop the panel working now.
        }
    }
}
