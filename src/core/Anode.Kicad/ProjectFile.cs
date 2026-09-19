using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anode.Kicad;

/// <summary>
/// What a <c>.kicad_pro</c> says that the drawing needs: the project's text variables, and the drawing sheet the
/// schematic and the board are each framed with. Read-only, and forgiving — a project file that will not read is
/// a project without settings, not an error.
/// </summary>
public sealed partial class ProjectFile
{
    public required string Path { get; init; }

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The variables the project defines, as <c>${NAME}</c> in any text.</summary>
    public IReadOnlyDictionary<string, string> TextVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>The schematic's drawing sheet as written in the file; empty or null for KiCad's default.</summary>
    public string? SchematicDrawingSheet { get; init; }

    /// <summary>The board's drawing sheet as written in the file; empty or null for KiCad's default.</summary>
    public string? BoardDrawingSheet { get; init; }

    /// <summary>The project file next to or above <paramref name="file"/>, read; null when there is none.</summary>
    public static ProjectFile? For(string? file)
    {
        for (var folder = file is null ? null : new DirectoryInfo(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file)) ?? string.Empty);
             folder is not null;
             folder = folder.Parent)
        {
            if (folder.Exists && folder.EnumerateFiles("*.kicad_pro").OrderBy(f => f.Name, StringComparer.Ordinal).FirstOrDefault() is { } project)
            {
                return Load(project.FullName);
            }
        }

        return null;
    }

    public static ProjectFile Load(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = json.RootElement;
            return new ProjectFile
            {
                Path = path,
                TextVariables = root.TryGetProperty("text_variables", out var vars) && vars.ValueKind == JsonValueKind.Object
                    ? vars.EnumerateObject().ToDictionary(v => v.Name, v => v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() ?? string.Empty : v.Value.ToString())
                    : new Dictionary<string, string>(),
                SchematicDrawingSheet = Setting(root, "schematic"),
                BoardDrawingSheet = Setting(root, "pcbnew"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ProjectFile { Path = path };
        }

        static string? Setting(JsonElement root, string section) =>
            root.TryGetProperty(section, out var block) && block.ValueKind == JsonValueKind.Object
            && block.TryGetProperty("page_layout_descr_file", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// A path as KiCad writes it in project settings, made absolute: <c>${KIPRJMOD}</c> is the project's folder,
    /// other <c>${NAME}</c>s are project variables or the environment, and a relative path is taken from the project's
    /// folder. Null for an empty one, which means "the default".
    /// </summary>
    public string? Resolve(string? written)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        string expanded = Variable().Replace(written, m => m.Groups[1].Value switch
        {
            "KIPRJMOD" => Folder,
            var name => TextVariables.TryGetValue(name, out var value) ? value : Environment.GetEnvironmentVariable(name) ?? m.Value,
        });

        return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(expanded) ? expanded : System.IO.Path.Combine(Folder, expanded));
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex Variable();
}
