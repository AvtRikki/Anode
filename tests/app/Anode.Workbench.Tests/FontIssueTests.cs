using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// A face this machine lacks is reported in the checks, as KiCad reports it: which face, and what is drawn in its
/// place — on a board, noting when KiCad's saved letters mean nothing is actually drawn differently.
/// </summary>
public class FontIssueTests
{
    private const string Board = """
        (kicad_pcb (version 20241229) (generator "pcbnew")
        	(layers (0 "F.Cu" signal) (5 "F.SilkS" user))
        	(gr_text "Saved" (at 10 10 0) (layer "F.SilkS")
        		(effects (font (face "Saved Face Anode") (size 1 1)))
        		(render_cache "Saved" 0 (polygon (pts (xy 9 9) (xy 11 9) (xy 11 10)))))
        	(gr_text "Bare" (at 10 20 0) (layer "F.SilkS")
        		(effects (font (face "Bare Face Anode") (size 1 1)))))

        """;

    private const string Sheet = """
        (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
        	(text "Faced" (exclude_from_sim no) (at 50 50 0)
        		(effects (font (face "Sheet Face Anode") (size 2.54 2.54)))
        		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01")))

        """;

    [Fact]
    public Task Missing_faces_are_reported_with_their_stand_ins() => ShellWindowTests.Dispatch(_ =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        string folder = Directory.CreateTempSubdirectory("anode-faces-").FullName;
        string board = Path.Combine(folder, "faces.kicad_pcb");
        string sheet = Path.Combine(folder, "faces.kicad_sch");
        File.WriteAllText(board, Board);
        File.WriteAllText(sheet, Sheet);

        try
        {
            var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var pcb = Pump(shell.OpenAsync(board))!;
            var saved = Assert.Single(pcb.Issues, i => i.Title == Tr.T("pcb.issue.face.title", "Saved Face Anode"));
            var bare = Assert.Single(pcb.Issues, i => i.Title == Tr.T("pcb.issue.face.title", "Bare Face Anode"));
            Assert.Equal(IssueSeverity.Warning, bare.Severity);
            Assert.False(string.IsNullOrWhiteSpace(bare.Location));
            Assert.Equal(Tr.T("pcb.issue.face.saved", "Saved Face Anode", saved.Location), saved.Detail);
            Assert.Equal(Tr.T("pcb.issue.face.detail", "Bare Face Anode", bare.Location), bare.Detail);

            var sch = Pump(shell.OpenAsync(sheet))!;
            var face = Assert.Single(sch.Issues, i => i.Title == Tr.T("sch.issue.face.title", "Sheet Face Anode"));
            Assert.Equal(Tr.T("sch.issue.face.detail", "Sheet Face Anode", face.Location), face.Detail);

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
        for (int i = 0; i < 4000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The task did not finish within 4 s.");
        return task.GetAwaiter().GetResult();
    }
}
