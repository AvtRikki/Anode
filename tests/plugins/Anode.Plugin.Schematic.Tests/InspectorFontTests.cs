using Anode.Render.Fonts;
using Anode.Sdk;

using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// The inspector's type block: which face a text is set in and whether it is bold or italic, chosen from a list and
/// written through to the file. The stroke font is the first choice, as a text that names no face is drawn with it.
/// </summary>
public class InspectorFontTests
{
    private const string Sheet = """
        (kicad_sch (version 20250114) (generator "eeschema") (uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10") (paper "A4")
        	(text "Anode" (exclude_from_sim no) (at 50 50 0)
        		(effects (font (size 2.54 2.54)))
        		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01"))
        	(label "NET" (at 60 60 0)
        		(effects (font (size 1.27 1.27)) (justify left bottom))
        		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a02")))
        """;

    private static string Stroke => Tr.T("sch.property.strokeFont");

    private static List<InspectorRow> Rows(Anode.Kicad.SchItem item) =>
        [.. SchItemProperties.Blocks(item, (_, mutate) => mutate()).SelectMany(b => b.Rows)];

    [Fact]
    public void A_text_says_which_face_it_is_set_in_and_offers_the_others()
    {
        var sheet = KicadSchematic.Parse(Sheet);
        var row = Assert.Single(Rows(sheet.Texts[0]), r => r.Name == Tr.T("sch.property.font"));

        Assert.Equal(Stroke, row.Value);
        Assert.NotNull(row.Commit);
        Assert.Equal(Stroke, row.Choices![0]);
        Assert.Equal(OutlineText.Families().Count + 1, row.Choices.Count);
    }

    [Fact]
    public void Choosing_a_face_and_a_style_reaches_the_file()
    {
        var sheet = KicadSchematic.Parse(Sheet);
        var text = sheet.Texts[0];
        string face = OutlineText.Families()[0];

        Assert.Single(Rows(text), r => r.Name == Tr.T("sch.property.font")).Commit!(face);
        Assert.Single(Rows(text), r => r.Name == Tr.T("sch.property.style")).Commit!(Tr.T("sch.style.boldItalic"));

        Assert.Equal(new Anode.Kicad.TextFont(face, true, true, null), text.Font);
        Assert.Equal(Tr.T("sch.style.boldItalic"), Assert.Single(Rows(text), r => r.Name == Tr.T("sch.property.style")).Value);

        // Back to the stroke font, and the file says nothing about a face again.
        Assert.Single(Rows(text), r => r.Name == Tr.T("sch.property.font")).Commit!(Stroke);
        Assert.Null(text.Font.Face);
        Assert.DoesNotContain("(face", sheet.Document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_is_set_the_same_way()
    {
        var sheet = KicadSchematic.Parse(Sheet);
        var label = sheet.Labels[0];

        Assert.Single(Rows(label), r => r.Name == Tr.T("sch.property.style")).Commit!(Tr.T("sch.style.italic"));

        Assert.True(label.Font.Italic);
        Assert.False(label.Font.Bold);
    }
}
