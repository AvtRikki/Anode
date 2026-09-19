using Anode.Geometry;
using Anode.Kicad;
using Anode.Kicad.DrawingSheets;

namespace Anode.Render.Tests;

/// <summary>The drawing-sheet interpreter's rules, each on the smallest template that shows it.</summary>
public class DrawingSheetTests
{
    private static readonly Vector2L A4 = new(297_000_000, 210_000_000);

    [Theory]
    [InlineData("1", 0, "1")]
    [InlineData("1", 3, "4")]
    [InlineData("1", 9, "10")]
    [InlineData("A", 2, "C")]
    [InlineData("Page 1", 1, "Page 2")]
    public void Labels_count_on_from_their_last_character(string label, int by, string expected) =>
        Assert.Equal(expected, DrawingSheet.Increment(label, by));

    [Fact]
    public void Variables_are_filled_in_and_unknown_ones_are_left_as_written()
    {
        var block = new SchTitleBlock("${PROJECTNAME} board", "2026-09-18", "B", "Anode")
        {
            Comments = new Dictionary<int, string> { [2] = "second" },
        };
        var frame = new SheetFrameText("a.kicad_sch", "/amp/", 2, 5)
        {
            Variables = new Dictionary<string, string> { ["PROJECTNAME"] = "wren", ["DESIGNER"] = "Paul" },
        };

        Assert.Equal(
            "wren board · B · second · Paul · 2/5 · /amp/ · A4 · ${NOBODY}",
            DrawingSheet.Expand("${TITLE} · ${REVISION} · ${COMMENT2} · ${DESIGNER} · ${#}/${##} · ${SHEETPATH} · ${PAPER} · ${NOBODY}", block, "A4", frame));
    }

    [Fact]
    public void Page_options_decide_which_page_an_item_is_on()
    {
        var template = DrawingSheetFile.Parse("""
            (kicad_wks
              (line (start 10 10) (end 20 10) (option page1only))
              (line (start 10 20) (end 20 20) (option notonpage1)))
            """);

        int On(int page)
        {
            int count = 0;
            DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText(Page: page) { Template = template }, (_, _, _) => count++);
            return count;
        }

        Assert.Equal(1, On(1));
        Assert.Equal(1, On(2));

        // And which one: the first line is 10 mm above the bottom margin, the second 20.
        double? y1 = null, y2 = null;
        DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText(Page: 1) { Template = template }, (a, _, _) => y1 = a.Y);
        DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText(Page: 2) { Template = template }, (a, _, _) => y2 = a.Y);
        Assert.Equal((210 - 10 - 10) * 1e6, y1!.Value, 1);
        Assert.Equal((210 - 10 - 20) * 1e6, y2!.Value, 1);
    }

    [Fact]
    public void Repeats_past_the_first_stop_at_the_margins()
    {
        var template = DrawingSheetFile.Parse("(kicad_wks (line (start 0 0 ltcorner) (end 0 5 ltcorner) (repeat 100) (incrx 50)))");
        int count = 0;
        DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, (_, _, _) => count++);

        // 277 mm between the margins: copies at 0, 50 … 250.
        Assert.Equal(6, count);
    }

    [Fact]
    public void A_polygon_is_filled_where_its_template_puts_it()
    {
        var template = DrawingSheetFile.Parse("""
            (kicad_wks (polygon (pos 20 20 ltcorner) (rotate 90) (pts (xy 0 0) (xy 10 0) (xy 10 5))))
            """);
        var outlines = new List<IReadOnlyList<Vector2D>>();
        DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, (_, _, _) => { }, outlines.Add);

        var outline = Assert.Single(outlines);

        // KiCad turns (10, 0) by 90° to (0, -10), then moves it to 30, 30 from the page corner.
        Assert.Equal(30e6, outline[1].X, 1);
        Assert.Equal(20e6, outline[1].Y, 1);
    }

    [Fact]
    public void A_picture_is_counted_as_not_drawn()
    {
        var template = DrawingSheetFile.Parse("(kicad_wks (bitmap (pos 10 10) (scale 1)))");

        Assert.Equal(1, DrawingSheet.Draw(A4, SchTitleBlock.Empty, "A4", new SheetFrameText { Template = template }, (_, _, _) => { }));
    }
}
