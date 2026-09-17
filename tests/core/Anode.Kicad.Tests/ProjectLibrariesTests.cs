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
}
