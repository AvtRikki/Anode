using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;

namespace Anode.Editing.Tests;

/// <summary>
/// Editing a sheet through the editor. The rule that matters here is quiet but visible: drawing does not select what
/// it drew. A selection with no preview dims everything else, so a tool that selected each committed leg made the
/// whole sheet blink between the click and the next rubber band.
/// </summary>
public class SchematicEditorTests
{
    private const string Text = """
        (kicad_sch
        	(version 20260206)
        	(generator "anode")
        	(uuid "6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10")
        	(paper "A4")
        	(lib_symbols)
        	(wire
        		(pts
        			(xy 50.8 50.8) (xy 63.5 50.8)
        		)
        		(stroke
        			(width 0)
        			(type default)
        		)
        		(uuid "0a1b2c3d-0000-4000-8000-000000000001")
        	)
        	(sheet_instances
        		(path "/"
        			(page "1")
        		)
        	)
        	(embedded_fonts no)
        )
        """;

    private static readonly Vector2L A = new(76_200_000, 50_800_000);
    private static readonly Vector2L B = new(76_200_000, 38_100_000);

    private static SchematicEditor Editor(out Schematic sheet)
    {
        sheet = Schematic.Parse(Text);
        return new SchematicEditor(SchematicSceneBuilder.Build(sheet));
    }

    [Fact]
    public void Drawing_does_not_select_what_it_drew()
    {
        var editor = Editor(out var sheet);
        Assert.Empty(editor.Selection);

        editor.Add([SchNodes.Wire([A, B])]);

        Assert.Equal(2, sheet.Wires.Count);
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void Adding_can_select_when_that_is_what_is_wanted()
    {
        var editor = Editor(out _);

        var wire = SchNodes.Wire([A, B]);
        editor.Add([wire], select: true);

        Assert.Same(wire, Assert.Single(editor.Selection));
    }

    [Fact]
    public void What_was_drawn_is_on_the_sheet_and_undo_takes_it_off()
    {
        var editor = Editor(out var sheet);
        byte[] original = sheet.Document.ToBytes();

        editor.Add([SchNodes.Wire([A, B])]);
        Assert.True(editor.History.CanUndo);
        Assert.NotEqual(original, sheet.Document.ToBytes());

        editor.Undo();

        Assert.Single(sheet.Wires);
        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void A_drawn_wire_is_in_the_scene_and_leaves_it_again()
    {
        var editor = Editor(out _);
        int before = editor.Scene.TopLevelItems.Count();

        editor.Add([SchNodes.Wire([A, B])]);
        Assert.Equal(before + 1, editor.Scene.TopLevelItems.Count());

        editor.Undo();
        Assert.Equal(before, editor.Scene.TopLevelItems.Count());
    }
}
