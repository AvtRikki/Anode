using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// Editing a library symbol through the same editor a sheet is edited with: what is moved, turned, mirrored or
/// pulled lands in the library's coordinates — Y up — pins point the right way after, deletes and edits are steps on
/// the library's one history, and a derived symbol lends nothing of its parent's body to be changed.
/// </summary>
public sealed class SymbolEditingTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-symedit-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A pin pulled down the screen goes down in the library, which is to say its Y falls.</summary>
    [Fact]
    public void A_pin_moved_down_the_screen_goes_down_in_the_library()
    {
        using var document = Open();
        var editor = document.Editor!;
        var pin = Pin(document);
        var was = pin.Position;

        Move(document, pin, new Vector2L(0, 2_540_000));

        Assert.Equal(new Vector2L(was.X, was.Y - 2_540_000), pin.Position);
        Assert.True(document.IsDirty);
    }

    /// <summary>
    /// Turned a quarter counter-clockwise on screen about where a wire meets it, a pin pointing right points up —
    /// its angle one quarter more, as the library counts angles — and stays where it was.
    /// </summary>
    [Fact]
    public void A_pin_turned_on_screen_turns_the_same_way_in_the_library()
    {
        using var document = Open();
        var editor = document.Editor!;
        var pin = Pin(document);

        editor.SetSelection([pin]);
        editor.Rotate(90);

        Assert.Equal(new Vector2L(-7_620_000, 0), pin.Position);
        Assert.Equal(90, pin.Angle);
    }

    /// <summary>
    /// Two things turned together turn about their middle: the pin on the left of the box, turned a quarter
    /// counter-clockwise on screen, ends up below it, pointing up into it.
    /// </summary>
    [Fact]
    public void Things_turned_together_turn_about_their_middle()
    {
        using var document = Open();
        var editor = document.Editor!;
        var pin = Pin(document);
        var box = document.Current!.Graphics.Single();

        editor.SetSelection([pin, box]);
        editor.Rotate(90);

        Assert.True(pin.Position.Y < Math.Min(box.Start.Y, box.End.Y), "the pin did not end up below the box");
        Assert.Equal(90, pin.Angle);
    }

    [Fact]
    public void A_pin_mirrored_left_for_right_points_the_other_way()
    {
        using var document = Open();
        var editor = document.Editor!;
        var pin = Pin(document);

        editor.SetSelection([pin]);
        editor.Mirror(horizontal: true);

        // About its own point: it stays, and points left instead of right.
        Assert.Equal(new Vector2L(-7_620_000, 0), pin.Position);
        Assert.Equal(180, pin.Angle);
    }

    /// <summary>A field turned a quarter reads the other way, never upside down — how KiCad turns a field.</summary>
    [Fact]
    public void A_field_turned_reads_the_other_way()
    {
        using var document = Open();
        var editor = document.Editor!;
        var reference = document.Current!.Fields.Single(f => f.Name == "Reference");

        editor.SetSelection([reference]);
        editor.Rotate(90);
        Assert.Equal(90, reference.Angle);

        editor.Rotate(90);
        Assert.Equal(0, reference.Angle);
    }

    [Fact]
    public void A_deleted_pin_comes_back_where_it_was_with_one_undo()
    {
        using var document = Open();
        var editor = document.Editor!;
        byte[] original = document.Library.Document.ToBytes();
        var pin = Pin(document);

        editor.SetSelection([pin]);
        editor.DeleteSelection();
        Assert.Empty(document.Current!.Pins);

        document.Undo();
        Assert.Single(document.Current!.Pins);
        Assert.Equal(original, document.Library.Document.ToBytes());
        Assert.False(document.IsDirty);
    }

    /// <summary>Fields are the symbol's own: they move and turn, but a delete leaves them.</summary>
    [Fact]
    public void Fields_are_not_deleted()
    {
        using var document = Open();
        var editor = document.Editor!;
        var value = document.Current!.Fields.Single(f => f.Name == "Value");

        editor.SetSelection([value]);
        editor.DeleteSelection();

        Assert.Contains(value, document.Current!.Fields);
    }

    /// <summary>
    /// The handles of a shape stand where it is drawn — turned over from the library — and pulling one writes the
    /// library's own point: a rectangle's top-right corner dragged up the screen raises the rectangle's top.
    /// </summary>
    [Fact]
    public void A_rectangle_corner_pulled_up_the_screen_raises_its_top()
    {
        using var document = Open();
        var editor = document.Editor!;
        var box = document.Current!.Graphics.Single();

        editor.SetSelection([box]);
        var corner = editor.Handles.Single(h => h.Kind == SchHandleKind.Corner && h.At == new Vector2L(5_080_000, -5_080_000));
        Assert.True(editor.BeginPointEdit(box, corner));
        editor.UpdatePointEdit(editor.Scene.ToSceneMm(new Vector2D(5_080_000, -7_620_000)));
        editor.CommitPointEdit();

        Assert.Equal(7_620_000, Math.Max(box.Start.Y, box.End.Y));
        Assert.Equal(-5_080_000, Math.Min(box.Start.Y, box.End.Y));
    }

    [Fact]
    public void The_inspector_writes_a_pins_type_and_one_undo_takes_it_back()
    {
        using var document = Open();
        var pin = Pin(document);
        document.Editor!.SetSelection([pin]);

        var type = document.Selection!.Blocks.Single().Rows.Single(r => r.Choices == SymbolWrites.PinTypes);
        type.Commit!("power_in");
        Assert.Equal("power_in", pin.ElectricalType);

        var hidden = document.Selection!.Blocks.Single().Rows.Single(r => r.Switch is not null);
        hidden.Commit!("yes");
        Assert.True(pin.IsHidden);
        Assert.Contains("(hide yes)", pin.Node.ToString(), StringComparison.Ordinal);

        document.Undo();
        document.Undo();
        Assert.Equal("input", Pin(document).ElectricalType);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Pin_numbers_hidden_from_the_inspector_are_written_as_KiCad_writes_them()
    {
        using var document = Open();
        var numbers = document.Overview!.Blocks.SelectMany(b => b.Rows).Single(r => r.Name == Anode.Sdk.Tr.T("sch.lib.row.pinNumbers"));

        numbers.Commit!("no");

        Assert.False(document.Current!.ShowPinNumbers);
        Assert.Contains("(pin_numbers", document.Library.Document.ToString(), StringComparison.Ordinal);

        document.Undo();
        Assert.True(document.Current!.ShowPinNumbers);
    }

    /// <summary>A derived symbol's body is its parent's: none of it can be moved, pulled or deleted from here.</summary>
    [Fact]
    public void A_derived_symbol_lends_nothing_of_its_parents_body()
    {
        string library = Path.Combine(TestData.KiCadDir, "qa", "data", "eeschema", "libs", "4xxx.kicad_sym");
        Assert.SkipUnless(File.Exists(library), TestData.SkipReason);
        using var document = new SymbolLibraryDocument(library, SymbolLibrary.Load(library));
        document.Show("14528");
        var editor = document.Editor!;
        var parentPin = document.Body!.PinsOf(1, 1).First();

        editor.SetSelection([parentPin]);
        Assert.False(editor.BeginMove(parentPin, editor.Scene.ToSceneMm(new Vector2D(0, 0))));
        editor.DeleteSelection();
        Assert.Contains(parentPin, document.Body!.Pins);

        // Its own fields are its own.
        var value = document.Current!.Fields.Single(f => f.Name == "Value");
        editor.SetSelection([value]);
        Assert.True(editor.BeginMove(value, editor.Scene.ToSceneMm(new Vector2D(0, 0))));
        editor.CancelMove();
    }

    private static SchPin Pin(SymbolLibraryDocument document) => document.Current!.Pins.Single();

    private static void Move(SymbolLibraryDocument document, SchItem item, Vector2L by)
    {
        var editor = document.Editor!;
        editor.SetSelection([item]);
        var from = editor.Scene.ToSceneMm(new Vector2D(0, 0));
        Assert.True(editor.BeginMove(item, from));
        editor.UpdateMove(editor.Scene.ToSceneMm(new Vector2D(by.X, by.Y)));
        editor.CommitMove();
    }

    /// <summary>
    /// A library of one symbol: a pin on the left at (-7.62, 0) pointing right into a rectangle from (-5.08, 5.08)
    /// to (5.08, -5.08), with the reference above and the value below.
    /// </summary>
    private SymbolLibraryDocument Open()
    {
        string path = Path.Combine(_folder, "one.kicad_sym");
        File.WriteAllText(path, """
            (kicad_symbol_lib
            	(version 20241209)
            	(generator "anode")
            	(symbol "ONE"
            		(pin_names (offset 0.508))
            		(exclude_from_sim no) (in_bom yes) (on_board yes)
            		(property "Reference" "U" (at 0 7.62 0) (effects (font (size 1.27 1.27))))
            		(property "Value" "ONE" (at 0 -7.62 0) (effects (font (size 1.27 1.27))))
            		(symbol "ONE_1_1"
            			(rectangle (start -5.08 5.08) (end 5.08 -5.08) (stroke (width 0.254) (type default)) (fill (type background)))
            			(pin input line (at -7.62 0 0) (length 2.54) (name "IN" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27))))))
            		(embedded_fonts no)))
            """);
        return new SymbolLibraryDocument(path, SymbolLibrary.Load(path));
    }
}
