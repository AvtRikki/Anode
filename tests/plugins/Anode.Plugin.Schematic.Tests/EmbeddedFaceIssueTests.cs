using Anode.Kicad;
using Anode.Render;
using Anode.Sdk;
using Anode.Tests;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// A sheet opened as the plugin opens it: a face the file carries is not reported missing, while one neither the
/// file nor this machine has still is.
/// </summary>
public class EmbeddedFaceIssueTests
{
    [Fact]
    public async Task A_face_the_file_carries_is_not_reported_missing()
    {
        string font = TestData.FullPath("qa/resources/fonts/NotoSans-Regular.ttf");
        Assert.SkipUnless(File.Exists(font), TestData.SkipReason);

        string folder = Directory.CreateTempSubdirectory("anode-embedded-").FullName;
        string path = Path.Combine(folder, "embedded.kicad_sch");
        File.WriteAllText(path, $$"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
            	(text "Carried" (exclude_from_sim no) (at 50 50 0)
            		(effects (font (face "Noto Sans") (size 2.54 2.54)))
            		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01"))
            	(text "Missing" (exclude_from_sim no) (at 50 70 0)
            		(effects (font (face "Missing Face Anode") (size 2.54 2.54)))
            		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a02"))
            	(embedded_fonts yes)
            	{{EmbeddedFile.Block("NotoSans-Regular.ttf", "font", File.ReadAllBytes(font))}})

            """);

        try
        {
            var type = new SchematicDocumentType(new QuietLog(), new SymbolLibraryList(folder));
            using var document = (SchematicDocument)await type.OpenAsync(path, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(document.Issues, i => i.Title == Tr.T("sch.issue.face.title", "Noto Sans"));
            Assert.Contains(document.Issues, i => i.Title == Tr.T("sch.issue.face.title", "Missing Face Anode"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task The_overview_switch_asks_for_fonts_and_a_save_carries_them()
    {
        string font = TestData.FullPath("qa/resources/fonts/NotoSans-Regular.ttf");
        Assert.SkipUnless(File.Exists(font), TestData.SkipReason);
        byte[] data = File.ReadAllBytes(font);

        // Noto Sans known to this process from another file, as KiCad knows a font once any open file carried it.
        Anode.Render.Fonts.OutlineText.Embed(EmbeddedFile.In(Anode.Kicad.Schematic.Parse(
            $"(kicad_sch (version 20250114) (generator \"eeschema\") {EmbeddedFile.Block("NotoSans-Regular.ttf", "font", data)})").Document.Root));

        string folder = Directory.CreateTempSubdirectory("anode-embed-save-").FullName;
        string path = Path.Combine(folder, "sheet.kicad_sch");
        const string text = """
            (kicad_sch
            	(version 20250114)
            	(generator "eeschema")
            	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
            	(paper "A4")
            	(text "Noto"
            		(exclude_from_sim no)
            		(at 50 50 0)
            		(effects
            			(font
            				(face "Noto Sans")
            				(size 2.54 2.54)
            			)
            		)
            		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01")
            	)
            	(embedded_fonts no)
            )

            """;
        File.WriteAllText(path, text);

        try
        {
            var type = new SchematicDocumentType(new QuietLog(), new SymbolLibraryList(folder));
            using (var document = (SchematicDocument)await type.OpenAsync(path, TestContext.Current.CancellationToken))
            {
                InspectorRow Switch() => document.Overview!.Blocks.Single(b => b.Title == Tr.T("sch.overview.fonts.title"))
                    .Rows.Single(r => r.Switch is not null);

                Assert.False(Switch().Switch);
                Assert.Contains(document.Overview!.Blocks.SelectMany(b => b.Rows), r => r.Name == "Noto Sans" && r.Value == Tr.T("sch.overview.fonts.installed"));

                // One undoable step, and saving with it off writes the file back as it was.
                Switch().Commit!("yes");
                Assert.True(Switch().Switch);
                Assert.True(document.IsDirty);
                document.Editor.Undo();
                Assert.False(Switch().Switch);
                await document.SaveAsync();
                Assert.Equal(text, File.ReadAllText(path));

                Switch().Commit!("yes");
                await document.SaveAsync();
                Assert.False(document.IsDirty);
                Assert.Contains(document.Overview!.Blocks.SelectMany(b => b.Rows), r => r.Name == "Noto Sans" && r.Value == Tr.T("sch.overview.fonts.carried"));
            }

            string saved = File.ReadAllText(path);
            Assert.Contains("(embedded_fonts yes)", saved, StringComparison.Ordinal);
            var carried = Assert.Single(EmbeddedFile.In(Anode.Kicad.Schematic.Parse(saved).Document.Root));
            Assert.Equal("NotoSans-Regular.ttf", carried.Name);
            Assert.Equal(data, carried.Data);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A drawing sheet the design carries: KiCad keeps it in the root sheet's file, and a sheet below the root draws
    /// with it too, the project naming it <c>kicad-embed://…</c>.
    /// </summary>
    [Fact]
    public async Task A_sheet_below_the_root_draws_the_template_the_root_carries()
    {
        string template = TestData.FullPath("demos/vme-wren/cern-ohl-left.kicad_wks");
        Assert.SkipUnless(File.Exists(template), TestData.SkipReason);

        string folder = Directory.CreateTempSubdirectory("anode-embedded-wks-").FullName;
        string root = Path.Combine(folder, "design.kicad_sch");
        string child = Path.Combine(folder, "child.kicad_sch");
        File.WriteAllText(Path.Combine(folder, "design.kicad_pro"),
            """{ "schematic": { "page_layout_descr_file": "kicad-embed://cern-ohl-left.kicad_wks" }, "text_variables": {} }""");
        File.WriteAllText(root, $$"""
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
            	(sheet (at 50 50) (size 30 20) (uuid "2b6c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a09")
            		(property "Sheetname" "Child" (at 50 49 0))
            		(property "Sheetfile" "child.kicad_sch" (at 50 71 0)))
            	(embedded_fonts no)
            	{{EmbeddedFile.Block("cern-ohl-left.kicad_wks", "worksheet", File.ReadAllBytes(template))}})

            """);
        File.WriteAllText(child, """
            (kicad_sch (version 20250114) (generator "eeschema") (uuid "7f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b11") (paper "A4")
            	(embedded_fonts no)
            )

            """);

        try
        {
            var type = new SchematicDocumentType(new QuietLog(), new SymbolLibraryList(folder));
            using var below = (SchematicDocument)await type.OpenAsync(child, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(below.Issues, i => i.Title == Tr.T("sch.issue.drawingSheet.title"));

            // The carried template, not KiCad's default: its own frame, drawn from the root's file.
            int carried = below.Scene.Layers.Single(l => l.Name == Anode.Render.LayerStyle.Sch.Frame).Lines.Count;
            var plain = SchematicSceneBuilder.Build(Anode.Kicad.Schematic.Load(child));
            Assert.NotEqual(plain.Layers.Single(l => l.Name == Anode.Render.LayerStyle.Sch.Frame).Lines.Count, carried);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class QuietLog : ILog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
