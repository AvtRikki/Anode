using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Tests;
using Anode.Workbench.Services;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// The nets of the open sheet, listed in a panel of their own: choosing one lights it on the canvas, as the status
/// bar then says, and choosing it again puts it out. The board's layers sit in the same place, so the panel appears
/// only for a sheet.
/// </summary>
public class NetsPanelTests
{
    [Fact]
    public Task A_net_chosen_in_the_panel_is_lit_on_the_sheet()
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

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-nets-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            var document = Pump(shell.OpenAsync(fixture));
            Assert.NotNull(document);
            Dispatcher.UIThread.RunJobs();

            // The sheet brings the panel; the tab has to be shown for the list to be drawn.
            var stack = shell.LeftStacks.First(s => s.Tabs.Any(t => t.Descriptor.Id == "sch.nets"));
            stack.Select(stack.Tabs.First(t => t.Descriptor.Id == "sch.nets"));
            Dispatcher.UIThread.RunJobs();

            var panel = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.GetType().Name == "NetsPanel");
            Assert.True(panel is not null,
                $"no nets panel among [{string.Join(", ", shell.LeftStacks.SelectMany(s => s.Tabs).Select(t => t.Descriptor.Id))}] "
                + $"/ rail [{string.Join(", ", shell.Rail.Select(r => r.Descriptor.Id))}]");
            var rows = panel.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("row")).ToList();
            Assert.NotEmpty(rows);

            // Choosing a row lights that net, and the status bar names it.
            // The row is "name … pins"; the name is the one that is not docked to the right.
            string Name(Button row) => row.GetVisualDescendants().OfType<TextBlock>()
                .First(t => DockPanel.GetDock(t) != Dock.Right && !string.IsNullOrWhiteSpace(t.Text)).Text!;
            string name = Name(rows[0]);
            rows[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                document!.StatusFields.Any(f => f.Text.Contains(name, StringComparison.Ordinal)),
                $"row '{name}' lit nothing: [{string.Join(" | ", document.StatusFields.Select(f => f.Text))}]");
            ShellWindowTests.Snapshot(window, directory, "nets-panel");

            // The same row again puts it out.
            var again = panel.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("row"));
            string second = Name(again);
            again.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(
                !document.StatusFields.Any(f => f.Text.Contains(name, StringComparison.Ordinal)),
                $"clicking '{second}' did not put '{name}' out: [{string.Join(" | ", document.StatusFields.Select(f => f.Text))}]");

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        });
    }

    [Fact]
    public Task The_design_scope_lists_the_nets_of_every_sheet()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        if (!File.Exists(root) || !File.Exists(Path.ChangeExtension(root, ".kicad_pro")))
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(directory =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

            var recents = new RecentProjectsStore(Path.Combine(Path.GetTempPath(), $"anode-nets-{Guid.NewGuid():N}.json"));
            var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
            var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
            window.Show();

            Assert.NotNull(Pump(shell.OpenAsync(root)));
            Dispatcher.UIThread.RunJobs();

            var stack = shell.LeftStacks.First(s => s.Tabs.Any(t => t.Descriptor.Id == "sch.nets"));
            stack.Select(stack.Tabs.First(t => t.Descriptor.Id == "sch.nets"));
            Dispatcher.UIThread.RunJobs();

            var panel = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "NetsPanel");
            int Rows() => panel.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("row"));
            string Names() => string.Join(",", panel.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("row"))
                .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First(t => DockPanel.GetDock(t) != Dock.Right).Text));

            int onSheet = Rows();
            string sheetNames = Names();
            Assert.True(onSheet > 0);

            // The scope button turns the list into the design's nets: the amplifier sheet stands twice under this
            // root, so the design has nets this sheet alone never shows.
            var scope = panel.GetVisualDescendants().OfType<Button>().First(b => !b.Classes.Contains("row"));
            scope.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(sheetNames, Names());
            Assert.True(Rows() > onSheet, $"design {Rows()} rows is not more than the sheet's {onSheet}");
            Assert.Contains("+12V", Names(), StringComparison.Ordinal);
            ShellWindowTests.Snapshot(window, directory, "nets-panel-design");

            Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
            window.Close();
        });
    }

    /// <summary>
    /// The netlist command writes the design's netlist where the picker points. The file is KiCad's own export
    /// form, so the check is that it parses as one and carries the nets of the design.
    /// </summary>
    [Fact]
    public Task The_netlist_command_writes_the_designs_netlist()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        if (!File.Exists(root))
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            string folder = Directory.CreateTempSubdirectory("anode-netlist-").FullName;
            string target = Path.Combine(folder, "design.net");

            try
            {
                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                // The picker is the window's; here it answers with the path the test chose.
                shell.PickNewFile = (_, _, _, _) => Task.FromResult<string?>(target);

                Assert.NotNull(Pump(shell.OpenAsync(root)));
                Dispatcher.UIThread.RunJobs();

                Assert.True(shell.Commands.TryExecute("sch.exportNetlist"));
                for (int i = 0; i < 2000 && !File.Exists(target); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }

                Assert.True(File.Exists(target), "the netlist was not written");
                string netlist = File.ReadAllText(target);
                Assert.StartsWith("(export (version \"E\")", netlist, StringComparison.Ordinal);
                Assert.Contains("(components", netlist, StringComparison.Ordinal);
                Assert.Contains("(net (code ", netlist, StringComparison.Ordinal);

                // The design's supply reaches both places of the amplifier sheet, so it is one net of many pins.
                Assert.Contains("(name \"+12V\")", netlist, StringComparison.Ordinal);

                Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
                window.Close();
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    /// <summary>
    /// The bill of materials command writes the design's parts where the picker points, as the CSV KiCad's own
    /// default preset writes: the columns in its order, one line per part that is the same thing.
    /// </summary>
    [Fact]
    public Task The_bill_of_materials_command_writes_the_designs_parts()
    {
        string root = Path.Combine(TestData.KiCadDir, "demos", "complex_hierarchy", "complex_hierarchy.kicad_sch");
        if (!File.Exists(root))
        {
            Assert.Skip(TestData.SkipReason);
            return Task.CompletedTask;
        }

        return ShellWindowTests.Dispatch(_ =>
        {
            GraphicsOptions.Renderer = RendererKind.Skia;
            string folder = Directory.CreateTempSubdirectory("anode-bom-").FullName;
            string target = Path.Combine(folder, "design.csv");

            try
            {
                var recents = new RecentProjectsStore(Path.Combine(folder, "recents.json"));
                var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
                var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
                window.Show();

                shell.PickNewFile = (_, _, _, _) => Task.FromResult<string?>(target);

                Assert.NotNull(Pump(shell.OpenAsync(root)));
                Dispatcher.UIThread.RunJobs();

                Assert.True(shell.Commands.TryExecute("sch.exportBom"));
                for (int i = 0; i < 2000 && !File.Exists(target); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }

                Assert.True(File.Exists(target), "the bill of materials was not written");
                string[] rows = File.ReadAllLines(target);

                Assert.Equal("\"Reference\",\"Value\",\"Datasheet\",\"Footprint\",\"Qty\",\"DNP\"", rows[0]);
                Assert.True(rows.Length > 1, "the bill of materials has no parts on it");

                // The amplifier sheet stands twice, so its parts are there under the designators of both places.
                Assert.Contains(rows, r => r.Contains("R201", StringComparison.Ordinal));
                Assert.Contains(rows, r => r.Contains("R301", StringComparison.Ordinal));

                // Power symbols are not parts.
                Assert.DoesNotContain(rows, r => r.Contains("\"#", StringComparison.Ordinal));

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
