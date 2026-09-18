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
/// Dropping a part onto the sheet. The gesture itself belongs to the operating system and cannot be driven here, so
/// what is tested is the seam that carries its meaning: a part, a place on the canvas, and what lands on the sheet.
/// </summary>
public class DroppedPartTests
{
    [Fact]
    public Task A_part_dropped_on_the_canvas_lands_where_it_was_let_go() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-drop-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "project.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(folder, "parts.kicad_sym"), """
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
                """);

            string sheet = Path.Combine(folder, "project.kicad_sch");
            File.WriteAllText(sheet, """
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
                """);

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

            // The part as the panel offers it, and the canvas it would be dropped on.
            var panel = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolsPanel");
            var row = panel.GetVisualDescendants().OfType<ListBoxItem>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "R"));
            object choice = row.DataContext!;

            var canvas = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "SchematicCanvas");

            // Two different places on the canvas: what is dropped must land where it was let go, not at a fixed spot.
            Assert.True(Drop(document!, choice, new Point(canvas.Bounds.Width * 0.35, canvas.Bounds.Height * 0.4)));
            Assert.True(Drop(document!, choice, new Point(canvas.Bounds.Width * 0.65, canvas.Bounds.Height * 0.6)));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["R1", "R2"], References(document!));
            Assert.Equal(2, Positions(document!).Distinct().Count());

            // And one undo takes back one part, as one drop put it down.
            Assert.True(shell.Commands.TryExecute("edit.undo"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(References(document!));

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>What the canvas does when a part is let go over it.</summary>
    private static bool Drop(IDocument document, object choice, Point at)
    {
        var libId = choice.GetType().GetProperty("LibId")!.GetValue(choice)!;
        var symbol = choice.GetType().GetProperty("Symbol")!.GetValue(choice)!;
        var drop = document.GetType().GetMethod("DropPart")!;
        return (bool)drop.Invoke(document, [libId, symbol, at])!;
    }

    private static IReadOnlyList<string> References(IDocument document) =>
    [
        .. Symbols(document)
            .Select(s => s.GetType().GetProperty("Reference")?.GetValue(s) as string ?? "?")
            .Order(StringComparer.Ordinal),
    ];

    private static IReadOnlyList<string> Positions(IDocument document) =>
        [.. Symbols(document).Select(s => s.GetType().GetProperty("Position")?.GetValue(s)?.ToString() ?? "?")];

    private static IEnumerable<object> Symbols(IDocument document)
    {
        var sheet = document.GetType().GetProperty("Sheet")?.GetValue(document);
        return sheet?.GetType().GetProperty("Symbols")?.GetValue(sheet) is System.Collections.IEnumerable symbols
            ? symbols.Cast<object>()
            : [];
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
}
