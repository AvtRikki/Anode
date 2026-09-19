using Anode.Render;
using Anode.Sdk;
using Anode.Tests;

// Inside this namespace "Schematic" names the plugin's own namespace segment, not the sheet.
using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// The title block edited from the inspector's overview of the sheet: the row writes the file, the overview reads
/// the new value back, and one undo takes it away again.
/// </summary>
public class TitleBlockOverviewTests
{
    [Fact]
    public void A_title_block_field_is_written_from_the_overview_and_undone()
    {
        Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason);
        using var strings = Tr.Register(JsonTextCatalog.FromAssembly(typeof(SchematicDocument).Assembly));

        var sheet = KicadSchematic.Load(TestData.AnySchematic()!);
        string data = Directory.CreateTempSubdirectory("anode-title-").FullName;
        using var document = new SchematicDocument(sheet, SchematicSceneBuilder.Build(sheet), TestData.AnySchematic()!, new SymbolLibraryList(data));
        byte[] original = sheet.Document.ToBytes();

        InspectorRow Revision() => document.Overview!.Blocks.SelectMany(b => b.Rows).Single(r => r.Name == Tr.T("sch.overview.revision"));

        string before = Revision().Value;
        Assert.NotNull(Revision().Commit);

        Revision().Commit!("7");

        Assert.Equal("7", sheet.TitleBlock.Revision);
        Assert.True(document.IsDirty);
        Assert.Equal("7", Revision().Value);

        document.Editor.Undo();

        Assert.Equal(before, Revision().Value);
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void Every_field_is_offered_even_when_empty()
    {
        Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason);
        using var strings = Tr.Register(JsonTextCatalog.FromAssembly(typeof(SchematicDocument).Assembly));

        var sheet = KicadSchematic.Parse("""
            (kicad_sch
            	(version 20260206)
            	(generator "anode")
            	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
            	(paper "A4")
            	(lib_symbols)
            )
            """);
        string data = Directory.CreateTempSubdirectory("anode-title-").FullName;
        using var document = new SchematicDocument(sheet, SchematicSceneBuilder.Build(sheet), Path.Combine(data, "empty.kicad_sch"), new SymbolLibraryList(data));

        var writable = document.Overview!.Blocks[0].Rows.Where(r => r.Commit is not null).Select(r => r.Name).ToList();

        Assert.Equal(
            [
                Tr.T("sch.overview.title"), Tr.T("sch.overview.revision"), Tr.T("sch.overview.date"), Tr.T("sch.overview.company"),
                Tr.T("sch.overview.comment", 1), Tr.T("sch.overview.comment", 2), Tr.T("sch.overview.comment", 3), Tr.T("sch.overview.comment", 4),
            ],
            writable);
    }

    [Fact]
    public void A_comment_is_written_from_the_overview_and_a_fifth_shows_only_once_it_exists()
    {
        Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason);
        using var strings = Tr.Register(JsonTextCatalog.FromAssembly(typeof(SchematicDocument).Assembly));

        var sheet = KicadSchematic.Load(TestData.AnySchematic()!);
        string data = Directory.CreateTempSubdirectory("anode-title-").FullName;
        using var document = new SchematicDocument(sheet, SchematicSceneBuilder.Build(sheet), TestData.AnySchematic()!, new SymbolLibraryList(data));
        byte[] original = sheet.Document.ToBytes();

        InspectorRow? Comment(int n) =>
            document.Overview!.Blocks[0].Rows.SingleOrDefault(r => r.Name == Tr.T("sch.overview.comment", n));

        Comment(2)!.Commit!("Checked by the bench");

        Assert.Equal("Checked by the bench", sheet.TitleBlock.Comment(2));
        Assert.Equal("Checked by the bench", Comment(2)!.Value);
        Assert.Null(Comment(5));

        // KiCad keeps nine; a fifth in the file is offered for editing like the rest.
        document.EditTitleBlock("comment5", "Fifth");
        Assert.Equal("Fifth", Comment(5)!.Value);

        document.Editor.Undo();
        document.Editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }
}
