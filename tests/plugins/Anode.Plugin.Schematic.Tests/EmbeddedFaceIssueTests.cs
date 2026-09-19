using Anode.Kicad;
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
