using System.Text;
using Anode.Tests;

namespace Anode.Kicad.Tests;

/// <summary>
/// Files KiCad embeds, read back as KiCad reads them: every one the demos carry decompresses and matches the checksum
/// KiCad wrote, and each is the kind of file its name says.
/// </summary>
public class EmbeddedFileTests
{
    [Theory]
    [InlineData("qa/data/pcbnew/api_kitchen_sink.kicad_pcb")]
    [InlineData("demos/royalblue54L_feather/RoyalBlue54L-Feather.kicad_pcb")]
    [InlineData("demos/vme-wren/vme-wren.kicad_pcb")]
    public void Every_embedded_file_matches_the_checksum_KiCad_wrote(string file)
    {
        string path = TestData.FullPath(file);
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var files = EmbeddedFile.In(Board.Load(path).Document.Root).Where(f => f.HasData).ToList();
        Assert.NotEmpty(files);

        foreach (var embedded in files)
        {
            byte[]? data = embedded.Data;
            Assert.True(data is not null, $"{embedded.Name} does not decode or match {embedded.Checksum}");

            string head = Encoding.Latin1.GetString(data!, 0, Math.Min(16, data!.Length));
            switch (Path.GetExtension(embedded.Name).ToLowerInvariant())
            {
                case ".png": Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], data[..4]); break;
                case ".pdf": Assert.StartsWith("%PDF", head, StringComparison.Ordinal); break;
                case ".step" or ".stp": Assert.StartsWith("ISO-10303-21", head, StringComparison.Ordinal); break;
            }
        }
    }

    [Fact]
    public void A_footprint_lists_its_files_by_checksum_and_the_board_carries_the_data()
    {
        string path = TestData.FullPath("qa/data/pcbnew/api_kitchen_sink.kicad_pcb");
        Assert.SkipUnless(File.Exists(path), TestData.SkipReason);

        var files = EmbeddedFile.In(Board.Load(path).Document.Root);
        var reference = Assert.Single(files, f => !f.HasData);
        var carried = Assert.Single(files, f => f.HasData);

        Assert.Equal(reference.Name, carried.Name);
        Assert.Equal(reference.Checksum, carried.Checksum);
        Assert.Null(reference.Data);
    }

    [Fact]
    public void A_file_that_does_not_match_its_checksum_is_refused()
    {
        byte[] original = Encoding.UTF8.GetBytes("a font, say, of thirty-one bytes");
        var (encoded, checksum) = EmbeddedFile.Encode(original);

        Assert.Equal(original, EmbeddedFile.Decode(encoded, checksum));
        Assert.Null(EmbeddedFile.Decode(encoded, "0123456789ABCDEF0123456789ABCDEF"));
        Assert.Null(EmbeddedFile.Decode("not base64 at all", checksum));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(29)]
    public void The_legacy_checksum_differs_only_where_the_tail_was_padded(int length)
    {
        byte[] data = [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7))];

        bool padded = length % 16 % 4 != 0;
        Assert.Equal(!padded, EmbeddedFile.ChecksumOf(data) == EmbeddedFile.ChecksumOf(data, legacyTail: true));
        Assert.Equal(32, EmbeddedFile.ChecksumOf(data).Length);
    }
}
