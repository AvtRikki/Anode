using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Writing how a text is set: the face, bold and italic land in <c>(effects (font …))</c> in KiCad's order, what is
/// turned off is written out of the file rather than written as "no", and undoing gives the file back as it was.
/// </summary>
public class FontWriteTests
{
    private const string Sheet = """
        (kicad_sch
        	(version 20250114)
        	(generator "eeschema")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(text "Anode"
        		(exclude_from_sim no)
        		(at 50 50 0)
        		(effects
        			(font
        				(size 2.54 2.54)
        			)
        			(justify left)
        		)
        		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01")
        	)
        )

        """;

    [Fact]
    public void A_face_and_a_style_are_written_where_KiCad_writes_them()
    {
        var sheet = Schematic.Parse(Sheet);
        var text = sheet.Texts[0];

        FontWrites.SetItalic(text.Node, true);
        FontWrites.SetFace(text.Node, "Noto Sans");
        FontWrites.SetBold(text.Node, true);

        // Face first, then size, then bold before italic — KiCad's own order, whatever order they were set in.
        Assert.Contains("""
            			(font
            				(face "Noto Sans")
            				(size 2.54 2.54)
            				(bold yes)
            				(italic yes)
            			)
            """.TrimEnd('\n'), sheet.Document.ToString(), StringComparison.Ordinal);

        var font = text.Font;
        Assert.Equal("Noto Sans", font.Face);
        Assert.True(font.Bold);
        Assert.True(font.Italic);
    }

    [Fact]
    public void Turning_a_style_off_takes_it_out_of_the_file()
    {
        var sheet = Schematic.Parse(Sheet);
        var text = sheet.Texts[0];

        FontWrites.SetFace(text.Node, "Noto Sans");
        FontWrites.SetBold(text.Node, true);
        FontWrites.SetBold(text.Node, false);
        FontWrites.SetFace(text.Node, null);

        Assert.Equal(Sheet, sheet.Document.ToString());
        Assert.Equal(default, text.Font);
    }

    [Fact]
    public void A_text_with_no_effects_of_its_own_is_given_them()
    {
        var sheet = Schematic.Parse("""
            (kicad_sch
            	(version 20250114)
            	(generator "eeschema")
            	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
            	(paper "A4")
            	(text "Anode"
            		(at 50 50 0)
            		(uuid "0a3c1f5e-7d2b-4c8a-9e61-2f4b8d7c9a01")
            	)
            )

            """);
        var text = sheet.Texts[0];

        FontWrites.SetFace(text.Node, "Noto Sans");

        // Effects before the uuid, with the size KiCad gives a text that had none.
        Assert.Contains("""
            		(effects
            			(font
            				(face "Noto Sans")
            				(size 1.27 1.27)
            			)
            		)
            		(uuid
            """.TrimEnd('\n'), sheet.Document.ToString(), StringComparison.Ordinal);
        Assert.Equal(1_270_000, text.TextHeight);
    }

    [Fact]
    public void An_edit_undone_gives_the_file_back()
    {
        var sheet = Schematic.Parse(Sheet);
        var text = sheet.Texts[0];
        var history = new UndoStack();

        history.Execute(new ModifyNodesCommand("Font", [text], () =>
        {
            FontWrites.SetFace(text.Node, "Noto Sans");
            FontWrites.SetItalic(text.Node, true);
        }));
        Assert.NotEqual(Sheet, sheet.Document.ToString());

        history.Undo();
        Assert.Equal(Sheet, sheet.Document.ToString());
        Assert.Equal(default, text.Font);

        history.Redo();
        Assert.Equal("Noto Sans", text.Font.Face);
    }
}
