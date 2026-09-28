using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// A sheet and the library its parts came from. A part in step with its library adds nothing to the inspector or the
/// issues; one whose library changed says so, in what changed, and is brought back in step with one step to undo.
/// A part derived from another follows the body it is derived from.
/// </summary>
public sealed class LibraryLinkTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-link-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string LibraryPath => Path.Combine(_folder, "mine.kicad_sym");

    [Fact]
    public async Task A_part_in_step_with_its_library_says_nothing()
    {
        using var document = await Open();

        Assert.DoesNotContain(document.Issues, i => i.Title == Tr("sch.issue.libraryDrift.title"));

        document.Editor.SetSelection([Placed(document, "Mine:Amp")]);
        var selected = document.Selection!;
        Assert.DoesNotContain(selected.Blocks, b => b.Title == Tr("sch.block.library"));
        Assert.Contains(selected.Actions, a => a.Label == Tr("sch.action.editSymbol"));
        Assert.DoesNotContain(selected.Actions, a => a.Label == Tr("sch.action.updateFromLibrary"));
    }

    /// <summary>
    /// A pin lengthened in the library and saved: the sheet opened afterwards tells both parts that draw that body —
    /// the part itself and the one derived from it — and the issue's own button takes it, one step to undo.
    /// </summary>
    [Fact]
    public async Task A_pin_changed_in_the_library_is_told_and_taken_in_one_step()
    {
        using (var sheet = await Open())
        {
            // The sheet is written first; the library is changed after it.
        }

        await LengthenOutput();

        using var document = await Open(write: false);
        var issues = Drifts(document);
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Contains(Tr("sch.drift.pins", Tr("sch.drift.changed", 1)), i.Detail, StringComparison.Ordinal));

        // The inspector says the same, and offers the update first.
        var amp = Placed(document, "Mine:Amp");
        document.Editor.SetSelection([amp]);
        var block = document.Selection!.Blocks[0];
        Assert.Equal(Tr("sch.block.library"), block.Title);
        Assert.True(block.IsAlert);
        Assert.Contains(document.Selection.Actions, a => a.Label == Tr("sch.action.updateFromLibrary") && a.IsPrimary);

        byte[] before = document.Sheet.Document.ToBytes();
        issues.Single(i => i.Location == "Mine:Amp").Action!();

        Assert.Contains("(length 5.08)", document.Sheet.Root.Find("lib_symbols")!.ToString(), StringComparison.Ordinal);
        Assert.Equal(["Mine:Buf"], Drifts(document).Select(i => i.Location));

        document.Editor.Undo();
        Assert.Equal(before, document.Sheet.Document.ToBytes());
        Assert.Equal(2, Drifts(document).Count);
    }

    /// <summary>A library that no table names is said to be missing, and the part is not called different for it.</summary>
    [Fact]
    public async Task A_part_whose_library_is_not_in_the_tables_is_said_so()
    {
        using var document = await Open();
        File.Delete(Path.Combine(_folder, "sym-lib-table"));
        using var reopened = await Open(write: false);

        reopened.Editor.SetSelection([Placed(reopened, "Mine:Amp")]);
        var block = reopened.Selection!.Blocks.Single(b => b.Title == Tr("sch.block.library"));
        Assert.Equal(Tr("sch.lib.link.short.notInTable", "Mine", "Amp"), block.Rows.Single().Value);
        Assert.Empty(Drifts(reopened));
    }

    private static List<Anode.Sdk.Issue> Drifts(SchematicDocument document) =>
        [.. document.Issues.Where(i => i.Title == Tr("sch.issue.libraryDrift.title"))];

    private static SymbolInstance Placed(SchematicDocument document, string libId) =>
        document.Sheet.Symbols.Single(s => s.LibId == libId);

    /// <summary>The library's output pin — on Amp's unit B — made longer, and the library saved.</summary>
    private async Task LengthenOutput()
    {
        using var library = new SymbolLibraryDocument(LibraryPath, SymbolLibrary.Load(LibraryPath));
        library.Show("Amp");
        library.ShowUnit(2);
        var pin = library.Body!.PinsOf(2, 1).Single();
        library.History.Execute(new ModifyNodesCommand("length", [pin], () => SymbolWrites.SetPinLength(pin, 5_080_000)));
        Assert.True(await library.SaveAsync());
    }

    /// <summary>
    /// A project whose table names one library, Mine, of Amp — two units — and Buf, derived from it; and a sheet
    /// with an Amp placed as its unit B and a Buf, their copies taken from the library as it is.
    /// </summary>
    private async Task<SchematicDocument> Open(bool write = true)
    {
        string path = Path.Combine(_folder, "link.kicad_sch");
        if (write)
        {
            File.WriteAllText(Path.Combine(_folder, "link.kicad_pro"), "{}");
            File.WriteAllText(Path.Combine(_folder, "sym-lib-table"),
                "(sym_lib_table\n\t(version 7)\n\t(lib (name \"Mine\")(type \"KiCad\")(uri \"${KIPRJMOD}/mine.kicad_sym\")(options \"\")(descr \"\"))\n)\n");
            File.WriteAllText(LibraryPath, Library);
            WriteSheet(path);
        }

        var sheet = KicadSchematic.Load(path);
        var document = new SchematicDocument(sheet, SchematicSceneBuilder.Build(sheet), path, new SymbolLibraryList(Path.Combine(_folder, "data")));

        // The libraries are read off the interface's thread the first time the sheet is asked about them.
        _ = document.Issues;
        await document.LibrariesRead;
        return document;
    }

    private void WriteSheet(string path)
    {
        var library = SymbolLibrary.Load(LibraryPath);
        var sheet = KicadSchematic.Parse(
            "(kicad_sch (version 20250114) (generator \"eeschema\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b11\") (paper \"A4\") (lib_symbols) (embedded_fonts no))");
        foreach (var (name, x, unit) in new[] { ("Amp", 50, 2), ("Buf", 80, 1) })
        {
            var definition = library.Flatten(library.Find(name)!);
            SchSymbols.Ensure(sheet, "Mine:" + name, definition);
            var placed = SchSymbols.Place(sheet, "Mine:" + name, definition, new Vector2L(x * 1_000_000L, 50_000_000L), "U" + x, "link", SchSymbols.PathOf(sheet), unit: unit);
            new AddNodesCommand(sheet, [placed]).Apply();
        }

        sheet.Document.Save(path);
    }

    private const string Library = """
        (kicad_symbol_lib (version 20241209) (generator "anode")
        	(symbol "Amp" (pin_names (offset 0.508)) (in_bom yes) (on_board yes)
        		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Value" "Amp" (at 0 -5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Description" "Amplifier" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))
        		(symbol "Amp_1_1" (pin input line (at -5.08 0 0) (length 2.54) (name "IN" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27))))))
        		(symbol "Amp_2_1" (pin output line (at 5.08 0 180) (length 2.54) (name "OUT" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))))
        	(symbol "Buf" (extends "Amp")
        		(property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Value" "Buf" (at 0 -5.08 0) (effects (font (size 1.27 1.27))))
        		(property "Description" "" (at 0 0 0) (effects (font (size 1.27 1.27)) (hide yes)))))
        """;

    private static string Tr(string key, params object[] args) => Anode.Sdk.Tr.T(key, args);
}
