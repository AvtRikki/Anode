using Anode.Sdk;

// Anode.Kicad.Schematic collides with this plugin's own namespace.
using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic;

/// <summary>
/// What a <c>.kicad_sch</c> holds, for the project tree: the sheets it places, each with the file behind it. The same
/// file may appear more than once under different names — that is what a hierarchy is, not a duplicate — so these
/// nodes are sheet instances rather than files. The tree asks one level at a time, so only the opened sheet is read.
/// </summary>
public sealed class SchematicStructure : IProjectStructure
{
    public IReadOnlyList<string> Extensions => [".kicad_sch"];

    public IReadOnlyList<ProjectNode> Describe(string path)
    {
        var schematic = KicadSchematic.Load(path);
        string? directory = Path.GetDirectoryName(path);
        var nodes = new List<ProjectNode>();

        foreach (var sheet in schematic.Sheets)
        {
            string? file = sheet.SheetFile is { Length: > 0 } name && directory is not null
                ? Path.Combine(directory, name)
                : null;
            bool exists = file is not null && File.Exists(file);

            nodes.Add(new ProjectNode(sheet.SheetName ?? sheet.SheetFile ?? "—", Icons.Sheets)
            {
                Detail = exists ? sheet.SheetFile : Tr.T("sch.sheets.missing"),
                Path = exists ? file : null,
            });
        }

        return nodes;
    }
}
