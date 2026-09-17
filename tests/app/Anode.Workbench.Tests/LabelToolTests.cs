using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Placing a label, the whole way: choose the tool, click the sheet, type the name, press Enter. The name is asked
/// for in a field on the canvas, and a field that never takes the keys is a label that never appears — so the test
/// drives real pointer and keyboard input rather than calling the editor underneath.
/// </summary>
public class LabelToolTests
{
    [Fact]
    public Task A_label_is_named_on_the_canvas_and_lands_in_the_file()
    {
        if (TestData.AnySchematic() is not { } fixture)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            string folder = Directory.CreateTempSubdirectory("anode-label-").FullName;
            string sheet = Path.Combine(folder, Path.GetFileName(fixture));
            File.Copy(fixture, sheet);

            try
            {
                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                var document = Pump(shell.OpenAsync(sheet));
                Assert.NotNull(document);
                Dispatcher.UIThread.RunJobs();

                Assert.True(shell.Commands.TryExecute("sch.tool.label"));
                Assert.Equal("sch.tool.label", document!.ActiveToolId);

                // The canvas belongs to the plugin, which the tests do not reference: it is found by what it is.
                var canvas = window.GetVisualDescendants().OfType<Control>()
                    .First(c => c.GetType().Name == "SchematicCanvas");
                var middle = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window);
                Assert.NotNull(middle);

                window.MouseDown(middle!.Value, MouseButton.Left);
                window.MouseUp(middle.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                // The field is up and has the keys — this is what was broken when a placed label could not be seen.
                var box = canvas.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                Assert.NotNull(box);
                Assert.True(box!.IsFocused);

                window.KeyTextInput("VCC");
                Dispatcher.UIThread.RunJobs();

                // What was typed has to be in the field before Enter can mean anything.
                Assert.Equal("VCC", box.Text);

                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

                // The field answers a task, so the placing lands a turn of the loop later; give it that turn.
                for (int i = 0; i < 50 && !document.IsDirty; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(2);
                }

                // The field is gone, the sheet has changed, and what was typed is in the file that gets saved.
                Assert.Empty(canvas.GetVisualDescendants().OfType<TextBox>());
                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
                Assert.True(document.IsDirty);
                Assert.True(Pump(document.SaveAsync()));
                Assert.Contains("(label \"VCC\"", File.ReadAllText(sheet), StringComparison.Ordinal);

                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
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
