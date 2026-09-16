using System.Reflection;
using Anode.Sdk;

namespace Anode.Plugin.Pcb;

/// <summary>
/// The PCB domain as a plugin: its own translations, a document type for <c>.kicad_pcb</c> and the layers panel.
/// Board commands belong to the open document and are registered when it becomes active.
/// </summary>
public sealed class PcbPlugin : IPlugin
{
    public void Activate(IPluginContext context)
    {
        Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));

        context.Documents.Register(new PcbDocumentType(context.Log));

        context.Panels.Register(new PanelDescriptor("pcb.layers", "pcb.panel.layers", DockSide.Left, workbench => new LayersPanel(workbench))
        {
            IconKey = Icons.Layers,
            RailLabelKey = "pcb.panel.layers.rail",
            Group = "layers",
            Order = 10,
            DocumentTypes = [PcbDocumentType.TypeId],
        });

        context.Log.Info(Tr.T("pcb.log.activated", context.Manifest.Name, context.Manifest.Version));
    }
}
