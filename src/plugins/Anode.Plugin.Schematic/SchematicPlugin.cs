using System.Reflection;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The schematic domain as a plugin: its own translations, a document type for <c>.kicad_sch</c> and the hierarchy
/// of sheets it describes for the project tree. Sheet commands belong to the open document and are registered when
/// it becomes active.
/// </summary>
public sealed class SchematicPlugin : IPlugin
{
    public void Activate(IPluginContext context)
    {
        Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));

        context.Documents.Register(new SchematicDocumentType(context.Log));

        context.Project.Register(new SchematicStructure());

        // The parts a sheet can draw from. Same place as the inspector, so the two are tabs of one stack and each
        // gets the whole right side when it is the one showing — rather than halving it between them.
        var remembered = new SymbolLibraryList(context.DataDirectory);
        context.Panels.Register(new PanelDescriptor(
            "sch.symbols",
            "sch.panel.symbols",
            DockArea.RightTop,
            workbench => new SymbolsPanel(workbench, remembered))
        {
            IconKey = Icons.Component,
            RailLabelKey = "sch.panel.symbolsRail",
            DocumentTypes = [SchematicDocumentType.TypeId],
            Order = 10,
        });

        context.Log.Info(Tr.T("sch.log.activated", context.Manifest.Name, context.Manifest.Version));
    }
}
