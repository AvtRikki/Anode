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
/// The inspector shows an object in blocks, and E puts the caret in the first value that can be written. The rule
/// that carries the design is that a value on the field fill can be changed and a bare one was computed — so the
/// test asks which rows offered a way to commit, not how they were painted.
/// </summary>
public class InspectorTests
{
    [Fact]
    public Task A_selected_object_is_shown_in_blocks_and_E_reaches_the_first_value()
    {
        if (TestData.AnySchematic() is not { } fixture)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            string folder = Directory.CreateTempSubdirectory("anode-inspector-").FullName;
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

                // Nothing selected: the panel says so rather than showing an empty card.
                Assert.Null(document!.Selection);

                var canvas = window.GetVisualDescendants().OfType<Control>()
                    .First(c => c.GetType().Name == "SchematicCanvas");
                var middle = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window);
                Assert.NotNull(middle);

                // Something certainly there, at a point certainly on the canvas: a label put down, then picked up.
                Assert.True(shell.Commands.TryExecute("sch.tool.label"));
                Click(window, middle!.Value);
                window.KeyTextInput("VCC");
                Dispatcher.UIThread.RunJobs();
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

                for (int i = 0; i < 50 && !document.IsDirty; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(2);
                }

                Assert.True(shell.Commands.TryExecute("sch.tool.select"));
                Click(window, middle.Value);

                var selection = document.Selection;
                Assert.NotNull(selection);

                // The header names it, and says what kind of thing it is beside the name.
                Assert.Equal("VCC", selection!.Title);
                Assert.Equal(Tr.T("sch.item.label"), selection.Tag);
                Assert.Equal(Path.GetFileName(sheet), selection.Subtitle);

                // Blocks, in the order the document asked for. A label names a net whether or not anything is
                // wired to it yet, so what it is connected to sits between what it is and where it is.
                Assert.Equal(
                    [Tr.T("sch.block.identity"), Tr.T("sch.block.electrics"), Tr.T("sch.block.typography"), Tr.T("sch.block.geometry")],
                    selection.Blocks.Select(b => b.Title));

                var electrics = selection.Blocks[1];
                Assert.Equal("VCC", Assert.Single(electrics.Rows).Value);

                // What can be written, and what was computed.
                var identity = selection.Blocks[0];
                var text = Assert.Single(identity.Rows);
                Assert.Equal("VCC", text.Value);
                Assert.NotNull(text.Commit);

                // How it is set is chosen from a list rather than typed, and the panel draws the list.
                var typography = selection.Blocks[2];
                var face = typography.Rows[0];
                Assert.Equal(Tr.T("sch.property.strokeFont"), face.Value);
                Assert.Equal(Tr.T("sch.property.strokeFont"), face.Choices![0]);
                Assert.Equal(2, window.GetVisualDescendants().OfType<ComboBox>().Count(c => c.IsVisible));

                ShellWindowTests.Snapshot(window, directory, "inspector-typography");

                var geometry = selection.Blocks[3];
                Assert.All(geometry.Rows, row => Assert.NotNull(row.Commit));

                // A position is a pair of millimetres with no unit appended — the unit cost the panel the width it
                // needed for the second number.
                var position = geometry.Rows[0];
                Assert.Contains(" / ", position.Value, StringComparison.Ordinal);
                Assert.DoesNotContain(Tr.T("sch.units.mm"), position.Value, StringComparison.Ordinal);

                // E is answered by the panel: the caret lands in the first value that will take one.
                Assert.True(shell.Commands.TryExecute("sch.properties"));

                // The panel posts the focus behind layout, because a box that has not been laid out yet refuses the
                // caret; so the test waits for it rather than assuming one pump delivers it.
                TextBox? focused = null;
                for (int i = 0; i < 50 && focused is null; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    focused = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsFocused);
                    if (focused is null)
                    {
                        Thread.Sleep(2);
                    }
                }

                // When this fails it must say which of two very different things went wrong: the panel never drew an
                // editable row, or it drew one and the caret never arrived.
                var boxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
                Assert.True(
                    focused is not null,
                    $"E did not put the caret in a value: {boxes.Count} box(es) "
                    + $"[{string.Join(", ", boxes.Select(b => $"\"{b.Text}\""))}]");
                Assert.Equal("VCC", focused!.Text);

                // Writing through that field lands in the file and is one step of the history.
                // Enter commits, with the caret still in the field: nothing else is touched, so nothing else can be
                // what committed it.
                focused.Text = "+3V3";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                Assert.True(Pump(document.SaveAsync()));
                Assert.Contains("(label \"+3V3\"", File.ReadAllText(sheet), StringComparison.Ordinal);

                Assert.True(shell.Commands.TryExecute("edit.undo"));
                Dispatcher.UIThread.RunJobs();
                Assert.True(Pump(document.SaveAsync()));
                Assert.Contains("(label \"VCC\"", File.ReadAllText(sheet), StringComparison.Ordinal);

                // Choosing a face from the list is one step of the history too, and it reaches the file.
                var list = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsVisible);
                string chosen = (string)list.Items[1]!;
                list.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.True(Pump(document.SaveAsync()));
                Assert.Contains($"(face \"{chosen}\")", File.ReadAllText(sheet), StringComparison.Ordinal);

                Assert.True(shell.Commands.TryExecute("edit.undo"));
                Dispatcher.UIThread.RunJobs();
                Assert.True(Pump(document.SaveAsync()));
                Assert.DoesNotContain("(face ", File.ReadAllText(sheet), StringComparison.Ordinal);

                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    private static void Click(MainWindow window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
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
