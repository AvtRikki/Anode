using Avalonia;
using Avalonia.Media;
using Anode.Sdk;

namespace Anode.Workbench;

internal static class Program
{
    // Don't use Avalonia or anything SynchronizationContext-reliant before AppMain is called.
    [STAThread]
    public static int Main(string[] args)
    {
        GraphicsOptions.Renderer = GraphicsOptions.Parse(args);
        App.RequestedLanguage = ParseLanguage(args);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (InvalidOperationException ex) when (OperatingSystem.IsMacOS() && ex.Message.Contains("RenderTimer", StringComparison.Ordinal))
        {
            // CVDisplayLink cannot be created without an active display (lid closed, no external monitor, locked session).
            Console.Error.WriteLine("Kicad·One could not start rendering: no active display is available.");
            Console.Error.WriteLine("Open the laptop lid or connect a monitor and launch again.");
            Console.Error.WriteLine($"({ex.Message})");
            return 2;
        }
    }

    /// <summary>Reads <c>--lang=ru</c>; an unknown language is ignored and the stored choice wins.</summary>
    private static System.Globalization.CultureInfo? ParseLanguage(string[] args)
    {
        if (args.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) is not { } argument)
        {
            return null;
        }

        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(argument["--lang=".Length..]);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            Console.Error.WriteLine($"Unknown language: {argument["--lang=".Length..]}");
            return null;
        }
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Anode.Workbench/Assets/Fonts#Source Serif 4" })
            .LogToTrace();

        // Avalonia's macOS compositor defaults to Metal, which OpenGlControlBase cannot share textures with.
        if (GraphicsOptions.Renderer == RendererKind.OpenGl)
        {
            builder = builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software],
            });
        }

        return builder;
    }
}
