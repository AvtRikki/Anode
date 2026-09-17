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
/// The whole point of the components panel: see what the project can draw from, and put one on the sheet. Driven
/// the way a person drives it — the panel's own row is clicked, then the canvas — because that is the path that has
/// to work.
/// </summary>
public class SymbolsPanelTests
{
    private const string Library = """
        (kicad_symbol_lib
        	(version 20250324)
        	(generator "anode")
        	(symbol "R"
        		(property "Reference" "R"
        			(at 0 0 0)
        		)
        		(property "Value" "R"
        			(at 0 0 0)
        		)
        		(property "Description" "Resistor"
        			(at 0 0 0)
        		)
        		(symbol "R_1_1"
        			(pin passive line
        				(at 0 3.81 270)
        				(length 1.27)
        				(name "~")
        				(number "1")
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
    public Task A_part_from_the_panel_lands_on_the_sheet() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-parts-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "project.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(folder, "parts.kicad_sym"), Library);
            string sheet = Path.Combine(folder, "project.kicad_sch");
            File.WriteAllText(sheet, Sheet);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = App.CreateWorkbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            // The components panel shares the right-hand place with the inspector, which holds it until something
            // asks for this one — so the tab is chosen before the panel can be looked at.
            var right = Assert.Single(shell.RightStacks);
            Assert.Equal(["Inspector", "Components"], right.Tabs.Select(t => t.Title));
            right.Select(right.Tabs.Single(t => t.Descriptor.Id == "sch.symbols"));
            Dispatcher.UIThread.RunJobs();

            // A frame is drawn so the docks are realised and the panel is built.
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            var panel = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "SymbolsPanel");
            Assert.True(panel is not null, "the components panel was not shown for a schematic");

            // The library lying beside the project is listed, without any table saying to look.
            var row = panel!.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "R"));
            Assert.True(row is not null, "the part in the project's own library was not offered");

            row!.Command?.Execute(null);
            InvokeClick(row);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("parts:R", ChosenPart(document!));

            // Then the sheet: the part is dropped where the pointer says.
            var canvas = window.GetVisualDescendants().OfType<Control>()
                .First(c => c.GetType().Name == "SchematicCanvas");
            var middle = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window);
            Assert.NotNull(middle);

            window.MouseDown(middle!.Value, MouseButton.Left);
            window.MouseUp(middle.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(document!.IsDirty, "placing a part did not change the sheet");
            Assert.True(Pump(document.SaveAsync()));

            string written = File.ReadAllText(sheet);
            Assert.Contains("(lib_id \"parts:R\")", written, StringComparison.Ordinal);

            // The definition travels with it, so the sheet still draws without the library.
            Assert.Contains("(symbol \"parts:R\"", written, StringComparison.Ordinal);

            // One click made both, so one undo takes back both.
            Assert.True(shell.Commands.TryExecute("edit.undo"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(Pump(document.SaveAsync()));

            string undone = File.ReadAllText(sheet);
            Assert.DoesNotContain("(lib_id \"parts:R\")", undone, StringComparison.Ordinal);
            Assert.DoesNotContain("(symbol \"parts:R\"", undone, StringComparison.Ordinal);

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>The document is the plugin's, which the tests do not reference: it is asked by name.</summary>
    private static string? ChosenPart(IDocument document) =>
        document.GetType().GetProperty("ChosenPart")?.GetValue(document) as string;

    private static void InvokeClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 2 s.");
        return task.GetAwaiter().GetResult();
    }
}
