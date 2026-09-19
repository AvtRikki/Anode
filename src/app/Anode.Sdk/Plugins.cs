using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anode.Sdk;

/// <summary>Version of the plugin contracts in this assembly. Plugins declare the version they were built against.</summary>
public static class PlatformContract
{
    public const int Version = 11;
}

/// <summary>Contents of a plugin's <c>plugin.json</c>.</summary>
/// <param name="Id">Stable identifier, e.g. <c>anode.pcb</c>.</param>
/// <param name="Assembly">Entry assembly file name next to the manifest.</param>
/// <param name="EntryType">Full name of the <see cref="IPlugin"/> implementation.</param>
/// <param name="ContractVersion">Value of <see cref="PlatformContract.Version"/> the plugin was built against.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string EntryType,
    int ContractVersion,
    string? Description = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PluginManifest Parse(string json) =>
        JsonSerializer.Deserialize<PluginManifest>(json, Options) ?? throw new FormatException("Empty plugin manifest.");
}

/// <summary>Entry point of a plugin. Activated once, on the UI thread, after the workbench services exist.</summary>
public interface IPlugin
{
    void Activate(IPluginContext context);
}

/// <summary>What a plugin gets to extend the workbench.</summary>
public interface IPluginContext
{
    PluginManifest Manifest { get; }

    IWorkbench Workbench { get; }

    ICommandRegistry Commands { get; }

    IPanelRegistry Panels { get; }

    IDocumentRegistry Documents { get; }

    /// <summary>Where a plugin describes its files for the project tree.</summary>
    IProjectStructureRegistry Project { get; }

    /// <summary>
    /// Where this application keeps its per-user files. A plugin that remembers something across sessions — a list
    /// of libraries, a last choice — puts it here rather than inventing a place of its own.
    /// </summary>
    string DataDirectory { get; }

    ILog Log { get; }
}

public interface ILog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
