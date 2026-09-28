using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench.Tests;

/// <summary>
/// From a part on the sheet to its symbol in the library and back, in the window: ⌘E opens the library on the part's
/// symbol at the unit it is placed as; a change made there — not yet saved — is told on the sheet on return, and one
/// command takes it. A part whose library cannot be found says why instead of doing nothing.
/// </summary>
public class LibraryLinkWindowTests
{
    private const string Root = "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b12";

    [Fact]
    public Task A_part_is_edited_in_its_library_and_the_sheet_takes_the_change() => ShellWindowTests.Dispatch(directory =>
    {
        using var project = new TempFolder();
        string sheetPath = Project(project.Path, out string libraryPath);
        var (shell, window) = Open(sheetPath);
        var sheet = shell.ActiveDocument!;

        Select(sheet, "Mine:Amp");
        Assert.True(shell.Commands.TryExecute("sch.editSymbol"));
        Settle(() => shell.ActiveDocument?.DocumentTypeId == "anode.symlib");

        // The library, on Amp, at unit B — where the part on the sheet is.
        var library = shell.ActiveDocument!;
        Assert.Equal("anode.symlib", library.DocumentTypeId);
        Assert.Contains(library.StatusFields, f => f.Text == Tr.T("sch.lib.status.symbol", "Amp", "B"));

        var description = library.Overview!.Blocks.SelectMany(b => b.Rows).Single(r => r.Name == Tr.T("sch.lib.row.description"));
        description.Commit!("Low-noise amplifier");
        Assert.True(library.IsDirty);

        // Back on the sheet: the change is told, before the library is saved, and says it is not saved yet.
        shell.Activate(sheet);
        Settle(() => Drifts(sheet).Any());
        var drift = Assert.Single(Drifts(sheet));
        Assert.Contains(Tr.T("sch.lib.row.description"), drift.Detail, StringComparison.Ordinal);
        Assert.Contains(Tr.T("sch.issue.libraryDrift.unsaved"), drift.Detail, StringComparison.Ordinal);
        ShellWindowTests.Snapshot(window, directory, "library-drift");

        Assert.True(shell.Commands.TryExecute("sch.updateFromLibrary"));
        Settle(() => !Drifts(sheet).Any());
        Assert.Empty(Drifts(sheet));
        Assert.True(sheet.IsDirty);

        Assert.True(Pump(sheet.SaveAsync()));
        Assert.Contains("Low-noise amplifier", File.ReadAllText(sheetPath), StringComparison.Ordinal);
        Assert.DoesNotContain("Low-noise amplifier", File.ReadAllText(libraryPath), StringComparison.Ordinal);

        // And one undo on the sheet puts the old copy back, which differs again.
        Assert.True(shell.Commands.TryExecute("edit.undo"));
        Settle(() => Drifts(sheet).Any());
        Assert.Single(Drifts(sheet));

        Assert.DoesNotContain(shell.Log.Entries, e => e.Level == LogLevel.Error);
        window.Close();
    });

    [Fact]
    public Task A_part_whose_library_is_in_no_table_says_why_it_cannot_be_edited() => ShellWindowTests.Dispatch(_ =>
    {
        using var project = new TempFolder();
        string sheetPath = Project(project.Path, out _);
        var (shell, window) = Open(sheetPath);
        var sheet = shell.ActiveDocument!;

        Select(sheet, "Nowhere:Part");
        Assert.True(shell.Commands.TryExecute("sch.editSymbol"));
        Settle(() => shell.CurrentBanner is not null);

        Assert.Equal(Tr.T("sch.lib.link.banner.notInTable", "Nowhere", "Part"), shell.CurrentBanner?.Message);
        Assert.Same(sheet, shell.ActiveDocument);
        window.Close();
    });

    private static List<Issue> Drifts(IDocument sheet) =>
        [.. sheet.Issues.Where(i => i.Title == Tr.T("sch.issue.libraryDrift.title"))];

