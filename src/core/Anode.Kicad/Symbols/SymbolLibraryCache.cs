namespace Anode.Kicad;

/// <summary>
/// Libraries already read, kept so they are read once. A symbol library is a file of some size — KiCad's own run to
/// hundreds of kilobytes — and the same ones are wanted again every time a sheet is opened or the panel is rebuilt.
///
/// What makes a cached copy stale is the file changing underneath: the entry remembers when the file was last
/// written and how long it was, and reads again when either differs. That covers a library edited in KiCad while
/// this application has it open, which is the case that matters.
/// </summary>
public static class SymbolLibraryCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    private sealed record Entry(SymbolLibrary Library, DateTime Written, long Length);

    /// <summary>How many libraries are held. For tests and for anyone wondering where the memory went.</summary>
    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return Entries.Count;
            }
        }
    }

    /// <summary>
    /// The library at <paramref name="path"/>, read only if it has not been read or has changed since. Exceptions
    /// are the caller's to handle, as they are for a plain read.
    /// </summary>
    public static SymbolLibrary Load(string path)
    {
        string key = Path.GetFullPath(path);
        var written = File.GetLastWriteTimeUtc(key);
        long length = new FileInfo(key).Length;

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var entry) && entry.Written == written && entry.Length == length)
            {
                return entry.Library;
            }
        }

        // Read outside the lock: parsing a large library must not hold up every other reader.
        var library = SymbolLibrary.Load(key);

        lock (Gate)
        {
            Entries[key] = new Entry(library, written, length);
        }

        return library;
    }

    /// <summary>Forgets everything read so far.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }
}
