using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Anode.Sexpr;
using ZstdSharp;

namespace Anode.Kicad;

/// <summary>
/// A file KiCad carries inside a board, a schematic, a footprint or a symbol: <c>(embedded_files (file (name "…")
/// (type font) (data |…|) (checksum "…")))</c>. The data is the file compressed with zstd and written in base64, in
/// lines of 76 between bars; the checksum is taken of the file itself. A footprint on a board lists its files by name
/// and checksum only, the data living once at the board's root.
/// </summary>
/// <param name="Type">"font", "model", "datasheet", "worksheet" or "other".</param>
public sealed class EmbeddedFile(string name, string type, string? checksum, string? encoded)
{
    private byte[]? _data;
    private bool _read;

    public string Name { get; } = name;

    public string Type { get; } = type;

    public string? Checksum { get; } = checksum;

    public bool IsFont => Type == "font";

    /// <summary>Whether this entry carries the data itself, not only a reference to it.</summary>
    public bool HasData => encoded is { Length: > 0 };

    /// <summary>
    /// The file itself, decompressed; null when the entry carries no data, the data does not decompress, or it does
    /// not match its checksum — KiCad refuses such a file too.
    /// </summary>
    public byte[]? Data
    {
        get
        {
            if (!_read)
            {
                _data = encoded is { Length: > 0 } ? Decode(encoded, Checksum) : null;
                _read = true;
            }

            return _data;
        }
    }

    /// <summary>Every file embedded anywhere under <paramref name="root"/>, in file order.</summary>
    public static IReadOnlyList<EmbeddedFile> In(SList root)
    {
        var files = new List<EmbeddedFile>();
        Walk(root);
        return files;

        void Walk(SList list)
        {
            foreach (var child in list.Lists())
            {
                if (child.Head != "embedded_files")
                {
                    Walk(child);
                    continue;
                }

                foreach (var file in child.Lists().Where(f => f.Head == "file"))
                {
                    if (file.ChildString("name") is not { } name)
                    {
                        continue;
                    }

                    string? encoded = null;
                    if (file.Find("data") is { } data)
                    {
                        var text = new StringBuilder();
                        for (int i = 1; i < data.Count; i++)
                        {
                            if (data.AtomAt(i) is { } atom)
                            {
                                text.Append(atom.Value.Trim('|'));
                            }
                        }

                        encoded = text.ToString();
                    }

                    files.Add(new EmbeddedFile(name, file.Find("type")?.AtomAt(1)?.Value ?? "other", file.ChildString("checksum"), encoded));
                }
            }
        }
    }

    /// <summary>
    /// The file behind KiCad's encoding, checked as KiCad checks it: a 64-character checksum is SHA-256 (older
    /// files), otherwise MurmurHash3, falling back to the tail handling KiCad used before fixing it.
    /// </summary>
    public static byte[]? Decode(string encoded, string? checksum)
    {
        byte[] data;
        try
        {
            using var decompressor = new Decompressor();
            data = decompressor.Unwrap(Convert.FromBase64String(encoded)).ToArray();
        }
        catch (Exception e) when (e is FormatException or ZstdException)
        {
            return null;
        }

        if (checksum is null)
        {
            return data;
        }

        bool matches = checksum.Length == 64
            ? string.Equals(Convert.ToHexString(SHA256.HashData(data)), checksum, StringComparison.OrdinalIgnoreCase)
            : checksum == ChecksumOf(data) || checksum == ChecksumOf(data, legacyTail: true);
        return matches ? data : null;
    }

    /// <summary>How KiCad writes a file it embeds: zstd at level 15, then base64, with its checksum.</summary>
    public static (string Encoded, string Checksum) Encode(ReadOnlySpan<byte> data)
    {
        using var compressor = new Compressor(15);
        return (Convert.ToBase64String(compressor.Wrap(data)), ChecksumOf(data));
    }

