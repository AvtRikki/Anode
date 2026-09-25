using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// Changing parts from the bill and taking it back from the bill: a change that reaches two sheets is one thing to
/// undo there, whichever sheets it touched; flags are ticks; and a sheet edited since is not undone from under.
/// </summary>
public class BomUndoTests
{
    private const string Root = "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10";
    private const string Child = "3a1b2c3d-0000-4000-8000-000000000001";

    [Fact]
    public Task A_change_across_two_sheets_is_one_step_to_undo_from_the_bill() => ShellWindowTests.Dispatch(directory =>
    {
        using var design = Design(out string root, out string child);
        var (shell, window) = Open(root);

        Assert.True(shell.Commands.TryExecute("sch.bom"));
        Dispatcher.UIThread.RunJobs();
        var bom = shell.ActiveDocument!;
        var view = View(window);

        // R1 on the root and R2 on the sheet below are one line of 10k: one box.
        var cell = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), b => b.Text == "10k");
        Type(window, cell, "4k7");
        Settle(() => Sheets(shell).Count() == 2 && Sheets(shell).All(d => d.IsDirty));

        // Both sheets took it, each in its own tab, and the bill is at the front again.
        Assert.All(Sheets(shell), d => Assert.True(d.IsDirty));
        Assert.Same(bom, shell.ActiveDocument);
        Assert.Contains(View(window).GetVisualDescendants().OfType<TextBox>(), b => b.Text == "4k7");

        // One undo from the bill takes it back on both.
        Assert.True(shell.Commands.TryExecute("edit.undo"));
        Settle(() => Sheets(shell).All(d => !d.IsDirty));
        Assert.All(Sheets(shell), d => Assert.False(d.IsDirty));
        Assert.Contains(View(window).GetVisualDescendants().OfType<TextBox>(), b => b.Text == "10k");

        // And one redo puts it on both again.
        Assert.True(shell.Commands.TryExecute("edit.redo"));
        Settle(() => Sheets(shell).All(d => d.IsDirty));
        foreach (var sheet in Sheets(shell))
        {
            Assert.True(Pump(sheet.SaveAsync()));
        }

        Assert.Contains("\"Value\" \"4k7\"", File.ReadAllText(root), StringComparison.Ordinal);
        Assert.Contains("\"Value\" \"4k7\"", File.ReadAllText(child), StringComparison.Ordinal);

