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
/// The panels at the foot of a sheet — the fields table and find — driven as a person drives them, on one of KiCad's
/// own designs: the command brings the panel up, a value typed into a line of the table lands in every part of it,
/// and the find box finds.
/// </summary>
public class SheetTablesTests
{
    [Fact]
    public Task A_value_typed_into_the_table_lands_in_every_part_of_its_line() => ShellWindowTests.Dispatch(directory =>
    {
        string demo = Path.Combine(TestData.KiCadDir, "demos", "pic_programmer");
        Assert.SkipUnless(Directory.Exists(demo), TestData.SkipReason);

        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-tables-").FullName;
        try
        {
            foreach (string file in Directory.GetFiles(demo, "*.kicad_*"))
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }

            string sheet = Path.Combine(folder, "pic_programmer.kicad_sch");
            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
            window.Show();

            var document = Pump(shell.OpenAsync(sheet));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            Assert.True(shell.Commands.TryExecute("sch.fieldsTable"));
            ShellWindowTests.Snapshot(window, directory, "sheet-fields-table");

            var table = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "FieldsPanel");
            Assert.True(table is not null, "the fields table did not come up");

            // The seven 10K resistors are one line, grouped by value as KiCad groups them: one box says 10K.
            var cell = Assert.Single(table!.GetVisualDescendants().OfType<TextBox>(), b => b.Text == "10K");
            cell.Focus();
            cell.SelectAll();
            window.KeyTextInput("12K");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(document!.IsDirty, "writing into the table did not change the sheet");
            Assert.True(Pump(document.SaveAsync()));
            string written = File.ReadAllText(sheet);
            Assert.Equal(7, Count(written, "(property \"Value\" \"12K\""));
            Assert.Equal(0, Count(written, "(property \"Value\" \"10K\""));

            // One value typed is one step back.
            Assert.True(shell.Commands.TryExecute("edit.undo"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(Pump(document.SaveAsync()));
            Assert.Equal(7, Count(File.ReadAllText(sheet), "(property \"Value\" \"10K\""));

            // Find comes up where the table was, and finds the seven again.
            Assert.True(shell.Commands.TryExecute("edit.find"));
            Dispatcher.UIThread.RunJobs();
            var find = window.GetVisualDescendants().FirstOrDefault(v => v.GetType().Name == "FindPanel");
            Assert.True(find is not null, "the find panel did not come up");

            var box = find!.GetVisualDescendants().OfType<TextBox>().First();
            box.Focus();
            window.KeyTextInput("10K");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            ShellWindowTests.Snapshot(window, directory, "sheet-find-panel");

            Assert.Contains(find.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } said && said.Contains('7'));
            Assert.Single(document.GetType().GetProperty("Selection")!.GetValue(document) is SelectionInfo s ? [s] : Array.Empty<SelectionInfo>());

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    private static int Count(string text, string what)
    {
        int count = 0;
        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
