using System.Reflection;
using Anode.Sdk;

namespace Anode.Plugin.Schematic;

/// <summary>
/// The schematic domain as a plugin: its own translations, a document type for <c>.kicad_sch</c> and the sheets
/// panel. Sheet commands belong to the open document and are registered when it becomes active.
/// </summary>
public sealed class SchematicPlugin : IPlugin
{
    public void Activate(IPluginContext context)
    {
        Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));

        context.Documents.Register(new SchematicDocumentType(context.Log));

        context.Panels.Register(new PanelDescriptor("sch.sheets", "sch.panel.sheets", DockSide.Left, workbench => new SheetsPanel(workbench))
        {
            IconKey = Icons.Sheets,
            RailLabelKey = "sch.panel.sheets.rail",
            Group = "project",
            Order = 5,
            DocumentTypes = [SchematicDocumentType.TypeId],
        });

        context.Log.Info(Tr.T("sch.log.activated", context.Manifest.Name, context.Manifest.Version));
    }
}
