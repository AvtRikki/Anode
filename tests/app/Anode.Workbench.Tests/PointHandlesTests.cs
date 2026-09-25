using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The handles of a selected shape are drawn on the canvas, in both themes, without upsetting the frame. The
/// dragging itself is the editor's and is proven there; this is the picture a person sees before taking hold.
/// </summary>
public class PointHandlesTests
{
    private const string Sheet = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(polyline
        		(pts (xy 60 60) (xy 140 60) (xy 140 120) (xy 200 150))
        		(stroke (width 0.3) (type default))
        		(uuid "2a1b2c3d-0000-4000-8000-000000000001")
        	)
        	(rectangle
        		(start 60 90)
        		(end 110 150)
        		(stroke (width 0) (type default))
        		(fill (type none))
        		(uuid "2a1b2c3d-0000-4000-8000-000000000002")
        	)
        	(embedded_fonts no)
        )
        """;

    [Fact]
    public Task Handles_are_drawn_on_the_selected_shapes() => ShellWindowTests.Dispatch(directory =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        string folder = Directory.CreateTempSubdirectory("anode-handles-").FullName;
        try
        {
            string sheet = Path.Combine(folder, "shapes.kicad_sch");
            File.WriteAllText(sheet, Sheet);

            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                Application.Current!.RequestedThemeVariant = theme;
                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                var document = Pump(shell.OpenAsync(sheet));
                Assert.NotNull(document);
                Dispatcher.UIThread.RunJobs();

                foreach (int shape in new[] { 0, 1 })
                {
                    Select(document!, shape);
                    ShellWindowTests.Snapshot(window, directory, $"sheet-handles-{shape}-{theme}");
                }

                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
                window.Close();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>Selects the n-th graphic of the sheet through the document's editor, which the tests only know by name.</summary>
    private static void Select(IDocument document, int index)
    {
        var editor = document.GetType().GetProperty("Editor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(document)!;
        var sheet = editor.GetType().GetProperty("Sheet")!.GetValue(editor)!;
        var graphics = ((IEnumerable)sheet.GetType().GetProperty("Graphics")!.GetValue(sheet)!).Cast<object>().ToList();

        var setSelection = editor.GetType().GetMethod("SetSelection")!;
        var itemType = setSelection.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var chosen = Array.CreateInstance(itemType, 1);
        chosen.SetValue(graphics[index], 0);
        setSelection.Invoke(editor, [chosen]);
        Dispatcher.UIThread.RunJobs();
    }

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
