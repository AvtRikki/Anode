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

        // Libraries the user added by hand. Both the panel and the open sheet read them: the panel to offer parts,
        // the sheet to take a definition from its library again.
        var remembered = new SymbolLibraryList(context.DataDirectory);

        context.Documents.Register(new SchematicDocumentType(context.Log, remembered));

        context.Project.Register(new SchematicStructure());

        // The parts a sheet can draw from. Same place as the inspector, so the two are tabs of one stack and each
        // gets the whole right side when it is the one showing — rather than halving it between them.
        var disabled = new DisabledSources(context.DataDirectory);
        context.Panels.Register(new PanelDescriptor(
            "sch.symbols",
            "sch.panel.symbols",
            DockArea.RightTop,
            workbench => new SymbolsPanel(workbench, remembered, disabled))
        {
            IconKey = Icons.Component,
            RailLabelKey = "sch.panel.symbolsRail",
            DocumentTypes = [SchematicDocumentType.TypeId],
            Order = 10,
        });

        // The nets of the open sheet, where the board's layers sit for a board: a list to follow a net from.
        context.Panels.Register(new PanelDescriptor("sch.nets", "sch.panel.nets", DockArea.LeftBottom, workbench => new NetsPanel(workbench))
        {
            RailLabelKey = "sch.panel.netsRail",
            DocumentTypes = [SchematicDocumentType.TypeId],
            Order = 20,
        });

        // Find and Replace at the foot of the window, beside the checks: a list of places, each with where it is.
        context.Panels.Register(new PanelDescriptor(FindSession.PanelId, "sch.panel.find", DockArea.Bottom, workbench => new FindPanel(workbench))
        {
            IconKey = Icons.Search,
            RailLabelKey = "sch.panel.findRail",
            DocumentTypes = [SchematicDocumentType.TypeId],
            Order = 20,
        });

        // Every part of the sheet with every field it carries: KiCad's Symbol Fields Table, beside Find.
        context.Panels.Register(new PanelDescriptor(SchematicDocument.FieldsPanelId, "sch.panel.fields", DockArea.Bottom, workbench => new FieldsPanel(workbench))
        {
            IconKey = Icons.Component,
            RailLabelKey = "sch.panel.fieldsRail",
            DocumentTypes = [SchematicDocumentType.TypeId],
            Order = 30,
        });

        context.Log.Info(Tr.English("sch.log.activated", context.Manifest.Name, context.Manifest.Version));
    }
}
