using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render;
using Anode.Tests;

using KicadSchematic = Anode.Kicad.Schematic;

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// Find and Replace on a real sheet: stepping lands on each place in turn and selects it, a replace is a step to
/// undo, a replace of everything is one step, and a search narrowed to the selection stays narrowed while it moves.
/// </summary>
public class FindReplaceTests
{
    private static string File => Path.Combine(TestData.KiCadDir, "demos", "pic_programmer", "pic_programmer.kicad_sch");

    [Fact]
    public void Stepping_visits_every_place_in_turn_and_comes_round()
    {
        Assert.SkipUnless(System.IO.File.Exists(File), TestData.SkipReason);
        using var document = Open(out _);
        Reset("10K", SchFindMode.WholeWord, matchCase: true);

        var hits = document.Find(FindSession.Options);
        Assert.Equal(7, hits.Count);

        for (int i = 0; i < hits.Count; i++)
        {
            FindSession.Step(document, 1);
            Assert.Equal(i, FindSession.IndexOf(hits, FindSession.Current));
            Assert.Same(hits[i].Item, Assert.Single(document.Editor.Selection));
        }

        FindSession.Step(document, 1);
        Assert.Equal(0, FindSession.IndexOf(hits, FindSession.Current));

        FindSession.Step(document, -1);
        Assert.Equal(hits.Count - 1, FindSession.IndexOf(hits, FindSession.Current));
    }

    [Fact]
    public void Replacing_everything_is_one_step_to_undo()
    {
        Assert.SkipUnless(System.IO.File.Exists(File), TestData.SkipReason);
        using var document = Open(out var sheet);
        Reset("10K", SchFindMode.WholeWord, matchCase: true);
        byte[] original = sheet.Document.ToBytes();

        int changed = document.ReplaceAll(document.Find(FindSession.Options), FindSession.Options, "10k");

        Assert.Equal(7, changed);
        Assert.Empty(document.Find(FindSession.Options));
        Assert.Equal(7, sheet.Symbols.Count(s => s.Value == "10k"));

        document.Editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    [Fact]
    public void One_place_is_replaced_and_the_next_one_is_gone_on_to()
    {
        Assert.SkipUnless(System.IO.File.Exists(File), TestData.SkipReason);
        using var document = Open(out var sheet);
        Reset("10K", SchFindMode.WholeWord, matchCase: true);
        byte[] original = sheet.Document.ToBytes();

        FindSession.Step(document, 1);
        var first = FindSession.Current!;
        Assert.True(document.Replace(first, FindSession.Options, "12K"));
        FindSession.Step(document, 1);

        // Six are left, and the step went on to the first of them rather than starting over or stopping.
        var left = document.Find(FindSession.Options);
        Assert.Equal(6, left.Count);
        Assert.Equal(0, FindSession.IndexOf(left, FindSession.Current));

        document.Editor.Undo();
        Assert.Equal(original, sheet.Document.ToBytes());
    }

    /// <summary>
    /// Every step selects the place it lands on. Were the narrowing read from the selection as it is now, the second
    /// step would find only the place the first one selected.
    /// </summary>
    [Fact]
    public void A_search_narrowed_to_the_selection_stays_narrowed_while_it_moves()
    {
        Assert.SkipUnless(System.IO.File.Exists(File), TestData.SkipReason);
        using var document = Open(out var sheet);
        Reset("10K", SchFindMode.WholeWord, matchCase: true);

        var chosen = sheet.Symbols.Where(s => s.Value == "10K").Take(3).Cast<SchItem>().ToList();
        document.Editor.SetSelection(chosen);
        FindSession.Within = document.SelectedItems;

        var seen = new HashSet<SchItem>();
        for (int i = 0; i < 6; i++)
        {
            var hits = FindSession.Step(document, 1);
            Assert.Equal(3, hits.Count);
            seen.Add(FindSession.Current!.Item);
        }

        Assert.Equal(chosen.ToHashSet(), seen);
    }

    private static void Reset(string text, SchFindMode mode, bool matchCase)
    {
        FindSession.Text = text;
        FindSession.Mode = mode;
        FindSession.MatchCase = matchCase;
        FindSession.HiddenFields = false;
        FindSession.Pins = false;
        FindSession.ReplaceReferences = false;
        FindSession.Within = null;
        FindSession.Current = null;
    }

    private static SchematicDocument Open(out KicadSchematic sheet)
    {
        sheet = KicadSchematic.Load(File);
        var scene = SchematicSceneBuilder.Build(sheet);
        string data = Directory.CreateTempSubdirectory("anode-find-").FullName;
        return new SchematicDocument(sheet, scene, File, new SymbolLibraryList(data));
    }
}
