using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Starting from nothing: a new project is a folder with a KiCad project file and one empty sheet in it, opened and
/// editable. Which document a new project starts with belongs to the plugins — the shell asks for a type that can
/// create one rather than knowing about <c>.kicad_sch</c> itself.
/// </summary>
public class NewProjectTests
{
    [Fact]
    public Task A_new_project_holds_a_project_file_and_an_empty_sheet()
    {
        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var (shell, folder) = Workbench();
            try
            {
                Assert.NotNull(shell.CreatableType);

                string project = Path.Combine(folder, "lamp", "lamp.kicad_pro");
                var document = Pump(shell.NewProjectAsync(project));

                // The folder is the project: the project file and the sheet sit in it, and the shell moved there.
                Assert.True(File.Exists(project));
                string sheet = Path.Combine(folder, "lamp", "lamp.kicad_sch");
                Assert.True(File.Exists(sheet));
                Assert.Equal(Path.GetDirectoryName(project), shell.ProjectDirectory);
                Assert.Equal("lamp", shell.ProjectName);

                // The sheet opened as a document, and it is editable: an empty sheet is still a sheet.
                Assert.NotNull(document);
                Assert.Equal("lamp.kicad_sch", document!.Title);
                Assert.Equal("anode.schematic", document.DocumentTypeId);
                Assert.True(document.CanSave);
                Assert.False(document.IsDirty);
                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);

                // Saving it unchanged leaves the file exactly as it was written.
                byte[] before = File.ReadAllBytes(sheet);
                Assert.True(Pump(document.SaveAsync()));
                Assert.Equal(before, File.ReadAllBytes(sheet));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    [Fact]
    public Task A_project_takes_a_folder_of_its_own()
    {
        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var (shell, folder) = Workbench();
            try
            {
                // The place the dialog returns is where the project file goes, not where its files are strewn: a
                // project named "lamp" saved into a folder of other projects gets "lamp/" to itself.
                var document = Pump(shell.NewProjectAsync(Path.Combine(folder, "lamp.kicad_pro")));

                string directory = Path.Combine(folder, "lamp");
                Assert.Equal(directory, shell.ProjectDirectory);
                Assert.True(File.Exists(Path.Combine(directory, "lamp.kicad_pro")));
                Assert.True(File.Exists(Path.Combine(directory, "lamp.kicad_sch")));
                Assert.NotNull(document);

                // Nothing was left beside the folder.
                Assert.Empty(Directory.EnumerateFiles(folder, "*.kicad_*"));

                // The project file names the project; the tree says so in its header rather than as a rule. The panel
                // only builds its rows once it is in a window, so it is shown in one.
                var panel = Assert.Single(shell.Panels.Panels, p => p.Id == "shell.project").CreateContent(shell);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();
                window.Content = new ContentControl { Content = panel, Width = 260, Height = 600 };
                Dispatcher.UIThread.RunJobs();

                var texts = panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
                Assert.Contains("SCHEMATIC", texts);
                Assert.DoesNotContain("RULES", texts);
                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    [Fact]
    public Task A_sheet_can_be_added_to_the_open_project()
    {
        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var (shell, folder) = Workbench();
            try
            {
                Pump(shell.NewProjectAsync(Path.Combine(folder, "lamp", "lamp.kicad_pro")));

                string second = Path.Combine(folder, "lamp", "power.kicad_sch");
                var document = Pump(shell.NewSheetAsync(second));

                Assert.True(File.Exists(second));
                Assert.NotNull(document);
                Assert.Equal("power.kicad_sch", document!.Title);

                // Both sheets are open, and the project did not move.
                Assert.Equal(["lamp.kicad_sch", "power.kicad_sch"], shell.Documents.Select(d => d.Title).Order(StringComparer.Ordinal));
                Assert.Equal(Path.Combine(folder, "lamp"), shell.ProjectDirectory);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    private static (ShellViewModel Shell, string Folder) Workbench()
    {
        string folder = Directory.CreateTempSubdirectory("anode-new-").FullName;
        var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
        return (ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents), folder);
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
