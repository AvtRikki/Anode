using Anode.Kicad;
using Anode.Kicad.Editing;
using Anode.Render.Fonts;
using Anode.Tests;
using SkiaSharp;

namespace Anode.Render.Tests;

/// <summary>
/// What a document carries when it saves with KiCad's <c>(embedded_fonts yes)</c>: the file of each face its own
/// texts use, when the face's licence allows — read back, the file is that face — and nothing for a stand-in.
/// </summary>
public class FontEmbeddingTests
{
    /// <summary>An installed family whose licence reads as <paramref name="wanted"/>, or null when there is none here.</summary>
    private static string? Installed(Func<FontEmbedding, bool> wanted) =>
        new[] { "Helvetica", "Menlo", "DejaVu Sans", "Liberation Sans", "Arial", "Avenir", "Verdana", "Georgia" }
            .Concat(SKFontManager.Default.FontFamilies)
            .Where(OutlineText.IsInstalled)
            .FirstOrDefault(f => OutlineText.FileOf(f, false, false) is { } file && wanted(file.Embedding));

    private static Board Board(string face, bool wanted = true, string? footprintFace = null) => Kicad.Board.Parse($$"""
        (kicad_pcb (version 20241229) (generator "pcbnew")
          (layers (0 "F.Cu" signal) (5 "F.SilkS" user))
          (footprint "Lib:Part" (layer "F.Cu") (at 20 20)
            (property "Reference" "U1" (at 0 -2 0) (layer "F.SilkS")
              (effects (font (face "{{footprintFace ?? face}}") (size 1 1)))))
          (gr_text "Anode" (at 10 10 0) (layer "F.SilkS")
            (effects (font (face "{{face}}") (size 2 2))))
          (embedded_fonts {{(wanted ? "yes" : "no")}}))
        """);

    [Fact]
    public void An_installed_face_is_carried_under_its_file_name_and_reads_back_as_itself()
    {
        string? face = Installed(e => e is FontEmbedding.Installable or FontEmbedding.Editable);
        Assert.SkipWhen(face is null, "no embeddable face installed");

        var board = Board(face!);
        Assert.True(EmbeddedFonts.Sync(board.Document.Root, DocumentFonts.Carried(DocumentFonts.Of(board))));

        var carried = Assert.Single(EmbeddedFile.In(Kicad.Board.Parse(board.Document.ToString()).Document.Root));
        Assert.True(carried.IsFont);
        Assert.Matches(@"\.(ttf|otf|ttc)$", carried.Name);
        using var typeface = SKTypeface.FromData(SKData.CreateCopy(carried.Data!));
        Assert.Equal(face, typeface.FamilyName, StringComparer.OrdinalIgnoreCase);

        // Saving again adds nothing: the face is carried under that name already.
        Assert.False(EmbeddedFonts.Sync(board.Document.Root, DocumentFonts.Carried(DocumentFonts.Of(board))));
    }

    [Fact]
    public void A_face_whose_licence_forbids_it_stays_behind()
    {
        string? face = Installed(e => e is FontEmbedding.PreviewAndPrint or FontEmbedding.Restricted);
        Assert.SkipWhen(face is null, "no face with a restrictive licence installed");

        var board = Board(face!);
        var use = Assert.Single(DocumentFonts.Of(board));

        Assert.False(use.File!.MayTravel);
        Assert.False(EmbeddedFonts.Sync(board.Document.Root, DocumentFonts.Carried([use])));
    }

    [Fact]
    public void Only_the_boards_own_texts_count_and_a_stand_in_is_never_carried()
    {
        var board = Board("Board Face Anode", footprintFace: "Footprint Face Anode");
        var use = Assert.Single(DocumentFonts.Of(board));

        Assert.Equal("Board Face Anode", use.Face);
        Assert.Null(use.File);
        Assert.Empty(DocumentFonts.Carried([use]));
    }

    [Fact]
    public void A_face_carried_by_another_file_travels_under_the_name_it_had_there()
    {
        string font = TestData.FullPath("qa/resources/fonts/NotoSans-Regular.ttf");
        Assert.SkipUnless(File.Exists(font), TestData.SkipReason);
        byte[] data = File.ReadAllBytes(font);
        OutlineText.Embed(EmbeddedFile.In(Kicad.Board.Parse(
            $"(kicad_pcb (version 20241229) (generator \"pcbnew\") (embedded_fonts yes) {EmbeddedFile.Block("NotoSans-Regular.ttf", "font", data)})").Document.Root));

        var board = Board("Noto Sans");
        EmbeddedFonts.Sync(board.Document.Root, DocumentFonts.Carried(DocumentFonts.Of(board)));

        var carried = Assert.Single(EmbeddedFile.In(board.Document.Root));
        Assert.Equal("NotoSans-Regular.ttf", carried.Name);
        Assert.Equal(data, carried.Data);
    }
}
