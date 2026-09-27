using System.Reflection;
using Anode.Kicad;
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

        // A symbol library is a document of its own: the file is what is saved, one of its symbols is on the canvas.
        context.Documents.Register(new SymbolLibraryDocumentType());
        context.Commands.Register(new CommandDescriptor("sch.newLibrary", "sch.command.newLibrary")
        {
            MenuKey = "menu.file", MenuOrder = 6,
            Execute = () => _ = NewLibraryAsync(context),
        });
        context.Panels.Register(new PanelDescriptor(SymbolListPanel.PanelId, "sch.panel.library", DockArea.LeftBottom, workbench => new SymbolListPanel(workbench))
        {
            IconKey = Icons.Component,
            RailLabelKey = "sch.panel.libraryRail",
            DocumentTypes = [SymbolLibraryDocumentType.TypeId],
            Order = 10,
        });

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

        context.Log.Info(Tr.English("sch.log.activated", context.Manifest.Name, context.Manifest.Version));
    }

    /// <summary>
    /// A new, empty symbol library where the person asks for one, written as KiCad 9 writes one and opened. A file
    /// that is already there is never written over: that would throw away a library.
    /// </summary>
    internal static async Task NewLibraryAsync(IPluginContext context)
    {
        if (await context.Workbench.AskWhereToWriteAsync("symbols.kicad_sym", ".kicad_sym", "sch.command.newLibrary") is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            path = Path.ChangeExtension(Path.GetFullPath(path), ".kicad_sym");
            _ = SymbolLibrary.Parse(SymbolLibrary.EmptyText);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(SymbolLibrary.EmptyText);
            }

            await context.Workbench.OpenAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            context.Log.Error(ex.Message, ex);
            context.Workbench.ShowBanner(new Banner(Tr.T("sch.lib.createFailed", ex.Message)));
        }
    }
}
