using Anode.Geometry;
using Anode.Kicad.Editing;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Editing a sheet: a symbol moves with its fields, turns about a pivot, mirrors by a property rather than by
/// reflected geometry — and every one of those, undone, gives the file back byte for byte.
/// </summary>
public class SchEditingTests
{
    private static Schematic Sheet(out string path)
    {
        path = TestData.AnySchematic() ?? string.Empty;
        return path.Length == 0 ? null! : Schematic.Load(path);
    }

    [Fact]
    public void A_symbol_moves_with_its_fields()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);

        var symbol = sheet.Symbols[0];
        var before = symbol.Position;
        var fieldsBefore = symbol.Fields.Select(f => f.Position).ToList();
        var delta = new Vector2L(2_540_000, -1_270_000);

        SchEdits.Transform(symbol, SchEdits.Anchor(symbol), 0, delta);

        Assert.Equal(before + delta, symbol.Position);
        Assert.Equal([.. fieldsBefore.Select(p => p + delta)], symbol.Fields.Select(f => f.Position));
    }

    [Fact]
    public void A_wire_moves_by_its_points()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);
        Assert.SkipWhen(sheet.Wires.Count == 0, "The sheet has no wires.");

        var wire = sheet.Wires[0];
        var before = wire.Points;
        var delta = new Vector2L(1_270_000, 1_270_000);

        SchEdits.Transform(wire, SchEdits.Anchor(wire), 0, delta);

        Assert.Equal([.. before.Select(p => p + delta)], wire.Points);
    }

    [Fact]
    public void Rotating_a_symbol_turns_it_about_its_own_point()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);

        var symbol = sheet.Symbols[0];
        var pivot = symbol.Position;
        double before = symbol.Angle;

        SchEdits.Transform(symbol, pivot, 90, default);

        Assert.Equal(pivot, symbol.Position);
        Assert.Equal(KiCadNumber.Normalize360(before + 90), symbol.Angle);
    }

    [Fact]
    public void Mirroring_twice_returns_the_symbol_as_it_was()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);

        var symbol = sheet.Symbols[0];
        string? mirror = symbol.Mirror;
        var pivot = symbol.Position;

        SchEdits.Mirror(symbol, pivot, horizontal: true);
        Assert.NotEqual(mirror, symbol.Mirror);

        SchEdits.Mirror(symbol, pivot, horizontal: true);
        Assert.Equal(mirror, symbol.Mirror);
        Assert.Equal(pivot, symbol.Position);
    }

    [Fact]
    public void Delete_and_undo_put_items_back_where_they_were()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);

        byte[] original = sheet.Document.ToBytes();
        int symbols = sheet.Symbols.Count;
        var history = new UndoStack();

        history.Execute(new DeleteNodesCommand(sheet, [sheet.Symbols[0], sheet.Symbols[1]]));
        Assert.Equal(symbols - 2, sheet.Symbols.Count);

        history.Undo();

        Assert.Equal(symbols, sheet.Symbols.Count);
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void An_edited_then_undone_sheet_is_the_original_file()
    {
        var sheet = Sheet(out string path);
        Assert.SkipWhen(path.Length == 0, TestData.SkipReason);

        byte[] original = File.ReadAllBytes(path);
        var history = new UndoStack();
        var symbol = sheet.Symbols[0];

        history.Execute(new ModifyNodesCommand("Move", [symbol], () =>
            SchEdits.Transform(symbol, SchEdits.Anchor(symbol), 90, new Vector2L(2_540_000, 2_540_000))));

        Assert.NotEqual(original, sheet.Document.ToBytes());
        Assert.True(history.IsDirty);

        history.Undo();

        Assert.Equal(original, sheet.Document.ToBytes());
        Assert.False(history.IsDirty);

        // Redo puts the edit back, and the second undo is still exact.
        history.Redo();
        history.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }
}
