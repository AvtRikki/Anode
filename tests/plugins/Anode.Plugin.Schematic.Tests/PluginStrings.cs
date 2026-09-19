using Anode.Sdk;

[assembly: AssemblyFixture(typeof(Anode.Plugin.Schematic.Tests.PluginStrings))]

namespace Anode.Plugin.Schematic.Tests;

/// <summary>
/// The plugin's own translations, registered once for every test in this assembly. The catalog is global and tests
/// run side by side: one that registered it for its own length and let it go again turned the row names another test
/// was comparing into bare keys halfway through.
/// </summary>
public sealed class PluginStrings : IDisposable
{
    private readonly IDisposable _registration = Tr.Register(JsonTextCatalog.FromAssembly(typeof(SchematicDocument).Assembly));

    public void Dispose() => _registration.Dispose();
}
