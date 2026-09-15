namespace Ecad.App;

public enum RendererKind
{
    Skia,
    OpenGl,
}

/// <summary>Startup options that must be known before Avalonia initialises.</summary>
public static class AppOptions
{
    /// <summary>OpenGL is the default (see docs/adr/0001-renderer.md); Skia remains as a fallback.</summary>
    public static RendererKind Renderer { get; set; } = RendererKind.OpenGl;

    /// <summary>Reads <c>--renderer=opengl|skia</c>, falling back to the <c>ECAD_RENDERER</c> environment variable.</summary>
    public static RendererKind ParseRenderer(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(a => a.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))?["--renderer=".Length..]
                        ?? Environment.GetEnvironmentVariable("ECAD_RENDERER");

        return value?.ToLowerInvariant() switch
        {
            "skia" => RendererKind.Skia,
            _ => RendererKind.OpenGl,
        };
    }
}
