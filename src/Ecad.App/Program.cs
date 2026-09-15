using Avalonia;
using System;

namespace Ecad.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (InvalidOperationException ex) when (OperatingSystem.IsMacOS() && ex.Message.Contains("RenderTimer", StringComparison.Ordinal))
        {
            // CVDisplayLink cannot be created without an active display (lid closed, no external monitor, locked session).
            Console.Error.WriteLine("Ecad could not start rendering: no active display is available.");
            Console.Error.WriteLine("Open the laptop lid or connect a monitor and launch again.");
            Console.Error.WriteLine($"({ex.Message})");
            return 2;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
