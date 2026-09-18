using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Where the parts come from, and turning a source off. A project may reach more libraries than it wants to see at
/// once, so the panel lists them and lets one be switched off — and switching it off must actually remove its parts
/// from the list, not merely tick a box.
/// </summary>
public class SymbolSourcesTests
{
    private static string Library(string symbol) => $"""
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "anode")
        	(symbol "{symbol}"
        		(property "Reference" "U"
        			(at 0 0 0)
        		)
        		(property "Description" "{symbol} for testing"
        			(at 0 0 0)
        		)
        	)
        )
        """;

    private const string Sheet = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    [Fact]
    public Task A_source_can_be_switched_off_and_its_parts_go_with_it() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-sources-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "project.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(folder, "Device.kicad_sym"), Library("R"));
            File.WriteAllText(Path.Combine(folder, "Conn.kicad_sym"), Library("DB9"));
            string sheet = Path.Combine(folder, "project.kicad_sch");
            File.WriteAllText(sheet, Sheet);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            Assert.NotNull(Pump(shell.OpenAsync(sheet)));
            Dispatcher.UIThread.RunJobs();

            var right = Assert.Single(shell.RightStacks);
            right.Select(right.Tabs.Single(t => t.Descriptor.Id == "sch.symbols"));
            Dispatcher.UIThread.RunJobs();

            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            var panel = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "SymbolsPanel");
            Assert.True(panel is not null, "the components panel was not shown");

            // Both libraries lie beside the project, so both are found without any table.
            Assert.Equal(["DB9", "R"], PartNames(panel!));

            // The button says how many of the libraries found are being offered.
            var sources = panel!.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Content is string text && text.Contains("libraries", StringComparison.Ordinal));
            Assert.True(sources is not null, $"no sources button; buttons said: {Labels(panel)}");
            Assert.Equal("2 of 2 libraries", sources!.Content);

            // Open the list of sources and photograph it.
            sources.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // A flyout may render in a layer of its own rather than under the window; the message below says so if
            // that is what happened, instead of failing blankly.
            var items = window.GetVisualDescendants().OfType<MenuItem>().ToList();

            Assert.True(items.Count > 0, "the sources list showed no libraries in the window's visual tree");

            // Every library is listed with where it came from, and a tick against the ones being offered.
            Assert.All(items, item => Assert.Contains("in the project", (string)item.Header!, StringComparison.Ordinal));
            Assert.Equal(2, items.Count(i => i.Icon is not null));

            // Turn the connector library off: its part must leave the list.
            var conn = items.Single(i => ((string)i.Header!).Contains("Conn", StringComparison.Ordinal));
            conn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["R"], PartNames(panel));
            Assert.Equal("1 of 2 libraries", sources.Content);

            // The list again, now that one row has lost its tick. The tick lives in the menu's own icon column, so
            // a library that is off keeps its name in line with one that is on.
            sources.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Reopening leaves the previous list's items in the tree, so this asks a question that holds either
            // way: the connector library is now offered without a tick.
            var after = window.GetVisualDescendants().OfType<MenuItem>().ToList();
            Assert.Contains(after, i => ((string)i.Header!).Contains("Conn", StringComparison.Ordinal) && i.Icon is null);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>The part names the panel is offering, in the order it offers them.</summary>
    private static IReadOnlyList<string> PartNames(Visual panel) =>
    [
        .. panel.GetVisualDescendants().OfType<ListBoxItem>()
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text)
            .Where(t => t is "R" or "DB9")
            .OfType<string>()
            .Order(StringComparer.Ordinal),
    ];

    private static string Labels(Visual panel) =>
        string.Join(", ", panel.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString() ?? "?"));

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The sheet did not open within 2 s.");
        return task.GetAwaiter().GetResult();
    }
}
