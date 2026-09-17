using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;
using Anode.Workbench.Views;

namespace Anode.Workbench;

public partial class App : Application
{
    /// <summary>Language asked for on the command line (<c>--lang=ru</c>); otherwise the stored choice, otherwise English.</summary>
    public static CultureInfo? RequestedLanguage { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless test sessions have no lifetime; they build the workbench themselves.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsStore();
            var shell = CreateWorkbench(Path.Combine(AppContext.BaseDirectory, "plugins"), new RecentProjectsStore(), settings);
            if ((RequestedLanguage ?? settings.Language) is { } language)
            {
                shell.SetLanguage(Tr.Match(language));
            }

            desktop.MainWindow = new MainWindow { DataContext = shell };

            var files = (desktop.Args ?? []).Where(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a)).ToList();
            if (files.Count == 0)
            {
                ShellContributions.ShowStartPage(shell);
            }

            foreach (string path in files)
            {
                _ = shell.OpenAsync(path);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates the workbench, registers its own translations and contributions, and activates every plugin found
    /// under <paramref name="pluginsRoot"/>.
    /// </summary>
    public static ShellViewModel CreateWorkbench(
        string? pluginsRoot,
        RecentProjectsStore recents,
        SettingsStore? settings = null,
        string? dataDirectory = null)
    {
        Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));

        var log = new LogService();
        var shell = new ShellViewModel(log, recents, settings);
        if (dataDirectory is { Length: > 0 })
        {
            shell.DataDirectory = dataDirectory;
        }

        ShellContributions.Register(shell);

        if (pluginsRoot is not null)
        {
            foreach (var plugin in new PluginLoader(log).LoadAll(pluginsRoot))
            {
                try
                {
                    plugin.Instance.Activate(shell.CreatePluginContext(plugin.Manifest));
                    log.Info(Tr.T("shell.log.pluginActivated", plugin.Manifest.Name, plugin.Manifest.Version));
                }
                catch (Exception ex)
                {
                    log.Error(Tr.T("shell.log.pluginFailed", plugin.Manifest.Name, ex.Message), ex);
                }
            }

            // Plugins bring their own languages with them.
            ShellContributions.RegisterLanguages(shell);
        }

        shell.Relayout();
        return shell;
    }
}
