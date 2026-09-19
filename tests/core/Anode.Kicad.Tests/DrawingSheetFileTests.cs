using Anode.Kicad.DrawingSheets;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>Reading drawing sheets and the project settings that name them, on the templates KiCad's demos ship.</summary>
public class DrawingSheetFileTests
{
    private static string Demo(params string[] parts) => Path.Combine([TestData.KiCadDir, .. parts]);

    [Fact]
    public void The_default_reads_as_KiCads_layout()
    {
        var sheet = DrawingSheetFile.Default;

        Assert.Equal(new WksSetup(), sheet.Setup);
        var block = Assert.IsType<WksLine>(sheet.Items[0]);
        Assert.True(block.IsRectangle);
        Assert.Equal(new WksPoint(110, 34), block.Start);

        var border = Assert.IsType<WksLine>(sheet.Items[1]);
        Assert.Equal((WksCorner.LeftTop, 2, 2.0, 2.0), (border.Start.Corner, border.Repeat, border.IncrementX, border.IncrementY));

        var title = sheet.Items.OfType<WksText>().Single(t => t.Text.Contains("${TITLE}", StringComparison.Ordinal));
        Assert.Equal((2.0, 2.0, true, true), (title.Width, title.Height, title.Bold, title.Italic));

        // "center" centres both ways.
        var letter = sheet.Items.OfType<WksText>().First(t => t.Text == "A");
        Assert.Equal(("center", "center"), (letter.HorizontalAlign, letter.VerticalAlign));
    }

    [Theory]
    [InlineData("demos", "vme-wren", "cern-ohl-left.kicad_wks")]
    [InlineData("demos", "interf_u", "pagelayout_logo.kicad_wks")]
    [InlineData("qa", "data", "cli", "basic_test", "custom_ds.kicad_wks")]
    public void A_real_template_reads(params string[] parts)
    {
        string path = Demo(parts);
        Assert.SkipWhen(!File.Exists(path), TestData.SkipReason);

        var sheet = DrawingSheetFile.Load(path);

        Assert.Equal(path, sheet.Path);
        Assert.True(sheet.Items.Count > 15);
        Assert.Contains(sheet.Items, i => i is WksText);
        Assert.Contains(sheet.Items, i => i is WksLine { IsRectangle: true });
    }

    [Fact]
    public void A_logo_is_a_turned_outline()
    {
        string path = Demo("demos", "interf_u", "pagelayout_logo.kicad_wks");
        Assert.SkipWhen(!File.Exists(path), TestData.SkipReason);

        var logo = Assert.Single(DrawingSheetFile.Load(path).Items.OfType<WksPolygon>());
        Assert.NotEmpty(logo.Outlines);
        Assert.All(logo.Outlines, o => Assert.True(o.Count >= 3));
    }

    [Fact]
    public void Page_options_are_read()
    {
        var sheet = DrawingSheetFile.Parse("""
            (kicad_wks (setup (left_margin 5))
              (tbtext "first" (pos 10 10) (option page1only))
              (tbtext "later" (pos 10 10) (option notonpage1))
              (tbtext "always" (pos 10 10)))
            """);

        Assert.Equal(5, sheet.Setup.LeftMargin);
        Assert.Equal([WksPages.FirstOnly, WksPages.NotOnFirst, WksPages.All], sheet.Items.Select(i => i.Pages));
    }

    [Theory]
    [InlineData("Title: %T", "Title: ${TITLE}")]
    [InlineData("Id: %S/%N", "Id: ${#}/${##}")]
    [InlineData("%C0 %C3", "${COMMENT1} ${COMMENT4}")]
    [InlineData("100%%", "100%")]
    [InlineData("%Q gone", " gone")]
    [InlineData("${TITLE} stays", "${TITLE} stays")]
    public void Older_percent_codes_become_the_variables_they_stand_for(string written, string read) =>
        Assert.Equal(read, DrawingSheetFile.ConvertLegacyCodes(written));

    [Fact]
    public void Something_else_is_refused()
    {
        Assert.Throws<KiCadFormatException>(() => DrawingSheetFile.Parse("(kicad_sch (version 1))"));
    }

    [Fact]
    public void A_project_names_a_drawing_sheet_per_editor_and_its_variables()
    {
        string pro = Demo("demos", "vme-wren", "vme-wren.kicad_pro");
        Assert.SkipWhen(!File.Exists(pro), TestData.SkipReason);

        var project = ProjectFile.Load(pro);

        Assert.Equal("cern-ohl-left.kicad_wks", project.SchematicDrawingSheet);
        Assert.Equal(string.Empty, project.BoardDrawingSheet);
        Assert.Equal("BE-CEM", project.TextVariables["DIVGRP"]);

        Assert.Equal(Demo("demos", "vme-wren", "cern-ohl-left.kicad_wks"), project.Resolve(project.SchematicDrawingSheet));
        Assert.Null(project.Resolve(project.BoardDrawingSheet));
        Assert.Equal(Path.Combine(project.Folder, "x.kicad_wks"), project.Resolve("${KIPRJMOD}/x.kicad_wks"));

        // Found from any file of the project.
        Assert.Equal(pro, ProjectFile.For(Demo("demos", "vme-wren", "vme-wren.kicad_sch"))?.Path);
    }
}
