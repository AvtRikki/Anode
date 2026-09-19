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

    /// <summary>A 6 × 3 pixel PNG that says it is 600 pixels to the inch (23622 per metre).</summary>
    private const string Png600 = "iVBORw0KGgoAAAANSUhEUgAAAAYAAAADCAIAAAA/Y+msAAAACXBIWXMAAFxGAABcRgEUlENBAAAAEUlEQVR4nGP4z8CAhtD52IUAEUQR788PiNkAAAAASUVORK5CYII=";

    [Fact]
    public void A_picture_reads_with_its_size_from_its_own_header()
    {
        var bitmap = Assert.IsType<WksBitmap>(Assert.Single(DrawingSheetFile.Parse($"""
            (kicad_wks (bitmap (name "logo") (pos 30 20) (scale 2) (data "{Png600[..40]}" "{Png600[40..]}")))
            """).Items));

        Assert.Equal(new PngInfo(6, 3, 600), bitmap.Png);

        // Pixels × 25.4 × scale / PPI: 6 px at 600 PPI, twice as large, is 0.508 mm.
        Assert.Equal(0.508, bitmap.SizeMm!.Value.Width, 6);
        Assert.Equal(0.254, bitmap.SizeMm!.Value.Height, 6);
    }

    [Fact]
    public void An_older_picture_in_hex_reads_the_same()
    {
        var bitmap = Assert.IsType<WksBitmap>(Assert.Single(DrawingSheetFile.Parse("""
            (page_layout (bitmap (pos 30 20) (scale 1) (pngdata
              (data "89 50 4E 47 0D 0A 1A 0A 00 00 00 0D 49 48 44 52 00 00 00 06 00 00 00 03 08 02 00 00 00 3F 63 E9 AC 00 00 00 09 70 48 59 73 00 00 5C 46 00 00")
              (data "5C 46 01 14 94 43 41 00 00 00 11 49 44 41 54 78 9C 63 F8 CF C0 80 86 D0 F9 D8 85 00 11 44 11 EF CF 0F 88 D9 00 00 00 00 49 45 4E 44 AE 42 60 82"))))
            """).Items));

        Assert.Equal(Convert.FromBase64String(Png600), bitmap.Image);
    }

    [Fact]
    public void A_picture_that_is_not_a_PNG_has_no_size_and_is_counted_out()
    {
        var bitmap = Assert.IsType<WksBitmap>(Assert.Single(DrawingSheetFile.Parse(
            "(kicad_wks (bitmap (pos 30 20) (data \"R0lGODlhAQABAAAAACw=\")))").Items));

        Assert.NotNull(bitmap.Image);
        Assert.Null(bitmap.SizeMm);
    }

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
