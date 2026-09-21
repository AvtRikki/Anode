using Avalonia.Controls;
using Anode.Sdk;
using Anode.Workbench.Services;

namespace Anode.Workbench.Tests;

public class ShellServicesTests
{
    private static PanelDescriptor Panel(string id, DockArea area, int order = 0) =>
        new(id, id, area, _ => new Border()) { Order = order, RailLabelKey = id[..2] };

    [Fact]
    public void Each_place_holds_one_section_of_tabs()
    {
        PanelDescriptor[] panels =
        [
            Panel("sheets", DockArea.LeftTop, 0),
            Panel("hierarchy", DockArea.LeftTop, 1),
            Panel("layers", DockArea.LeftBottom, 2),
            Panel("inspector", DockArea.RightTop, 0),
            Panel("net", DockArea.RightTop, 1),
            Panel("rules", DockArea.RightBottom, 2),
            Panel("drc", DockArea.Bottom, 0),
            Panel("console", DockArea.Bottom, 1),
        ];

        var layout = DockPlanner.Plan(panels);

        Assert.Equal(["LeftTop", "LeftBottom"], layout.Left.Select(s => s.Key));
        Assert.Equal(["sheets", "hierarchy"], layout.Left[0].Panels.Select(p => p.Id));
        Assert.Equal(["layers"], layout.Left[1].Panels.Select(p => p.Id));
        Assert.Equal(["RightTop", "RightBottom"], layout.Right.Select(s => s.Key));
        Assert.Equal(["inspector", "net"], layout.Right[0].Panels.Select(p => p.Id));
        Assert.Equal(["drc", "console"], layout.Bottom!.Panels.Select(p => p.Id));
        Assert.Empty(layout.Rail);
    }

    [Fact]
    public void A_place_nobody_asked_for_has_no_section()
    {
        var layout = DockPlanner.Plan([Panel("project", DockArea.LeftTop), Panel("console", DockArea.Bottom)]);

        Assert.Equal(["LeftTop"], layout.Left.Select(s => s.Key));
        Assert.Empty(layout.Right);
        Assert.NotNull(layout.Bottom);
    }

    [Fact]
    public void Panels_sent_to_the_rail_stay_there()
    {
        var layout = DockPlanner.Plan([Panel("project", DockArea.LeftTop), Panel("console", DockArea.Bottom)], new HashSet<string> { "console" });

        Assert.Null(layout.Bottom);
        Assert.Equal(["console"], layout.Rail.Select(p => p.Id));
    }

    private static CommandDescriptor Command(string id, string title, string? scope = null) =>
        new(id, title) { ScopeKey = scope, Execute = () => { } };

    [Fact]
    public void Palette_ranks_title_start_above_word_start_and_inner_matches()
    {
        CommandDescriptor[] commands =
        [
            Command("auto", "Автопрокладка выделенных цепей", "плата"),
            Command("settings", "Настройки прокладки — класс HV_170V", "правила"),
            Command("route", "Прокладывать дорожку", "плата"),
            Command("pair", "Прокладывать дифференциальную пару", "плата"),
            Command("save", "Сохранить", "файл"),
        ];

        var matches = CommandMatcher.Match(commands, "прокл");

        Assert.Equal(["route", "pair", "settings", "auto"], matches.Select(m => m.Command.Id));
        Assert.Equal((0, 5), matches[0].Ranges[0]);
        Assert.Equal((4, 5), matches[3].Ranges[0]);
    }

    [Fact]
    public void Palette_requires_every_word_and_matches_scope()
    {
        CommandDescriptor[] commands = [Command("rotate", "Повернуть", "плата"), Command("route", "Прокладывать дорожку", "плата")];

        Assert.Equal(["route"], CommandMatcher.Match(commands, "прокл дор").Select(m => m.Command.Id));
        Assert.Equal(2, CommandMatcher.Match(commands, "плата").Count);
        Assert.Empty(CommandMatcher.Match(commands, "схема"));
        Assert.Equal(2, CommandMatcher.Match(commands, "").Count);
    }

    [Fact]
    public void Command_registry_replaces_by_id_and_unregisters()
    {
        var registry = new CommandRegistry();
        int runs = 0;
        var first = registry.Register(new CommandDescriptor("undo", "Отменить") { Execute = () => runs += 1 });
        var second = registry.Register(new CommandDescriptor("undo", "Отменить") { Execute = () => runs += 10 });

        Assert.True(registry.TryExecute("undo"));
        Assert.Equal(10, runs);

        second.Dispose();
        Assert.False(registry.TryExecute("undo"));
        first.Dispose();
    }

    [Fact]
    public void Document_registry_picks_type_by_extension()
    {
        var registry = new DocumentRegistry();
        var type = new FakeDocumentType();
        registry.Register(type);

        Assert.Same(type, registry.FindFor("/boards/Nixie.KICAD_PCB"));
        Assert.Null(registry.FindFor("/boards/Power.kicad_sch"));
    }

    [Fact]
    public async Task Recent_projects_keep_most_recent_first_without_duplicates()
    {
        string file = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.json");
        try
        {
            var store = new RecentProjectsStore(file);
            var now = new DateTime(2026, 9, 15, 12, 0, 0);
            store.Touch("/p/a.kicad_pcb", now);
            store.Touch("/p/b.kicad_pcb", now.AddMinutes(1));
            var list = store.Touch("/p/a.kicad_pcb", now.AddMinutes(2));

            Assert.Equal(["a", "b"], list.Select(p => p.Name));
            Assert.Equal(list, store.Load());
            // Registering a catalog tells every live view to rebuild, which is the headless session's business:
            // done from a thread of its own it reaches views whose application is not there to be read.
            await ShellWindowTests.Dispatch(_ =>
            {
                using (Tr.Register(JsonTextCatalog.FromAssembly(typeof(App).Assembly)))
                {
                    Assert.Equal("12 min ago", RecentProjectsStore.Ago(now, now.AddMinutes(12)));
                    Assert.Equal("yesterday", RecentProjectsStore.Ago(now, now.AddHours(30)));
                }
            });
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Manifest_parses_and_rejects_other_contract_versions()
    {
        var manifest = PluginManifest.Parse("""{ "id": "anode.pcb", "name": "PCB", "version": "0.1", "assembly": "Pcb.dll", "entryType": "Pcb.Plugin", "contractVersion": 1 }""");
        Assert.Equal("anode.pcb", manifest.Id);

        string dir = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(dir, "plugin.json");
        File.WriteAllText(path, """{ "id": "x", "name": "X", "version": "1", "assembly": "X.dll", "entryType": "X.P", "contractVersion": 99 }""");

        var ex = Assert.Throws<InvalidOperationException>(() => new PluginLoader(new LogService()).Load(path));
        Assert.Contains("contract v99", ex.Message);
    }

    private sealed class FakeDocumentType : IDocumentType
    {
        public string Id => "fake";

        public string Label => "плата";

        public IReadOnlyList<string> Extensions => [".kicad_pcb"];

        public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
