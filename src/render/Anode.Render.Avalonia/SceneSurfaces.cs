using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Render;
using Anode.Render.OpenGl;
using Anode.Render.Skia;
using Silk.NET.OpenGL;

namespace Anode.Render.Avalonia;

/// <summary>
/// A control that draws a scene for a given view. Interaction belongs to the canvas above it, so a board and a
/// schematic share the same two backends.
/// </summary>
public interface ISceneSurface
{
    string BackendName { get; }

    IRenderScene? Scene { set; }

    /// <summary>Stores the view and schedules a frame.</summary>
    void Present(ViewState? view);

    /// <summary>Frame time in milliseconds, raised on the UI thread.</summary>
    event Action<double>? FrameRendered;
}

/// <summary>Creates the surface the application was started with.</summary>
public static class SceneSurface
{
    public static ISceneSurface Create() =>
        GraphicsOptions.Renderer == RendererKind.OpenGl ? new GlSceneSurface() : new SkiaSceneSurface();
}

/// <summary>Drawing inside Avalonia's compositor via a Skia lease.</summary>
public sealed class SkiaSceneSurface : Control, ISceneSurface
{
    private SkiaSceneRenderer? _renderer;
    private ViewState? _view;

    public string BackendName => "Skia";

    public event Action<double>? FrameRendered;

    public IRenderScene? Scene
    {
        set
        {
            var old = _renderer;
            _renderer = value is null ? null : new SkiaSceneRenderer(value);

            // The render thread may still hold the old renderer for a frame; dispose after it is done.
            if (old is not null)
            {
                Dispatcher.UIThread.Post(old.Dispose, DispatcherPriority.Background);
            }

            InvalidateVisual();
        }
    }

    public void Present(ViewState? view)
    {
        _view = view;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (_renderer is null || _view is null)
        {
            var bg = LayerStyle.Background;
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B)), bounds);
            return;
        }

        context.Custom(new SceneDrawOperation(bounds, _renderer, _view, ms =>
            Dispatcher.UIThread.Post(() => FrameRendered?.Invoke(ms), DispatcherPriority.Background)));
    }

    private sealed class SceneDrawOperation(Rect bounds, SkiaSceneRenderer renderer, ViewState view, Action<double> rendered)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            var sw = Stopwatch.StartNew();

            canvas.Save();
            canvas.ClipRect(new SkiaSharp.SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            renderer.Render(canvas, view);
            canvas.Restore();

            rendered(sw.Elapsed.TotalMilliseconds);
        }
    }
}

/// <summary>The default backend (docs/adr/0001-renderer.md) through <see cref="OpenGlControlBase"/>.</summary>
public sealed class GlSceneSurface : OpenGlControlBase, ISceneSurface
{
    private GlSceneRenderer? _renderer;
    private IRenderScene? _scene;
    private bool _sceneDirty = true;
    private ViewState? _view;

    public string BackendName { get; private set; } = "OpenGL";

    public event Action<double>? FrameRendered;

    public IRenderScene? Scene
    {
        set
        {
            _scene = value;
            _sceneDirty = true;
            RequestNextFrameRendering();
        }
    }

    public void Present(ViewState? view)
    {
        _view = view;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        bool es = GlVersion.Type == GlProfileType.OpenGLES;
        var dialect = es
            ? GlslDialect.Es300
            : GlVersion.Major > 3 || (GlVersion.Major == 3 && GlVersion.Minor >= 3) ? GlslDialect.Desktop330 : GlslDialect.Desktop150;

        _renderer = new GlSceneRenderer(GL.GetApi(gl.GetProcAddress), dialect);
        _sceneDirty = true;
        BackendName = $"OpenGL{(es ? " ES" : string.Empty)} {GlVersion.Major}.{GlVersion.Minor}";
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null || _view is not { } view)
        {
            return;
        }

        if (_sceneDirty)
        {
            _renderer.SetScene(_scene);
            _sceneDirty = false;
        }

        var sw = Stopwatch.StartNew();
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * view.RenderScaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * view.RenderScaling));
        _renderer.Render(view, (uint)fb, width, height);

        double ms = sw.Elapsed.TotalMilliseconds;
        Dispatcher.UIThread.Post(() => FrameRendered?.Invoke(ms), DispatcherPriority.Background);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    protected override void OnOpenGlLost()
    {
        // GL objects died with the context; drop the renderer without touching GL.
        _renderer = null;
        base.OnOpenGlLost();
    }
}