    /// <summary>
    /// An <c>(embedded_files …)</c> block holding one file, laid out as KiCad writes it: the data in lines of 76
    /// characters, a bar before the first and after the last.
    /// </summary>
    public static string Block(string name, string type, ReadOnlySpan<byte> data)
    {
        var (encoded, checksum) = Encode(data);
        var text = new StringBuilder();
        text.Append("(embedded_files\n\t(file\n\t\t(name ").Append(SEscape.Quote(name)).Append(")\n\t\t(type ").Append(type).Append(")\n\t\t(data");
        for (int first = 0; first < encoded.Length; first += 76)
        {
            int length = Math.Min(76, encoded.Length - first);
            text.Append("\n\t\t\t").Append(first == 0 ? "|" : string.Empty).Append(encoded, first, length)
                .Append(first + length == encoded.Length ? "|" : string.Empty);
        }

        text.Append("\n\t\t)\n\t\t(checksum ").Append(SEscape.Quote(checksum)).Append(")\n\t)\n)");
        return text.ToString();
    }

    /// <summary>
    /// KiCad's checksum of an embedded file: MurmurHash3 x64 128 seeded with 0xABBA2345, both halves as upper-case
    /// hex. <paramref name="legacyTail"/> reproduces the hash of files saved before KiCad fixed its tail handling,
    /// which padded the last bytes to a whole word and counted the padding in the length.
    /// </summary>
    public static string ChecksumOf(ReadOnlySpan<byte> data, bool legacyTail = false)
    {
        const ulong c1 = 0x87c37b91114253d5, c2 = 0x4cf5ad432745937f;
        const uint seed = 0xABBA2345;
        ulong h1 = seed, h2 = seed;

        int blocks = data.Length / 16;
        for (int i = 0; i < blocks; i++)
        {
            ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i * 16));
            ulong k2 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice((i * 16) + 8));
            k1 *= c1; k1 = ulong.RotateLeft(k1, 31); k1 *= c2; h1 ^= k1;
            h1 = ulong.RotateLeft(h1, 27); h1 += h2; h1 = (h1 * 5) + 0x52dce729;
            k2 *= c2; k2 = ulong.RotateLeft(k2, 33); k2 *= c1; h2 ^= k2;
            h2 = ulong.RotateLeft(h2, 31); h2 += h1; h2 = (h2 * 5) + 0x38495ab5;
        }

        // The tail, zero-padded; the length is a 32-bit count, as KiCad keeps it.
        Span<byte> tail = stackalloc byte[16];
        tail.Clear();
        var rest = data.Slice(blocks * 16);
        rest.CopyTo(tail);
        int tailLength = rest.Length;
        if (legacyTail && tailLength > 0)
        {
            tailLength += 4 - ((tailLength + 4) % 4);
        }

        uint length = unchecked((uint)((blocks * 16) + tailLength));

        // KiCad mixes the tail by the length modulo 16: a legacy tail padded to a full 16 bytes is not mixed at all.
        int mixed = (int)(length & 15);
        if (mixed > 0)
        {
            ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(tail);
            ulong k2 = BinaryPrimitives.ReadUInt64LittleEndian(tail.Slice(8));
            if (mixed > 8)
            {
                k2 &= (1UL << ((mixed - 8) * 8)) - 1;
                k2 *= c2; k2 = ulong.RotateLeft(k2, 33); k2 *= c1; h2 ^= k2;
            }

            k1 &= mixed >= 8 ? ulong.MaxValue : (1UL << (mixed * 8)) - 1;
            k1 *= c1; k1 = ulong.RotateLeft(k1, 31); k1 *= c2; h1 ^= k1;
        }

        h1 ^= length; h2 ^= length;
        h1 += h2; h2 += h1;
        h1 = Mix(h1); h2 = Mix(h2);
        h1 += h2; h2 += h1;
        return $"{h1:X16}{h2:X16}";

        static ulong Mix(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccd;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53;
            k ^= k >> 33;
            return k;
        }
    }
}
