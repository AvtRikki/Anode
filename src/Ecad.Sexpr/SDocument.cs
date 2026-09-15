using System.Text;

namespace Ecad.Sexpr;

public sealed class SDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly List<SNode> _roots = [];

    public IReadOnlyList<SNode> Roots => _roots;

    /// <summary>The first top-level list, e.g. <c>(kicad_pcb ...)</c>.</summary>
    public SList Root => _roots.OfType<SList>().FirstOrDefault()
        ?? throw new InvalidOperationException("Document has no top-level list.");

    public string TrailingTrivia { get; set; } = string.Empty;

    public bool HasUtf8Bom { get; set; }

    public static SDocument Parse(string text) => SParser.Parse(text);

    public static SDocument Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static SDocument FromBytes(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        bool hasBom = bytes.StartsWith(bom);
        var doc = SParser.Parse(StrictUtf8.GetString(hasBom ? bytes[3..] : bytes));
        doc.HasUtf8Bom = hasBom;
        return doc;
    }

    public byte[] ToBytes()
    {
        byte[] body = StrictUtf8.GetBytes(SWriter.Write(this));
        return HasUtf8Bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>Writes atomically: a temp file next to the target is renamed over it.</summary>
    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temp, ToBytes());
        File.Move(temp, full, overwrite: true);
    }

    public void AddRoot(SNode node)
    {
        if (node.Parent is not null)
        {
            throw new InvalidOperationException("Node already belongs to a list.");
        }

        _roots.Add(node);
    }

    public override string ToString() => SWriter.Write(this);
}
