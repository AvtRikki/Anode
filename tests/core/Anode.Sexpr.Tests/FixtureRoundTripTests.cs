using System.Globalization;
using System.Text;
using Anode.Tests;

namespace Anode.Sexpr.Tests;

public class FixtureRoundTripTests
{
    private static readonly Dictionary<string, string> KnownNonCanonical = new()
    {
        ["embedded_table_rotated_legacy_v10.kicad_pcb"] = "Stored with CRLF line endings.",
        ["RoyalBlue54L-Feather.kicad_pcb"] = "Contains '(curved_edges no)(filter_ratio 0.9)', which the formatter never emits.",
        ["thirty-copper-layers.kicad_pcb"] = "Hand-written fixture with single-line stackup layers.",
    };

    public static TheoryData<string> AllFiles() =>
        TestData.Files(".kicad_pcb", ".kicad_sch", ".kicad_sym", ".kicad_mod");

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void Parse_then_write_is_byte_identical(string file)
    {
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        byte[] original = File.ReadAllBytes(TestData.FullPath(file));
        byte[] written = SDocument.FromBytes(original).ToBytes();

        Assert.True(original.AsSpan().SequenceEqual(written), $"{file}: output differs at byte {FirstDifference(original, written)}");
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void Prettifier_reproduces_kicad_layout(string file)
    {
        Assert.SkipWhen(file.Length == 0, TestData.SkipReason);

        string original = File.ReadAllText(TestData.FullPath(file), Encoding.UTF8);
        var doc = SDocument.Parse(original);

        // The prettifier appeared in KiCad 7.99; older or hand-written fixtures use other layouts.
        string? generatorVersion = doc.Root.Find("generator_version")?.AtomAt(1)?.Value;
        Assert.SkipUnless(double.TryParse(generatorVersion, CultureInfo.InvariantCulture, out var v) && v >= 8,
            $"Saved by KiCad {generatorVersion ?? "< 7"}, before the current formatter.");
        Assert.SkipWhen(KnownNonCanonical.TryGetValue(Path.GetFileName(file), out var reason), reason ?? string.Empty);

        string prettified = SWriter.WritePrettified(doc);

        int diff = FirstDifference(Encoding.UTF8.GetBytes(original), Encoding.UTF8.GetBytes(prettified));
        Assert.True(diff < 0, $"{file}: differs at byte {diff}: ...{Excerpt(original, diff)}... vs ...{Excerpt(prettified, diff)}...");
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }

        return a.Length == b.Length ? -1 : n;
    }

    private static string Excerpt(string s, int at)
    {
        int start = Math.Clamp(at - 40, 0, s.Length);
        int end = Math.Clamp(at + 40, 0, s.Length);
        return s[start..end].Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
