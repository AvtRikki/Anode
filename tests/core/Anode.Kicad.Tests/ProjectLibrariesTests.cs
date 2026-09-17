namespace Anode.Kicad.Tests;

/// <summary>
/// Which libraries a sheet can draw from. The machine reading a design may have no KiCad installed, so what matters
/// most here is that a library lying beside the design is found without any table telling us to look.
/// </summary>
public class ProjectLibrariesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-project-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Library(string file, string symbol) => File.WriteAllText(
        Path.Combine(_folder, file),
        $"(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n\t(symbol \"{symbol}\")\n)");

    private string Sheet()
    {
        File.WriteAllText(Path.Combine(_folder, "project.kicad_pro"), "{}");
        string sheet = Path.Combine(_folder, "project.kicad_sch");
        File.WriteAllText(sheet, "(kicad_sch (version 20260206) (paper \"A4\") (lib_symbols))");
        return sheet;
    }

    [Fact]
    public void The_project_folder_is_the_one_holding_the_project_file()
    {
        string sheet = Sheet();
        string nested = Directory.CreateDirectory(Path.Combine(_folder, "sheets")).FullName;
        string child = Path.Combine(nested, "child.kicad_sch");
        File.WriteAllText(child, "(kicad_sch)");

        Assert.Equal(_folder, ProjectLibraries.ProjectFolder(sheet));

        // A sheet in a subfolder still belongs to the project above it.
        Assert.Equal(_folder, ProjectLibraries.ProjectFolder(child));
        Assert.Null(ProjectLibraries.ProjectFolder(null));
    }

    [Fact]
    public void A_library_lying_beside_the_design_is_offered_without_a_table()
    {
        string sheet = Sheet();
        Library("parts.kicad_sym", "R");

        var index = ProjectLibraries.For(sheet);

        // Nicknamed by its file, which is how KiCad nicknames one by default.
        Assert.Contains("parts", index.Libraries.Where(l => l.IsProject).Select(l => l.Nickname));
        Assert.Equal("R", index.Find("parts:R")?.Name);
    }

    [Fact]
    public void A_table_beside_the_project_is_read_and_its_nicknames_kept()
    {
        string sheet = Sheet();
        Library("parts.kicad_sym", "R");
        File.WriteAllText(
            Path.Combine(_folder, "sym-lib-table"),
            "(sym_lib_table\n\t(version 7)\n\t(lib (name \"Own\")(type \"KiCad\")(uri \"${KIPRJMOD}/parts.kicad_sym\")(options \"\")(descr \"\"))\n)");

        var index = ProjectLibraries.For(sheet);

        // The table named it Own, so it is not offered a second time under its file name.
        Assert.Equal("R", index.Find("Own:R")?.Name);
        Assert.DoesNotContain("parts", index.Libraries.Select(l => l.Nickname));
    }

    [Fact]
    public void A_project_with_no_libraries_of_its_own_offers_none()
    {
        string sheet = Sheet();

        var index = ProjectLibraries.For(sheet);

        Assert.DoesNotContain(index.Libraries, l => l.IsProject);
    }

    [Fact]
    public void A_library_this_application_was_told_to_remember_is_offered_too()
    {
        string sheet = Sheet();
        string elsewhere = Directory.CreateTempSubdirectory("anode-elsewhere-").FullName;
        string library = Path.Combine(elsewhere, "remembered.kicad_sym");
        File.WriteAllText(library, "(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n\t(symbol \"Part\")\n)");

        try
        {
            var index = ProjectLibraries.For(sheet, [library]);

            Assert.Equal("Part", index.Find("remembered:Part")?.Name);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void A_remembered_library_that_is_gone_is_simply_not_listed()
    {
        string sheet = Sheet();

        var index = ProjectLibraries.For(sheet, [Path.Combine(_folder, "absent.kicad_sym")]);

        Assert.DoesNotContain("absent", index.Libraries.Select(l => l.Nickname));
    }

    [Fact]
    public void Adding_a_library_writes_a_table_when_the_project_has_none()
    {
        Sheet();
        Library("parts.kicad_sym", "R");

        string? nickname = ProjectLibraries.AddToProjectTable(_folder, Path.Combine(_folder, "parts.kicad_sym"));

        Assert.Equal("parts", nickname);

        string table = Path.Combine(_folder, "sym-lib-table");
        Assert.True(File.Exists(table));

        // Written as KiCad would read it, and relative to the project so the folder can be moved whole.
        var read = SymLibTable.Load(table);
        Assert.Equal("parts", read.Entries.Single().Name);
        Assert.Equal("${KIPRJMOD}/parts.kicad_sym", read.Entries.Single().Uri);
    }

    [Fact]
    public void Adding_a_library_keeps_every_row_already_in_the_table()
    {
        Sheet();
        Library("parts.kicad_sym", "R");
        Library("more.kicad_sym", "C");

        string original = "(sym_lib_table\n\t(version 7)\n\t(lib (name \"Own\")(type \"KiCad\")(uri \"${KIPRJMOD}/parts.kicad_sym\")(options \"\")(descr \"kept\"))\n)";
        File.WriteAllText(Path.Combine(_folder, "sym-lib-table"), original);

        ProjectLibraries.AddToProjectTable(_folder, Path.Combine(_folder, "more.kicad_sym"));

        var read = SymLibTable.Load(Path.Combine(_folder, "sym-lib-table"));
        Assert.Equal(["Own", "more"], read.Entries.Select(e => e.Name));

        // The row that was already there is untouched, description and all.
        Assert.Equal("kept", read.Entries[0].Description);
    }

    [Fact]
    public void Adding_the_same_library_twice_changes_nothing()
    {
        Sheet();
        Library("parts.kicad_sym", "R");
        string library = Path.Combine(_folder, "parts.kicad_sym");

        Assert.Equal("parts", ProjectLibraries.AddToProjectTable(_folder, library));

        string once = File.ReadAllText(Path.Combine(_folder, "sym-lib-table"));

        Assert.Null(ProjectLibraries.AddToProjectTable(_folder, library));
        Assert.Equal(once, File.ReadAllText(Path.Combine(_folder, "sym-lib-table")));
    }

    [Fact]
    public void A_second_library_of_the_same_file_name_gets_a_name_of_its_own()
    {
        Sheet();
        Library("parts.kicad_sym", "R");

        string other = Directory.CreateTempSubdirectory("anode-other-").FullName;
        string twin = Path.Combine(other, "parts.kicad_sym");
        File.WriteAllText(twin, "(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n\t(symbol \"C\")\n)");

        try
        {
            Assert.Equal("parts", ProjectLibraries.AddToProjectTable(_folder, Path.Combine(_folder, "parts.kicad_sym")));

            // Same file name, different file: it cannot take the nickname that is already spoken for.
            Assert.Equal("parts-2", ProjectLibraries.AddToProjectTable(_folder, twin));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }
}
