using Anode.Kicad;
using Anode.Render;

using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// Syncing a sheet's pins from the inspector: selecting the sheet lists what does not agree with the labels inside,
/// and each way of making it agree is one step to undo that touches only the sheet on screen.
/// </summary>
public sealed class SheetPinSyncTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-sync-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_inspector_lists_what_does_not_agree()
    {
        using var document = Open(out var sheet);
        document.Editor.SetSelection([sheet.Sheets.Single()]);

        var block = document.Selection!.Blocks.Single(b => b.Title == Tr("sch.sync.title"));

        Assert.Contains(block.Rows, r => r.Name == "RESET" && r.Trailing == Tr("sch.sync.noPin"));
        Assert.Contains(block.Rows, r => r.Name == "OLD" && r.Value == Tr("sch.sync.noLabel"));
        Assert.Contains(block.Rows, r => r.Name == "CLK" && r.Trailing == Tr("sch.sync.shapeDiffers"));
    }

    [Fact]
    public void Each_fix_is_one_step_and_undoes_to_the_byte()
    {
        using var document = Open(out var sheet);
        var child = sheet.Sheets.Single();
        byte[] original = sheet.Document.ToBytes();

        foreach (string key in new[] { "sch.sync.addPins", "sch.sync.takeShapes", "sch.sync.removePins" })
        {
            document.Editor.SetSelection([child]);
            var action = document.Selection!.Actions.Single(a => a.Label.StartsWith(Tr(key).Split('{')[0], StringComparison.Ordinal));
            action.Run();
        }

        Assert.True(document.PinMatch(child)!.IsInStep);

        document.Editor.Undo();
        document.Editor.Undo();
        document.Editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    /// <summary>A pin left behind when its label was renamed inside is pointed at the new name, and answers it.</summary>
    [Fact]
    public void A_pin_is_pointed_at_a_label_that_has_none()
    {
        using var document = Open(out var sheet);
        var child = sheet.Sheets.Single();
        document.Editor.SetSelection([child]);

        var row = document.Selection!.Blocks.Single(b => b.Title == Tr("sch.sync.title")).Rows.Single(r => r.Name == "OLD");
        Assert.Contains("RESET", row.Choices!);
        row.Commit!("RESET");

        var match = document.PinMatch(child)!;
        Assert.Empty(match.PinsWithoutLabel);
        Assert.DoesNotContain(match.LabelsWithoutPin, l => l.Shown == "RESET");
    }

    private static string Tr(string key) => Anode.Sdk.Tr.T(key);

    /// <summary>
    /// A sheet with pins SDA (agrees), CLK (input outside, output inside) and OLD (nothing inside), over a child
    /// with labels SDA, CLK and RESET.
    /// </summary>
    private SchematicDocument Open(out KicadSchematic sheet)
    {
        File.WriteAllText(Path.Combine(_folder, "child.kicad_sch"),
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"7f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + Label("SDA", "bidirectional", 1) + Label("CLK", "output", 2) + Label("RESET", "input", 3)
            + "\t(embedded_fonts no))\n");

        string parent = Path.Combine(_folder, "parent.kicad_sch");
        File.WriteAllText(parent,
            "(kicad_sch (version 20260206) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + "\t(sheet (at 40.64 30.48) (size 20.32 20.32) (uuid \"3a1b2c3d-0000-4000-8000-000000000001\")\n"
            + "\t\t(property \"Sheetname\" \"Child\" (at 40.64 29 0))\n"
            + "\t\t(property \"Sheetfile\" \"child.kicad_sch\" (at 40.64 52 0))\n"
            + Pin("SDA", "bidirectional", 35.56, 1) + Pin("CLK", "input", 40.64, 2) + Pin("OLD", "input", 45.72, 3)
            + "\t)\n"
            + "\t(embedded_fonts no))\n");

        sheet = KicadSchematic.Load(parent);
        var scene = SchematicSceneBuilder.Build(sheet);
        return new SchematicDocument(sheet, scene, parent, new SymbolLibraryList(Path.Combine(_folder, "data")));

        static string Label(string name, string shape, int n) =>
            $"\t(hierarchical_label \"{name}\" (shape {shape}) (at 100 {50 + (5 * n)} 0) (effects (font (size 1.27 1.27)))"
            + $" (uuid \"5a1b2c3d-0000-4000-8000-00000000000{n}\"))\n";

        static string Pin(string name, string shape, double y, int n) =>
            FormattableString.Invariant($"\t\t(pin \"{name}\" {shape} (at 40.64 {y} 180) (uuid \"4a1b2c3d-0000-4000-8000-00000000000{n}\")")
            + " (effects (font (size 1.27 1.27)) (justify left)))\n";
    }
}
