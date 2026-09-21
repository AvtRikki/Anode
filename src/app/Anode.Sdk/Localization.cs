using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Threading;

namespace Anode.Sdk;

/// <summary>
/// A source of translated texts. The workbench and every plugin bring their own; keys are global, so prefix them
/// with the domain (<c>command.file.open</c>, <c>pcb.layers.title</c>).
/// </summary>
public interface ITextCatalog
{
    /// <summary>Cultures this catalog has texts for.</summary>
    IReadOnlyCollection<CultureInfo> Cultures { get; }

    /// <summary>The text for <paramref name="key"/> in exactly <paramref name="culture"/>, or null.</summary>
    string? Find(string key, CultureInfo culture);
}

/// <summary>
/// Translations of the workbench. Texts live in JSON catalogs embedded in each assembly
/// (<c>i18n/en.json</c>, <c>i18n/ru.json</c>, …); a plugin registers its own on activation.
/// Missing keys fall back to the neutral language and finally to the key itself, so a half-translated
/// plugin still shows something readable instead of blanks.
/// </summary>
public static class Tr
{
    /// <summary>The language every catalog must have: the fallback when a key is missing elsewhere.</summary>
    public static readonly CultureInfo Neutral = CultureInfo.GetCultureInfo("en");

    private static readonly List<ITextCatalog> Catalogs = [];
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static CultureInfo _culture = Neutral;

    /// <summary>The language texts are resolved in. Set through <see cref="SetCulture"/>.</summary>
    public static CultureInfo Culture => _culture;

    /// <summary>Raised after the language changed; views rebuild their texts.</summary>
    public static event Action? Changed;

    /// <summary>Languages at least one catalog has, always including <see cref="Neutral"/>.</summary>
    public static IReadOnlyList<CultureInfo> AvailableCultures
    {
        get
        {
            lock (Catalogs)
            {
                return [.. Catalogs.SelectMany(c => c.Cultures).Append(Neutral)
                    .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c.Name, StringComparer.Ordinal)];
            }
        }
    }

    public static IDisposable Register(ITextCatalog catalog)
    {
        lock (Catalogs)
        {
            Catalogs.Add(catalog);
        }

        Cache.Clear();
        RaiseChanged();
        return new CatalogRegistration(catalog);
    }

    /// <summary>Switches the language; no-op when it is already active.</summary>
    public static void SetCulture(CultureInfo culture)
    {
        if (string.Equals(culture.Name, _culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _culture = culture;
        Cache.Clear();
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        RaiseChanged();
    }

    /// <summary>
    /// Tells the views on their own thread: a plugin registers its texts where it loads, which need not be the UI
    /// thread, and the views may only be touched from there.
    /// </summary>
    private static void RaiseChanged()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            changed();
        }
        else
        {
            Dispatcher.UIThread.Post(changed);
        }
    }

    /// <summary>Picks the best available language for <paramref name="preferred"/> (e.g. the system's).</summary>
    public static CultureInfo Match(CultureInfo preferred)
    {
        var available = AvailableCultures;
        for (var candidate = preferred; candidate is { Name.Length: > 0 }; candidate = candidate.Parent)
        {
            if (available.FirstOrDefault(c => string.Equals(c.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)) is { } hit)
            {
                return hit;
            }
        }

        return Neutral;
    }

    /// <summary>The text for <paramref name="key"/>, or the key itself when nothing has it.</summary>
    public static string T(string key) => Cache.GetOrAdd(key, Lookup);

    /// <summary>A text with <c>{0}</c>-style placeholders filled in, formatted in the active language.</summary>
    public static string T(string key, params object?[] args) => string.Format(_culture, T(key), args);

    /// <summary>Resolves diagnostic text in English, independently of the interface language.</summary>
    public static string English(string key, params object?[] args)
    {
        string text = key;
        lock (Catalogs)
        {
            foreach (var catalog in Catalogs)
            {
                if (catalog.Find(key, Neutral) is { } found)
                {
                    text = found;
                    break;
                }
            }
        }

        return args.Length == 0 ? text : string.Format(Neutral, text, args);
    }

    /// <summary>
    /// The plural form of <paramref name="keyPrefix"/> for <paramref name="count"/>: the catalog holds
    /// <c>&lt;prefix&gt;.one</c>, <c>.few</c>, <c>.many</c> and <c>.other</c> as the language needs them.
    /// </summary>
    public static string Plural(string keyPrefix, int count)
    {
        string suffix = _culture.TwoLetterISOLanguageName switch
        {
            "ru" or "uk" or "be" => RussianForm(count),
            _ => count == 1 ? "one" : "other",
        };

        string text = T($"{keyPrefix}.{suffix}");
        return text == $"{keyPrefix}.{suffix}" ? T($"{keyPrefix}.other") : text;

        static string RussianForm(int count)
        {
            int mod100 = count % 100;
            int mod10 = count % 10;
            return mod100 is >= 11 and <= 14 ? "many" : mod10 switch { 1 => "one", >= 2 and <= 4 => "few", _ => "many" };
        }
    }

    private static string Lookup(string key)
    {
        lock (Catalogs)
        {
            for (var culture = _culture; ; culture = culture.Parent)
            {
                foreach (var catalog in Catalogs)
                {
                    if (catalog.Find(key, culture) is { } text)
                    {
                        return text;
                    }
                }

                if (culture.Name.Length == 0)
                {
                    break;
                }
            }

            foreach (var catalog in Catalogs)
            {
                if (catalog.Find(key, Neutral) is { } text)
                {
                    return text;
                }
            }
        }

        return key;
    }

    private sealed class CatalogRegistration(ITextCatalog catalog) : IDisposable
    {
        public void Dispose()
        {
            lock (Catalogs)
            {
                Catalogs.Remove(catalog);
            }

            Cache.Clear();
            RaiseChanged();
        }
    }
}