        ShellWindowTests.Snapshot(window, directory, "bom-undo");
        Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
        window.Close();
    });

    /// <summary>A flag is a tick; ticking a line sets it on every part, and the bill's undo clears it again.</summary>
    [Fact]
    public Task A_flag_ticked_in_the_bill_is_set_on_every_part_and_undone_from_it() => ShellWindowTests.Dispatch(_ =>
    {
        using var design = Design(out string root, out string child);
        var (shell, window) = Open(root);

        Assert.True(shell.Commands.TryExecute("sch.bom"));
        Dispatcher.UIThread.RunJobs();

        // The project's preset has the DNP column; the 10k line's tick is off.
        var tick = View(window).GetVisualDescendants().OfType<CheckBox>().First(c => c.IsChecked == false && c.Content is null);
        tick.IsChecked = true;
        Settle(() => Sheets(shell).Count() == 2 && Sheets(shell).All(d => d.IsDirty));

        foreach (var sheet in Sheets(shell))
        {
            Assert.True(Pump(sheet.SaveAsync()));
        }

        Assert.Contains("(dnp yes)", File.ReadAllText(root), StringComparison.Ordinal);
        Assert.Contains("(dnp yes)", File.ReadAllText(child), StringComparison.Ordinal);

        shell.Show(shell.Documents.First(d => d.DocumentTypeId == "anode.bom"));
        Assert.True(shell.Commands.TryExecute("edit.undo"));
        Settle(() => Sheets(shell).All(d => d.IsDirty));
        foreach (var sheet in Sheets(shell))
        {
            Assert.True(Pump(sheet.SaveAsync()));
        }

        Assert.DoesNotContain("(dnp yes)", File.ReadAllText(root), StringComparison.Ordinal);
        Assert.DoesNotContain("(dnp yes)", File.ReadAllText(child), StringComparison.Ordinal);
        window.Close();
    });

    /// <summary>
    /// A sheet edited after the change has its own steps on top of it. Undoing the change from under them would take
    /// the drawing apart, so the bill does not offer it; the sheet's own undo is the way back.
    /// </summary>
    [Fact]
    public Task A_sheet_edited_since_is_not_undone_from_under() => ShellWindowTests.Dispatch(_ =>
    {
        using var design = Design(out string root, out _);
        var (shell, window) = Open(root);
        var sheet = shell.ActiveDocument!;

        Assert.True(shell.Commands.TryExecute("sch.bom"));
        Dispatcher.UIThread.RunJobs();
        var bom = shell.ActiveDocument!;

        var cell = Assert.Single(View(window).GetVisualDescendants().OfType<TextBox>(), b => b.Text == "10k");
        Type(window, cell, "4k7");
        Settle(() => Sheets(shell).Count() == 2);
        Assert.True(shell.Commands.Find("edit.undo")!.CanExecute(), "the change could not be undone from the bill to begin with");

        // Something else done on the root sheet itself, after the change.
        shell.Activate(sheet);
        Dispatcher.UIThread.RunJobs();
        Rotate(sheet);
        Dispatcher.UIThread.RunJobs();

        shell.Show(bom);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Commands.Find("edit.undo")!.CanExecute());
        window.Close();
    });

    /// <summary>Turns the first part of the sheet, which is an edit of the sheet's own.</summary>
    private static void Rotate(IDocument sheet)
    {
        var editor = sheet.GetType().GetProperty("Editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sheet)!;
        var theSheet = editor.GetType().GetProperty("Sheet")!.GetValue(editor)!;
        var symbols = (System.Collections.IEnumerable)theSheet.GetType().GetProperty("Symbols")!.GetValue(theSheet)!;
        var first = symbols.Cast<object>().First();

        var setSelection = editor.GetType().GetMethod("SetSelection")!;
        var chosen = Array.CreateInstance(setSelection.GetParameters()[0].ParameterType.GetGenericArguments()[0], 1);
        chosen.SetValue(first, 0);
        setSelection.Invoke(editor, [chosen]);
        editor.GetType().GetMethod("Rotate")!.Invoke(editor, [90.0]);
    }

    private static IEnumerable<IDocument> Sheets(ShellViewModel shell) => shell.Documents.Where(d => d.DocumentTypeId != "anode.bom" && d.FilePath is not null);

    private static Control View(Window window) =>
        window.GetVisualDescendants().OfType<Control>().First(v => v.GetType().Name == "BomView");

    private static void Type(Window window, TextBox cell, string text)
    {
        cell.Focus();
        cell.SelectAll();
        window.KeyTextInput(text);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static void Settle(Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static (ShellViewModel Shell, Window Window) Open(string root)
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var recents = new RecentProjectsStore(Path.Combine(Path.GetDirectoryName(root)!, "recents.json"));
        var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
        var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
        window.Show();

        Assert.NotNull(Pump(shell.OpenAsync(root)));
        Dispatcher.UIThread.RunJobs();
        return (shell, window);
    }

    /// <summary>
    /// A root sheet with R1 of 10k and a child sheet placed on it with R2 of 10k, and a project whose preset groups
    /// by value and shows the DNP column.
    /// </summary>
    private static TempFolder Design(out string root, out string child)
    {
        var folder = new TempFolder();
        root = Path.Combine(folder.Path, "design.kicad_sch");
        child = Path.Combine(folder.Path, "child.kicad_sch");

        File.WriteAllText(Path.Combine(folder.Path, "design.kicad_pro"), """
            { "schematic": { "bom_settings": { "name": "Grouped By Value", "sort_field": "Reference", "sort_asc": true,
                "filter_string": "", "group_symbols": true, "exclude_dnp": false, "include_excluded_from_bom": false,
                "fields_ordered": [
                    { "name": "Reference", "label": "Reference", "show": true, "group_by": false },
                    { "name": "Value", "label": "Value", "show": true, "group_by": true },
                    { "name": "${QUANTITY}", "label": "Qty", "show": true, "group_by": false },
                    { "name": "${DNP}", "label": "DNP", "show": true, "group_by": true } ] } } }
            """);

        File.WriteAllText(root, $"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "{Root}") (paper "A4")
            	(lib_symbols)
            {Part(1, "R1", $"/{Root}")}
            	(sheet (at 100 50) (size 30 20) (uuid "{Child}")
            		(property "Sheetname" "Child" (at 100 49 0))
            		(property "Sheetfile" "child.kicad_sch" (at 100 71 0))
            		(instances (project "design" (path "/{Root}" (page "2")))))
            	(sheet_instances (path "/" (page "1")))
            	(embedded_fonts no))

            """);

        File.WriteAllText(child, $"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "7f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
            	(lib_symbols)
            {Part(2, "R2", $"/{Root}/{Child}")}
            	(embedded_fonts no))

            """);

        return folder;

        static string Part(int n, string reference, string path) => $"""
            	(symbol (lib_id "Device:R") (at {50 * n} 50 0) (unit 1) (in_bom yes) (on_board yes) (uuid "0a1b2c3d-0000-4000-8000-00000000000{n}")
            		(property "Reference" "{reference}" (at {50 * n} 45 0))
            		(property "Value" "10k" (at {50 * n} 55 0))
            		(property "Footprint" "R_0603" (at {50 * n} 50 0) (hide yes))
            		(instances (project "design" (path "{path}" (reference "{reference}") (unit 1)))))
            """;
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

    /// <summary>A folder of its own for one test, removed with it.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("anode-bom-undo-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
