namespace Anode.Sdk;

/// <summary>
/// A node of the project tree. The workbench knows folders and file names; what is <em>inside</em> a file — the
/// sheets of a schematic, the boards of a project — is known only by the plugin that owns that format, so it hands
/// the tree these nodes instead of the shell learning every domain.
/// </summary>
/// <param name="Title">What the node is called: a sheet instance name, a file name, a library name.</param>
/// <param name="IconKey">Name from <see cref="Icons"/>.</param>
public sealed record ProjectNode(string Title, string IconKey)
{
    /// <summary>Quiet second line in mono: the file behind the node, a count, a note.</summary>
    public string? Detail { get; init; }

    /// <summary>File this node opens, when it opens one.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Which appearance of <see cref="Path"/> this node stands for, when a file can appear more than once — a sheet
    /// placed twice in a hierarchy is one file and two places. Opaque to the shell: it is handed back to the plugin
    /// when the node's children are described and to the document when the node is opened.
    /// </summary>
    public string? Instance { get; init; }

    /// <summary>Children known without reading another file; deeper levels are described on demand.</summary>
    public IReadOnlyList<ProjectNode> Children { get; init; } = [];
}

/// <summary>Describes files of one domain for the project tree.</summary>
public interface IProjectStructure
{
    /// <summary>Lower-case extensions including the dot, e.g. ".kicad_sch".</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>What the file holds, one level deep. Called on the UI thread, so it must stay cheap.</summary>
    IReadOnlyList<ProjectNode> Describe(string path);

    /// <summary>
    /// What one appearance of the file holds: the <see cref="ProjectNode.Instance"/> of the node being opened, null
    /// for a file listed on its own. Formats without appearances need not implement it.
    /// </summary>
    IReadOnlyList<ProjectNode> Describe(string path, string? instance) => Describe(path);
}

public interface IProjectStructureRegistry
{
    IReadOnlyList<IProjectStructure> Contributors { get; }

    event Action? Changed;

    IDisposable Register(IProjectStructure contributor);

    /// <summary>Children of a file, from whoever understands it; empty when nobody does or the file will not read.</summary>
    IReadOnlyList<ProjectNode> Describe(string path);

    /// <summary>Children of one appearance of a file; see <see cref="IProjectStructure.Describe(string, string?)"/>.</summary>
    IReadOnlyList<ProjectNode> Describe(string path, string? instance) => Describe(path);
}