/// <summary>
/// Texts from JSON files embedded as <c>i18n/&lt;culture&gt;.json</c>. Nested objects are flattened with dots, so
/// <c>{"command": {"file": {"open": "Open…"}}}</c> answers the key <c>command.file.open</c>.
/// </summary>
public sealed class JsonTextCatalog : ITextCatalog
{
    private readonly Dictionary<string, Dictionary<string, string>> _byCulture;

    private JsonTextCatalog(Dictionary<string, Dictionary<string, string>> byCulture)
    {
        _byCulture = byCulture;
        Cultures = [.. byCulture.Keys.Select(CultureInfo.GetCultureInfo)];
    }

    public IReadOnlyCollection<CultureInfo> Cultures { get; }

    /// <summary>Reads every <c>*.i18n.&lt;culture&gt;.json</c> resource embedded in the assembly.</summary>
    public static JsonTextCatalog FromAssembly(Assembly assembly)
    {
        var byCulture = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(".i18n.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            string culture = Path.GetFileNameWithoutExtension(name)[(name.LastIndexOf(".i18n.", StringComparison.Ordinal) + ".i18n.".Length)..];
            using var stream = assembly.GetManifestResourceStream(name)
                               ?? throw new InvalidOperationException($"Cannot read resource {name}.");
            using var document = JsonDocument.Parse(stream);

            var texts = byCulture.TryGetValue(culture, out var existing) ? existing : byCulture[culture] = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(document.RootElement, prefix: string.Empty, texts);
        }

        return new JsonTextCatalog(byCulture);
    }

    /// <summary>A catalog from texts already in memory, for tests and generated content.</summary>
    public static JsonTextCatalog FromTexts(params (string Culture, IReadOnlyDictionary<string, string> Texts)[] catalogs) =>
        new(catalogs.ToDictionary(c => c.Culture, c => c.Texts.ToDictionary(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase));

    public string? Find(string key, CultureInfo culture) =>
        _byCulture.TryGetValue(culture.Name, out var texts) && texts.TryGetValue(key, out string? text) ? text : null;

    /// <summary>Keys the catalog has for a culture; used by tests that compare languages.</summary>
    public IReadOnlyCollection<string> Keys(CultureInfo culture) =>
        _byCulture.TryGetValue(culture.Name, out var texts) ? texts.Keys : [];

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> texts)
    {
        foreach (var property in element.EnumerateObject())
        {
            string key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, key, texts);
                    break;
                case JsonValueKind.String:
                    texts[key] = property.Value.GetString()!;
                    break;
                default:
                    throw new FormatException($"Key {key}: expected a string or an object.");
            }
        }
    }
}
