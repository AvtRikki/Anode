using System.Reflection;
using System.Runtime.Loader;
using Anode.Sdk;

namespace Anode.Workbench.Services;

public sealed record LoadedPlugin(PluginManifest Manifest, string Directory, IPlugin Instance);

/// <summary>
/// Loads plugins from <c>plugins/*/plugin.json</c>. Each plugin gets its own <see cref="AssemblyLoadContext"/>;
/// assemblies the host already ships (the SDK, Avalonia, SkiaSharp) resolve from the default context so types are
/// shared, everything else resolves from the plugin's own folder through its deps.json.
/// </summary>
public sealed class PluginLoader(ILog log)
{
    public IReadOnlyList<LoadedPlugin> LoadAll(string pluginsRoot)
    {
        var loaded = new List<LoadedPlugin>();
        if (!System.IO.Directory.Exists(pluginsRoot))
        {
            log.Warn($"No plugins folder at {pluginsRoot}.");
            return loaded;
        }

        foreach (string manifestPath in System.IO.Directory.EnumerateFiles(pluginsRoot, "plugin.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                loaded.Add(Load(manifestPath));
            }
            catch (Exception ex)
            {
                log.Error($"Plugin at {manifestPath} failed to load: {ex.Message}", ex);
            }
        }

        return loaded;
    }

    public LoadedPlugin Load(string manifestPath)
    {
        var manifest = PluginManifest.Parse(File.ReadAllText(manifestPath));
        if (manifest.ContractVersion != PlatformContract.Version)
        {
            throw new InvalidOperationException(
                $"{manifest.Id} targets contract v{manifest.ContractVersion}, the workbench provides v{PlatformContract.Version}.");
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        string assemblyPath = Path.Combine(directory, manifest.Assembly);
        var context = new PluginLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
        if (Activator.CreateInstance(type) is not IPlugin instance)
        {
            throw new InvalidOperationException($"{manifest.EntryType} does not implement {nameof(IPlugin)}.");
        }

        log.Info($"Loaded plugin {manifest.Name} {manifest.Version} ({manifest.Id}).");
        return new LoadedPlugin(manifest, directory, instance);
    }

    private sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath))
    {
        private static readonly HashSet<string> HostAssemblies = ReadHostAssemblies();
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // Shared with the host: fall back to the default context so contract types are identical.
            if (name.Name is not null && HostAssemblies.Contains(name.Name))
            {
                return null;
            }

            return _resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

        private static HashSet<string> ReadHostAssemblies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    names.Add(Path.GetFileNameWithoutExtension(path));
                }
            }

            foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
            {
                if (assembly.GetName().Name is { } n)
                {
                    names.Add(n);
                }
            }

            return names;
        }
    }
}
