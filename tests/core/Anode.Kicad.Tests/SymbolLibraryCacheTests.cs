namespace Anode.Kicad.Tests;

/// <summary>
/// Reading a library once. What makes a held copy stale is the file changing underneath — a library edited in KiCad
/// while this application has it open — so the test changes the file and expects to be given the new one.
/// </summary>
public class SymbolLibraryCacheTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("anode-cache-").FullName;

    public SymbolLibraryCacheTests() => SymbolLibraryCache.Clear();

    public void Dispose()
    {
        SymbolLibraryCache.Clear();
        Directory.Delete(_folder, recursive: true);
    }

    private string Write(string symbol, params string[] more)
    {
        string path = Path.Combine(_folder, "parts.kicad_sym");
        string body = string.Concat(more.Prepend(symbol).Select(s => $"\t(symbol \"{s}\")\n"));
        File.WriteAllText(path, "(kicad_symbol_lib\n\t(version 20250324)\n\t(generator \"anode\")\n" + body + ")");
        return path;
    }

    [Fact]
    public void A_library_is_read_once_and_handed_out_again()
    {
        string path = Write("R");

        var first = SymbolLibraryCache.Load(path);
        var second = SymbolLibraryCache.Load(path);

        // Only that this library was read once and handed back. The cache is process-wide and other tests are
        // reading their own libraries through it at the same time, so its total is nobody's to assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void A_library_that_changed_on_disk_is_read_again()
    {
        string path = Write("R");
        var first = SymbolLibraryCache.Load(path);

        // Rewritten with another part in it, and stamped later, as an editor would leave it.
        Write("R", "C");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var second = SymbolLibraryCache.Load(path);

        Assert.NotSame(first, second);
        Assert.Equal(["R", "C"], second.Symbols.Select(s => s.Name));
    }

    [Fact]
    public void A_library_of_the_same_age_but_another_size_is_read_again()
    {
        string path = Write("R");
        var written = File.GetLastWriteTimeUtc(path);
        var first = SymbolLibraryCache.Load(path);

        // Some editors preserve the timestamp; the length still gives it away.
        Write("R", "C");
        File.SetLastWriteTimeUtc(path, written);

        Assert.NotSame(first, SymbolLibraryCache.Load(path));
    }

    [Fact]
    public void Forgetting_everything_means_reading_again()
    {
        string path = Write("R");
        var first = SymbolLibraryCache.Load(path);

        SymbolLibraryCache.Clear();

        Assert.NotSame(first, SymbolLibraryCache.Load(path));
    }

    [Fact]
    public void A_library_that_is_not_there_is_the_callers_problem()
    {
        Assert.ThrowsAny<IOException>(() => SymbolLibraryCache.Load(Path.Combine(_folder, "absent.kicad_sym")));
    }
}