    /// <summary>Selects the part placed from a definition — through the editor, as a click on it would.</summary>
    private static void Select(IDocument sheet, string libId)
    {
        var editor = sheet.GetType().GetProperty("Editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sheet)!;
        var theSheet = editor.GetType().GetProperty("Sheet")!.GetValue(editor)!;
        var symbols = (System.Collections.IEnumerable)theSheet.GetType().GetProperty("Symbols")!.GetValue(theSheet)!;
        var part = symbols.Cast<object>().Single(s => (string?)s.GetType().GetProperty("LibId")!.GetValue(s) == libId);

        var setSelection = editor.GetType().GetMethod("SetSelection")!;
        var chosen = Array.CreateInstance(setSelection.GetParameters()[0].ParameterType.GetGenericArguments()[0], 1);
        chosen.SetValue(part, 0);
        setSelection.Invoke(editor, [chosen]);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A project whose table names Mine, a library of Amp in two units, and a sheet with Amp placed as its unit B —
    /// its copy the library's — and a part from a library nobody names.
    /// </summary>
    private static string Project(string folder, out string library)
    {
        File.WriteAllText(Path.Combine(folder, "link.kicad_pro"), "{}");
        File.WriteAllText(Path.Combine(folder, "sym-lib-table"),
            "(sym_lib_table\n\t(version 7)\n\t(lib (name \"Mine\")(type \"KiCad\")(uri \"${KIPRJMOD}/mine.kicad_sym\")(options \"\")(descr \"\"))\n)\n");

        library = Path.Combine(folder, "mine.kicad_sym");
        File.WriteAllText(library, $"(kicad_symbol_lib (version 20241209) (generator \"anode\")\n{Amp("Amp")})\n");

        string sheet = Path.Combine(folder, "link.kicad_sch");
        File.WriteAllText(sheet, $"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "{Root}") (paper "A4")
            	(lib_symbols
            {Amp("Mine:Amp")}
            		(symbol "Nowhere:Part" (property "Reference" "U" (at 0 0 0) (effects (font (size 1.27 1.27))))
            			(symbol "Part_1_1" (rectangle (start -2.54 2.54) (end 2.54 -2.54) (stroke (width 0) (type default)) (fill (type none))))))
            	(symbol (lib_id "Mine:Amp") (at 100 80 0) (unit 2) (in_bom yes) (on_board yes) (uuid "0a1b2c3d-0000-4000-8000-000000000001")
            		(property "Reference" "U1" (at 100 75 0))
            		(property "Value" "Amp" (at 100 85 0))
            		(instances (project "link" (path "/{Root}" (reference "U1") (unit 2)))))
            	(symbol (lib_id "Nowhere:Part") (at 150 80 0) (unit 1) (in_bom yes) (on_board yes) (uuid "0a1b2c3d-0000-4000-8000-000000000002")
            		(property "Reference" "U2" (at 150 75 0))
            		(property "Value" "Part" (at 150 85 0))
            		(instances (project "link" (path "/{Root}" (reference "U2") (unit 1)))))
            	(sheet_instances (path "/" (page "1")))
            	(embedded_fonts no))

            """);

        return sheet;

        static string Amp(string name) => $"""
            	(symbol "{name}" (pin_names (offset 0.508)) (in_bom yes) (on_board yes)
            		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "Amp" (at 0 -5.08 0) (effects (font (size 1.27 1.27))))
            		(property "Description" "Amplifier" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
            		(symbol "Amp_1_1" (pin input line (at -5.08 0 0) (length 2.54) (name "IN" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27))))))
            		(symbol "Amp_2_1" (pin output line (at 5.08 0 180) (length 2.54) (name "OUT" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))))
            """;
    }

    private static (ShellViewModel Shell, Window Window) Open(string sheet)
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var recents = new RecentProjectsStore(Path.Combine(Path.GetDirectoryName(sheet)!, "recents.json"));
        var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot, recents);
        var window = new MainWindow { DataContext = shell, Width = 1240, Height = 772 };
        window.Show();

        Assert.NotNull(Pump(shell.OpenAsync(sheet)));
        Dispatcher.UIThread.RunJobs();
        return (shell, window);
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

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("anode-link-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
