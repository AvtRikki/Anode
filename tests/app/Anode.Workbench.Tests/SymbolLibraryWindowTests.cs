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
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// A symbol library opened in the window: it is a tab of its own, its symbols are listed beside it, a click there
/// brings a symbol onto the canvas, and its units are switched from the bar above.
/// </summary>
public class SymbolLibraryWindowTests
{
    [Fact]
    public Task A_library_opens_as_a_tab_and_a_symbol_is_chosen_from_the_panel() => ShellWindowTests.Dispatch(directory =>
    {
        string library = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "libs", "4xxx.kicad_sym");
        Assert.SkipUnless(File.Exists(library), TestData.SkipReason);

        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        string folder = Directory.CreateTempSubdirectory("anode-symlib-").FullName;
        try
        {
            string copy = Path.Combine(folder, "4xxx.kicad_sym");
            File.Copy(library, copy);

            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
            window.Show();

            var document = Pump(shell.OpenAsync(copy));
            Assert.NotNull(document);
            Assert.Equal("anode.symlib", document!.DocumentTypeId);
            Dispatcher.UIThread.RunJobs();

            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            // The library's symbols are listed; a click on one brings it up.
            var panel = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "SymbolListPanel");
            Assert.True(panel is not null, "the library's symbols were not listed");

            var row = panel!.GetVisualDescendants().OfType<Button>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "4001"));
            row.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
            Assert.NotNull(at);
            window.MouseDown(at!.Value, MouseButton.Left);
            window.MouseUp(at.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(document.StatusFields, f => f.Text.Contains("4001", StringComparison.Ordinal));

            // Its second gate, from the bar over the canvas.
            var view = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolLibraryView");
            var units = view.GetVisualDescendants().OfType<ComboBox>().First();
            Assert.Equal(5, units.ItemCount);
            units.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(document.StatusFields, f => f.Text.Contains("4001", StringComparison.Ordinal) && f.Text.EndsWith('B'));

            ShellWindowTests.Snapshot(window, directory, "symbol-library");

            // Nothing was changed, so nothing is dirty and nothing is lost.
            Assert.False(document.IsDirty);
            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>
    /// A library from nothing, and a symbol in it, the way a person makes them: File → New symbol library asks where,
    /// the + in the panel asks for a name, Enter makes the symbol and brings it up, and saving writes what KiCad reads.
    /// </summary>
    [Fact]
    public Task A_new_library_and_a_new_symbol_in_it() => ShellWindowTests.Dispatch(directory =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        string folder = Directory.CreateTempSubdirectory("anode-newlib-").FullName;
        try
        {
            string path = Path.Combine(folder, "mine.kicad_sym");
            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
            window.Show();

            // After the window, which sets its own picker.
            shell.PickNewFile = (_, _, _, _) => Task.FromResult<string?>(path);

            Assert.True(shell.Commands.TryExecute("sch.newLibrary"));
            for (int i = 0; i < 2000 && shell.ActiveDocument?.DocumentTypeId != "anode.symlib"; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }

            var document = shell.ActiveDocument;
            Assert.Equal("anode.symlib", document?.DocumentTypeId);
            Assert.True(File.Exists(path));

            // The + opens a box for the name; Enter makes it.
            Assert.True(shell.Commands.TryExecute("sch.lib.newSymbol"));
            Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }

            var panel = window.GetVisualDescendants().First(v => v.GetType().Name == "SymbolListPanel");
            var box = panel.GetVisualDescendants().OfType<TextBox>().Single(b => b.IsVisible && b.PlaceholderText == Tr.T("sch.lib.newName"));
            box.Focus();
            window.KeyTextInput("OPA1612");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(document!.IsDirty);
            Assert.Contains(document.StatusFields, f => f.Text.StartsWith("OPA1612", StringComparison.Ordinal));
            ShellWindowTests.Snapshot(window, directory, "symbol-library-new");

            Assert.True(Pump(document.SaveAsync()));
            Assert.Contains("(symbol \"OPA1612\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>
    /// A pin of a library symbol is selected and turned from the keyboard, as on a sheet: R on the canvas turns it,
    /// the library is changed, and the inspector offers the pin's type to be chosen.
    /// </summary>
    [Fact]
    public Task A_pin_is_turned_from_the_keyboard() => ShellWindowTests.Dispatch(directory =>
    {
        string library = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "libs", "4xxx.kicad_sym");
        Assert.SkipUnless(File.Exists(library), TestData.SkipReason);

        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        string folder = Directory.CreateTempSubdirectory("anode-symkeys-").FullName;
        try
        {
            string copy = Path.Combine(folder, "4xxx.kicad_sym");
            File.Copy(library, copy);
            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
            window.Show();

            var document = Pump(shell.OpenAsync(copy))!;
            document.GetType().GetMethod("Show")!.Invoke(document, ["4001"]);
            Dispatcher.UIThread.RunJobs();

            // The output pin of the first gate, chosen through the document's editor.
            var editor = document.GetType().GetProperty("Editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(document)!;
            var scene = editor.GetType().GetProperty("Scene")!.GetValue(editor)!;
            var items = ((System.Collections.IEnumerable)scene.GetType().GetProperty("TopLevelItems")!.GetValue(scene)!).Cast<object>().ToList();
            var pin = items.First(i => i.GetType().Name == "SchPin" && (string)i.GetType().GetProperty("Number")!.GetValue(i)! == "3");
            double angle = (double)pin.GetType().GetProperty("Angle")!.GetValue(pin)!;

            var setSelection = editor.GetType().GetMethod("SetSelection")!;
            var chosen = Array.CreateInstance(setSelection.GetParameters()[0].ParameterType.GetGenericArguments()[0], 1);
            chosen.SetValue(pin, 0);
            setSelection.Invoke(editor, [chosen]);

            var canvas = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "SchematicCanvas");
            canvas.Focus();
            window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((angle + 90) % 360, (double)pin.GetType().GetProperty("Angle")!.GetValue(pin)!);
            Assert.True(document.IsDirty);
            Assert.Contains(document.Selection!.Blocks.SelectMany(b => b.Rows), r => r.Choices is { Count: 12 });
            ShellWindowTests.Snapshot(window, directory, "symbol-pin-turned");

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    private static T Pump<T>(Task<T> task)
    {
        for (int i = 0; i < 5000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 5 s.");
        return task.GetAwaiter().GetResult();
    }
}
