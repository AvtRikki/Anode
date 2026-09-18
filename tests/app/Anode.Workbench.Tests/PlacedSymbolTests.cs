using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Placing a part, then getting the pointer back to do something with it. The tool stays armed after a placement so
/// that several of the same part can be laid down — which means the way out has to be plain, or the next click puts
/// down a second part when it was meant to pick up the first.
/// </summary>
public class PlacedSymbolTests
{
    private const string Library = """
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "anode")
        	(symbol "R"
        		(property "Reference" "R"
        			(at 0 0 0)
        		)
        		(symbol "R_0_1"
        			(rectangle
        				(start -5.08 -5.08)
        				(end 5.08 5.08)
        			)
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
    public Task A_part_on_the_pointer_shows_itself_and_can_be_put_down_again() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-armed-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "project.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(folder, "parts.kicad_sym"), Library);
            string sheet = Path.Combine(folder, "project.kicad_sch");
            File.WriteAllText(sheet, Sheet);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            var right = Assert.Single(shell.RightStacks);
            right.Select(right.Tabs.Single(t => t.Descriptor.Id == "sch.symbols"));
            Dispatcher.UIThread.RunJobs();

            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            var panel = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolsPanel");
            var row = panel.GetVisualDescendants().OfType<ListBoxItem>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "R"));
            Click(window, Middle(row, window), MouseButton.Left);

            // A part on the pointer is a tool like any other, and the bar says so.
            Assert.Contains("sch.tool.symbol", document!.Tools.Select(t => t.Id));
            Assert.Equal("sch.tool.symbol", document.ActiveToolId);

            var canvas = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "SchematicCanvas");
            var middle = Middle(canvas, window);

            Click(window, middle, MouseButton.Left);
            Assert.Equal(1, SymbolCount(document));

            // The tool is still armed, so that several of the same part can be laid down.
            Assert.Equal("sch.tool.symbol", document.ActiveToolId);

            // A right click puts the pointer back — the way out a hand reaches for first.
            Click(window, middle, MouseButton.Right);
            Assert.True(document.ActiveToolId is null, $"the right click left the tool armed: {document.ActiveToolId}");
            Assert.Null(ChosenPart(document));

            // Now the same click picks the part up instead of putting another one down.
            Click(window, middle, MouseButton.Left);
            Assert.Equal(1, SymbolCount(document));
            Assert.True(document.Selection is not null, "the placed part could not be selected");

            // And what can be selected can be deleted.
            Assert.True(shell.Commands.TryExecute("edit.delete"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, SymbolCount(document));

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    private static Point Middle(Visual control, MainWindow window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static string? ChosenPart(IDocument document) =>
        document.GetType().GetProperty("ChosenPart")?.GetValue(document) as string;

    private static int SymbolCount(IDocument document)
    {
        var sheet = document.GetType().GetProperty("Sheet")?.GetValue(document);
        return sheet?.GetType().GetProperty("Symbols")?.GetValue(sheet) is System.Collections.IEnumerable symbols
            ? symbols.Cast<object>().Count()
            : -1;
    }

    private static void Click(MainWindow window, Point at, MouseButton button)
    {
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(at, button);
        window.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

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

    [Fact]
    public Task Parts_are_numbered_as_they_land() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-numbered-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "project.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(folder, "parts.kicad_sym"), Library);
            string sheet = Path.Combine(folder, "project.kicad_sch");
            File.WriteAllText(sheet, Sheet);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            var right = Assert.Single(shell.RightStacks);
            right.Select(right.Tabs.Single(t => t.Descriptor.Id == "sch.symbols"));
            Dispatcher.UIThread.RunJobs();

            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            var panel = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolsPanel");
            var row = panel.GetVisualDescendants().OfType<ListBoxItem>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "R"));
            Click(window, Middle(row, window), MouseButton.Left);

            var canvas = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "SchematicCanvas");
            var first = Middle(canvas, window);
            var second = new Point(first.X + 80, first.Y + 40);

            // The tool stays armed, so two clicks put down two parts.
            Click(window, first, MouseButton.Left);
            Click(window, second, MouseButton.Left);

            // Each arrives carrying its own number: no "R?" to go back and fix.
            Assert.Equal(["R1", "R2"], References(document!));

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>What the parts on the sheet are called, in the order the file holds them.</summary>
    private static IReadOnlyList<string> References(IDocument document)
    {
        var sheet = document.GetType().GetProperty("Sheet")?.GetValue(document);
        if (sheet?.GetType().GetProperty("Symbols")?.GetValue(sheet) is not System.Collections.IEnumerable symbols)
        {
            return [];
        }

        return
        [
            .. symbols.Cast<object>()
                .Select(s => s.GetType().GetProperty("Reference")?.GetValue(s) as string ?? "?")
                .Order(StringComparer.Ordinal),
        ];
    }
}
