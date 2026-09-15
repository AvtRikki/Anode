using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Ecad.Rendering;
using Ecad.Rendering.OpenGL;
using Ecad.Rendering.Skia;
using Silk.NET.OpenGL;

namespace Ecad.App.Controls;

/// <summary>A control that draws a board scene for a given view; interaction lives in <see cref="BoardCanvas"/>.</summary>
internal interface IBoardSurface
{
    string BackendName { get; }

    BoardScene? Scene { set; }

    /// <summary>Stores the view and schedules a frame.</summary>
    void Present(ViewState? view);

    /// <summary>Frame time in milliseconds, raised on the UI thread.</summary>
    event Action<double>? FrameRendered;
}

/// <summary>Prototype A inside Avalonia's compositor via a Skia lease.</summary>
internal sealed class SkiaBoardSurface : Control, IBoardSurface
{
    private SkiaSceneRenderer? _renderer;
    private ViewState? _view;

    public string BackendName => "Skia";

    public event Action<double>? FrameRendered;

    public BoardScene? Scene
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

/// <summary>Prototype B through <see cref="OpenGlControlBase"/>.</summary>
internal sealed class GlBoardSurface : OpenGlControlBase, IBoardSurface
{
    private GlSceneRenderer? _renderer;
    private BoardScene? _scene;
    private bool _sceneDirty = true;
    private ViewState? _view;

    public string BackendName { get; private set; } = "OpenGL";

    public event Action<double>? FrameRendered;

    public BoardScene? Scene
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
