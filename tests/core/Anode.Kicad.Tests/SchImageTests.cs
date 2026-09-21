using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Pictures on a sheet — a logo, a scan, a note drawn elsewhere. The file travels inside the sheet, and how big it
/// is drawn is what the picture says about itself rather than anything the sheet decides.
/// </summary>
public class SchImageTests
{
    private static string WithPicture => Path.Combine(TestData.KiCadDir, "demos", "tiny_tapeout", "rp2040.kicad_sch");

    [Fact]
    public void A_sheets_pictures_are_read_with_the_file_they_carry()
    {
        Assert.SkipUnless(File.Exists(WithPicture), TestData.SkipReason);

        var sheet = Schematic.Load(WithPicture);
        var image = Assert.Single(sheet.Images);

        Assert.NotNull(image.Data);
        Assert.NotNull(image.Png);

        // A PNG says so in its first bytes; anything else means the base64 was read wrong.
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.Data![..4]);
        Assert.True(image.Png!.Width > 0 && image.Png.Height > 0);
    }

    [Fact]
    public void How_big_it_is_drawn_is_its_own_pixels_at_its_own_resolution()
    {
        Assert.SkipUnless(File.Exists(WithPicture), TestData.SkipReason);

        var image = Schematic.Load(WithPicture).Images.Single();
        var size = Assert.NotNull(image.Size);
        var png = image.Png!;

        // The same rule the drawing sheet's pictures follow: pixels ÷ resolution × scale, in inches then millimetres.
        Assert.Equal((long)Math.Round(png.Width * 25.4 * 1_000_000 * image.Scale / png.Ppi), size.Width);
        Assert.Equal((long)Math.Round(png.Height * 25.4 * 1_000_000 * image.Scale / png.Ppi), size.Height);

        // This one is written smaller than its natural size, and is a sensible size on an A4-ish sheet.
        Assert.True(image.Scale is > 0 and < 1, $"scale {image.Scale}");
        Assert.InRange(size.Width / 1_000_000.0, 1, 400);
    }

    [Fact]
    public void A_picture_the_sheet_cannot_read_is_a_picture_without_a_size()
    {
        var sheet = Schematic.Parse(
            "(kicad_sch (version 20250114) (generator \"anode\") (uuid \"6f6b3b2a-0d2f-4a2f-9a9e-1a0d5c2f7b10\") (paper \"A4\")\n"
            + "\t(image (at 100 50) (scale 2) (uuid \"0a1b2c3d-0000-4000-8000-000000000001\") (data \"not base64 at all\"))\n"
            + "\t(image (at 120 50) (uuid \"0a1b2c3d-0000-4000-8000-000000000002\"))\n"
            + "\t(embedded_fonts no))\n");

        Assert.Equal(2, sheet.Images.Count);

        // Neither is drawn, but both are still there — and saving gives the file back untouched.
        Assert.All(sheet.Images, image => Assert.Null(image.Size));
        Assert.Equal(2, sheet.Images.Single(i => i.Scale > 1).Scale);
    }

    [Fact]
    public void A_picture_is_kept_byte_for_byte_through_a_round_trip()
    {
        Assert.SkipUnless(File.Exists(WithPicture), TestData.SkipReason);

        byte[] original = File.ReadAllBytes(WithPicture);
        var sheet = Schematic.Load(WithPicture);

        Assert.Equal(original, sheet.Document.ToBytes());
    }
}
