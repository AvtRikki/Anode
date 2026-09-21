using System.Globalization;
using System.Reflection;
using Anode.Sdk;

namespace Anode.Workbench.Tests;

/// <summary>The catalogs themselves: every language says the same things, and nothing is left blank.</summary>
public class LocalizationTests
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru");

    public static TheoryData<string> CatalogAssemblies() =>
    [
        typeof(App).Assembly.Location,
        Path.Combine(PcbPluginTests.PluginRoot, "Anode.Plugin.Pcb.dll"),
        Path.Combine(SchematicPluginTests.PluginRoot, "Anode.Plugin.Schematic.dll"),
    ];

    [Theory]
    [MemberData(nameof(CatalogAssemblies))]
    public void Every_language_translates_the_same_keys(string assemblyPath)
    {
        var catalog = JsonTextCatalog.FromAssembly(Assembly.LoadFrom(assemblyPath));
        var english = Comparable(catalog.Keys(Tr.Neutral));

        Assert.NotEmpty(english);
        foreach (var culture in catalog.Cultures.Where(c => c.Name != Tr.Neutral.Name))
        {
            var translated = Comparable(catalog.Keys(culture));
            Assert.Equal([], english.Except(translated, StringComparer.Ordinal).Order(StringComparer.Ordinal));
            Assert.Equal([], translated.Except(english, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        // Plural forms differ by language (en: one/other, ru: one/few/many), so only their prefix is compared.
        static HashSet<string> Comparable(IReadOnlyCollection<string> keys) =>
            [.. keys.Select(k => k[(k.LastIndexOf('.') + 1)..] is "one" or "few" or "many" or "other" ? k[..k.LastIndexOf('.')] : k)];
    }

    [Fact]
    public void Texts_fall_back_to_english_and_finally_to_the_key()
    {
        var catalog = JsonTextCatalog.FromTexts(
            ("en", new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" }),
            ("ru", new Dictionary<string, string> { ["a"] = "А" }));

        using (Tr.Register(catalog))
        {
            Assert.Equal("A", Tr.T("a"));

            Tr.SetCulture(Russian);
            Assert.Equal("А", Tr.T("a"));
            Assert.Equal("B", Tr.T("b"));
            Assert.Equal("missing.key", Tr.T("missing.key"));
            Tr.SetCulture(Tr.Neutral);
        }

        // Once the catalog is gone the key is all that is left.
        Assert.Equal("a", Tr.T("a"));
    }

    [Fact]
    public void Diagnostic_text_stays_english_when_interface_language_changes()
    {
        using var registration = Tr.Register(JsonTextCatalog.FromTexts(
            ("en", new Dictionary<string, string> { ["test.diagnostic"] = "Loaded {0}: {1:F1}." }),
            ("ru", new Dictionary<string, string> { ["test.diagnostic"] = "Translated {0}: {1:F1}." })));
        var previous = Tr.Culture;
        try
        {
            Tr.SetCulture(Russian);
            Assert.Equal("Loaded board: 1.5.", Tr.English("test.diagnostic", "board", 1.5));
            Assert.Equal("missing.diagnostic", Tr.English("missing.diagnostic"));
            Assert.Equal(Russian, Tr.Culture);
            Assert.StartsWith("Translated", Tr.T("test.diagnostic", "board", 1.5));
        }
        finally
        {
            Tr.SetCulture(previous);
        }
    }

    [Fact]
    public void Plurals_follow_the_language()
    {
        var catalog = JsonTextCatalog.FromTexts(
            ("en", new Dictionary<string, string> { ["layer.one"] = "layer", ["layer.other"] = "layers" }),
            ("ru", new Dictionary<string, string> { ["layer.one"] = "слой", ["layer.few"] = "слоя", ["layer.many"] = "слоёв" }));

        using (Tr.Register(catalog))
        {
            Assert.Equal("layer", Tr.Plural("layer", 1));
            Assert.Equal("layers", Tr.Plural("layer", 4));

            Tr.SetCulture(Russian);
            Assert.Equal("слой", Tr.Plural("layer", 1));
            Assert.Equal("слоя", Tr.Plural("layer", 4));
            Assert.Equal("слоёв", Tr.Plural("layer", 11));
            Assert.Equal("слоёв", Tr.Plural("layer", 25));
            Assert.Equal("слой", Tr.Plural("layer", 21));
            Tr.SetCulture(Tr.Neutral);
        }
    }

    [Fact]
    public void Available_languages_include_every_catalog_and_english()
    {
        using (Tr.Register(JsonTextCatalog.FromAssembly(typeof(App).Assembly)))
        {
            Assert.Contains(Tr.AvailableCultures, c => c.Name == "en");
            Assert.Contains(Tr.AvailableCultures, c => c.Name == "ru");
            Assert.Equal("ru", Tr.Match(CultureInfo.GetCultureInfo("ru-RU")).Name);
            Assert.Equal("en", Tr.Match(CultureInfo.GetCultureInfo("fr-FR")).Name);
        }
    }
}
