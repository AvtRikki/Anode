using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The check that reports a pin leading nowhere, and — the half that matters more — the cases where it says nothing.
/// A check that cries about ordinary drawing teaches its reader to stop looking at checks.
/// </summary>
public class LoosePinTests
{
    private static string Sheet(string body) => $$"""
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols
        		(symbol "Device:R"
        			(property "Reference" "R"
        				(at 0 0 0)
        			)
        			(symbol "R_1_1"
        				(pin passive line
        					(at 0 3.81 270)
        					(length 1.27)
        					(name "~")
        					(number "1")
        				)
        				(pin passive line
        					(at 0 -3.81 90)
        					(length 1.27)
        					(name "~")
        					(number "2")
        				)
        			)
        		)
        	)
        	(symbol
        		(lib_id "Device:R")
        		(at 50.8 50.8 0)
        		(unit 1)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000201")
        		(property "Reference" "R1"
        			(at 50.8 50.8 0)
        		)
        	)
        {{body}}
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    private static IReadOnlyList<Issue> IssuesOf(string text)
    {
        IReadOnlyList<Issue> issues = [];

        ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            string folder = Directory.CreateTempSubdirectory("anode-loose-").FullName;
            try
            {
                string sheet = Path.Combine(folder, "project.kicad_sch");
                File.WriteAllText(sheet, text);

                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                var document = Pump(shell.OpenAsync(sheet));
                Assert.NotNull(document);
                Dispatcher.UIThread.RunJobs();

                issues = [.. document!.Issues];
                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }).GetAwaiter().GetResult();

        return issues;
    }

    private static IEnumerable<Issue> Loose(IReadOnlyList<Issue> issues) =>
        issues.Where(i => i.Title == Tr.T("sch.issue.loosePin.title"));

    [Fact]
    public void A_part_wired_to_nothing_has_every_pin_reported()
    {
        var loose = Loose(IssuesOf(Sheet(string.Empty))).ToList();

        Assert.Equal(2, loose.Count);
        Assert.Contains(loose, i => i.Detail.Contains("R1-1", StringComparison.Ordinal));
        Assert.Contains(loose, i => i.Detail.Contains("R1-2", StringComparison.Ordinal));

        // The place is named, so the reader can go and look.
        Assert.All(loose, i => Assert.Contains("50.8", i.Location, StringComparison.Ordinal));
    }

    [Fact]
    public void A_pin_marked_no_connect_is_not_reported()
    {
        // The mark sits on the lower pin, 3.81 below the middle of the part.
        var loose = Loose(IssuesOf(Sheet("""
        	(no_connect
        		(at 50.8 54.61)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000202")
        	)
        """))).ToList();

        var only = Assert.Single(loose);
        Assert.Contains("R1-1", only.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_on_a_named_net_is_not_reported()
    {
        // A label on the lower pin: one pin on a named net is ordinary — a signal leaving for elsewhere.
        var loose = Loose(IssuesOf(Sheet("""
        	(label "SDA"
        		(at 50.8 54.61 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000203")
        	)
        """))).ToList();

        var only = Assert.Single(loose);
        Assert.Contains("R1-1", only.Detail, StringComparison.Ordinal);
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
