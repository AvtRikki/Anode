using System.Text;
using Anode.Geometry;
using Anode.Kicad.Editing;

namespace Anode.Kicad.Tests;

/// <summary>
/// Writing a value back into a sheet — what the inspector does when a field is committed. The invariant that matters
/// is the one the whole editor rests on: a value that is changed and then undone leaves the file byte for byte as it
/// was, including the parts we never taught the app to read.
/// </summary>
public class SchWritesTests
{
    private const string Text = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(label "VCC"
        		(at 50.8 44.45 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000001")
        	)
        	(text "a note"
        		(at 76.2 44.45 0)
        		(effects
        			(font
        				(size 1.27 1.27)
        			)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000002")
        	)
        	(symbol
        		(lib_id "Device:R")
        		(at 101.6 50.8 0)
        		(unit 1)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000003")
        		(property "Reference" "R1"
        			(at 101.6 48.26 0)
        		)
        		(property "Value" "10k"
        			(at 101.6 53.34 0)
        		)
        	)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    private static Schematic Sheet() => Schematic.Parse(Text);

    [Fact]
    public void The_words_of_a_label_can_be_rewritten()
    {
        var sheet = Sheet();
        var label = sheet.Labels[0];

        SchWrites.SetText(label, "+3V3");

        Assert.Equal("+3V3", label.Text);
    }

    [Fact]
    public void Free_text_is_rewritten_the_same_way()
    {
        var sheet = Sheet();
        var note = sheet.Texts[0];

        SchWrites.SetText(note, "13V rail");

        Assert.Equal("13V rail", note.Text);
    }

    [Fact]
    public void A_symbol_field_is_written_by_name()
    {
        var sheet = Sheet();
        var symbol = sheet.Symbols[0];

        SchWrites.SetField(symbol, "Reference", "R7");
        SchWrites.SetField(symbol, "Value", "4k7");

        Assert.Equal("R7", symbol.Reference);
        Assert.Equal("4k7", symbol.Value);
    }

    [Fact]
    public void A_field_the_item_does_not_have_is_refused()
    {
        var sheet = Sheet();

        Assert.Throws<KiCadFormatException>(() => SchWrites.SetField(sheet.Symbols[0], "Footprint", "R_0402"));
    }

    [Fact]
    public void An_item_with_no_words_refuses_to_be_given_any()
    {
        var sheet = Sheet();

        Assert.Throws<NotSupportedException>(() => SchWrites.SetText(sheet.Symbols[0], "nonsense"));
    }

    [Fact]
    public void Position_and_angle_are_written_where_the_item_stands()
    {
        var sheet = Sheet();
        var label = sheet.Labels[0];

        SchWrites.SetPosition(label, new Vector2L(63_500_000, 38_100_000));
        SchWrites.SetAngle(label, 90);

        Assert.Equal(new Vector2L(63_500_000, 38_100_000), label.Position);
        Assert.Equal(90, label.Angle);

        // An angle is normalised the way KiCad writes it, so turning past a full circle comes back round.
        SchWrites.SetAngle(label, 450);
        Assert.Equal(90, label.Angle);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("field")]
    [InlineData("position")]
    [InlineData("angle")]
    public void A_value_that_is_written_and_undone_gives_the_file_back(string what)
    {
        var sheet = Sheet();
        byte[] original = sheet.Document.ToBytes();
        var label = sheet.Labels[0];
        var symbol = sheet.Symbols[0];

        var history = new UndoStack();
        history.Execute(new ModifyNodesCommand("Edit", what == "field" ? [symbol] : [label], () =>
        {
            switch (what)
            {
                case "text": SchWrites.SetText(label, "+3V3"); break;
                case "field": SchWrites.SetField(symbol, "Value", "4k7"); break;
                case "position": SchWrites.SetPosition(label, new Vector2L(63_500_000, 38_100_000)); break;
                default: SchWrites.SetAngle(label, 90); break;
            }
        }));

        Assert.NotEqual(original, sheet.Document.ToBytes());

        history.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());

        // And redo puts the change back, so the step is a real one in both directions.
        history.Redo();
        Assert.NotEqual(original, sheet.Document.ToBytes());
        history.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void What_was_written_reads_back_as_the_same_tree()
    {
        var sheet = Sheet();
        SchWrites.SetText(sheet.Labels[0], "A\"B");

        string written = Encoding.UTF8.GetString(sheet.Document.ToBytes());

        // A quote inside a name has to survive being written and read again.
        Assert.Equal("A\"B", Schematic.Parse(written).Labels[0].Text);
    }
}
