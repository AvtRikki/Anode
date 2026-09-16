namespace Anode.Sdk;

public enum RendererKind
{
    OpenGl,
    Skia,
}

/// <summary>Graphics settings decided at startup, before the windowing platform initialises.</summary>
public static class GraphicsOptions
{
    /// <summary>OpenGL by default (docs/adr/0001-renderer.md); Skia is the fallback and the headless renderer.</summary>
    public static RendererKind Renderer { get; set; } = RendererKind.OpenGl;

    /// <summary>Reads <c>--renderer=opengl|skia</c>, falling back to the <c>ANODE_RENDERER</c> environment variable.</summary>
    public static RendererKind Parse(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(a => a.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))?["--renderer=".Length..]
                        ?? Environment.GetEnvironmentVariable("ANODE_RENDERER");

        return value?.ToLowerInvariant() == "skia" ? RendererKind.Skia : RendererKind.OpenGl;
    }
}
