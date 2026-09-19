using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// A board's title block written from the inspector's overview, through the board plugin as it ships: the fields
/// are offered, a change marks the board dirty and reads back, undo takes it away, and a save writes it where KiCad
/// would.
/// </summary>
public class BoardTitleBlockTests
{
    [Fact]
    public Task The_board_title_block_is_written_from_the_overview()
    {
        if (TestData.AnyBoard() is not { } fixture)
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            string folder = Directory.CreateTempSubdirectory("anode-board-title-").FullName;
            string board = Path.Combine(folder, Path.GetFileName(fixture));
            File.Copy(fixture, board);

            try
            {
                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                var document = Pump(shell.OpenAsync(board));
                Assert.NotNull(document);

                InspectorRow Row(string name) =>
                    document!.Overview!.Blocks.SelectMany(b => b.Rows).Single(r => r.Name == name);

                string revision = Tr.T("pcb.overview.revision");
                string comment = Tr.T("pcb.overview.comment", 1);
                string before = Row(revision).Value;

                Row(revision).Commit!("B");
                Row(comment).Commit!("Checked on the bench");

                Assert.True(document!.IsDirty);
                Assert.Equal("B", Row(revision).Value);
                Assert.Equal("Checked on the bench", Row(comment).Value);

                // Undo is the board's own command, as the header button runs it.
                Assert.True(shell.Commands.TryExecute("edit.undo"));
                Assert.True(shell.Commands.TryExecute("edit.undo"));
                Assert.Equal(before, Row(revision).Value);
                Assert.False(document.IsDirty);

                Row(revision).Commit!("B");
                Assert.True(Pump(document.SaveAsync()));
                string text = File.ReadAllText(board);
                int block = text.IndexOf("(title_block", StringComparison.Ordinal);
                Assert.True(block >= 0);
                Assert.Contains("(rev \"B\")", text[block..text.IndexOf("(layers", block, StringComparison.Ordinal)], StringComparison.Ordinal);

                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
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
        for (int i = 0; i < 4000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 4 s.");
        return task.GetAwaiter().GetResult();
    }
}
