using Ecad.Geometry;
using Ecad.KiCad;
using SkiaSharp;

namespace Ecad.Rendering.Skia;

/// <summary>
/// Prototype A: draws a <see cref="BoardScene"/> with SkiaSharp. Primitives (text included, as stroke segments) are batched
/// into one path per layer and stroke width, cached until the layer changes.
/// </summary>
public sealed class SkiaSceneRenderer : IDisposable
{
    private const byte DimAlpha = 70;

    private readonly BoardScene _scene;
    private readonly Dictionary<LayerGeometry, LayerCache> _cache = [];
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _layerPaint = new();
    private readonly Lock _lock = new();

    private (int Owner, Net? Net) _highlightKey = (-1, null);
    private Dictionary<LayerGeometry, LayerCache>? _highlight;

    public SkiaSceneRenderer(BoardScene scene) => _scene = scene;

    public BoardScene Scene => _scene;

    public void Render(SKCanvas canvas, ViewState view)
    {
        lock (_lock)
        {
            RenderCore(canvas, view);
        }
    }

    private void RenderCore(SKCanvas canvas, ViewState view)
    {
        _fill.Color = ToSk(LayerStyle.Background);
        canvas.DrawRect(0, 0, (float)view.Width, (float)view.Height, _fill);

        var visible = view.VisibleWorld;

        if (view.ShowGrid)
        {
            DrawGrid(canvas, view, visible);
        }

        bool dimmed = view.IsDimmed;
        UpdateHighlight(view);

        canvas.Save();
        var matrix = ToSk(view.WorldToScreen);
        canvas.Concat(in matrix);

        float minWorldWidth = (float)(1.0 / view.PixelsPerMm);
        foreach (var layer in _scene.Layers)
        {
            if (!layer.IsVisible || !layer.Bounds.Intersects(visible))
            {
                continue;
            }

            var color = layer.Color;
            DrawLayer(canvas, GetCache(_cache, layer), dimmed ? color.WithAlpha(Math.Min(color.A, DimAlpha)) : color, minWorldWidth);
        }

        if (_highlight is not null)
        {
            foreach (var (layer, cache) in _highlight)
            {
                if (layer.IsVisible)
                {
                    DrawLayer(canvas, cache, Brighten(layer.Color), minWorldWidth);
                }
            }
        }

        canvas.Restore();
    }

    private void DrawLayer(SKCanvas canvas, LayerCache cache, ColorRgba color, float minWorldWidth)
    {
        // Semi-transparent layers are composited as a whole so overlapping primitives do not stack alpha.
        bool group = color.A < 255;
        var opaque = ToSk(color.WithAlpha(255));
        if (group)
        {
            _layerPaint.Color = new SKColor(0, 0, 0, color.A);
            canvas.SaveLayer(_layerPaint);
        }

        _fill.Color = opaque;
        if (cache.Polygons is { } polygons)
        {
            canvas.DrawPath(polygons, _fill);
        }

        if (cache.Circles is { } circles)
        {
            canvas.DrawPath(circles, _fill);
        }

        _stroke.Color = opaque;
        foreach (var (width, path) in cache.Lines)
        {
            _stroke.StrokeWidth = Math.Max(width, minWorldWidth);
            canvas.DrawPath(path, _stroke);
        }

        if (group)
        {
            canvas.Restore();
        }
    }

    private void DrawGrid(SKCanvas canvas, ViewState view, RectD visible)
    {
        double spacing = view.GridSpacing;

        _stroke.Color = ToSk(LayerStyle.Grid);
        _stroke.StrokeWidth = 1;
        var t = view.WorldToScreen;

        for (double x = Math.Floor(visible.MinX / spacing) * spacing; x <= visible.MaxX; x += spacing)
        {
            var p = t.Apply(new Vector2D(x, 0));
            canvas.DrawLine((float)p.X, 0, (float)p.X, (float)view.Height, _stroke);
        }

        for (double y = Math.Floor(visible.MinY / spacing) * spacing; y <= visible.MaxY; y += spacing)
        {
            var p = t.Apply(new Vector2D(0, y));
            canvas.DrawLine(0, (float)p.Y, (float)view.Width, (float)p.Y, _stroke);
        }
    }

    private void UpdateHighlight(ViewState view)
    {
        var key = (view.SelectedOwner, view.HighlightNet);
        if (key == _highlightKey)
        {
            return;
        }

        DisposeCaches(_highlight);
        _highlight = null;
        _highlightKey = key;
        if (view.SelectedOwner < 0 && view.HighlightNet is null)
        {
            return;
        }

        bool Matches(int owner) => owner == view.SelectedOwner || (view.HighlightNet is not null && _scene.OwnerNet(owner) == view.HighlightNet);

        _highlight = [];
        foreach (var layer in _scene.Layers)
        {
            var cache = BuildCache(layer, Matches);
            if (!cache.IsEmpty)
            {
                _highlight[layer] = cache;
            }
            else
            {
                cache.Dispose();
            }
        }
    }

    private static LayerCache GetCache(Dictionary<LayerGeometry, LayerCache> caches, LayerGeometry layer)
    {
        if (caches.TryGetValue(layer, out var cache) && cache.Version == layer.Version)
        {
            return cache;
        }

        cache?.Dispose();
        cache = BuildCache(layer, null);
        caches[layer] = cache;
        return cache;
    }

    private static LayerCache BuildCache(LayerGeometry layer, Func<int, bool>? filter)
    {
        var cache = new LayerCache(layer.Version);
        var byWidth = new Dictionary<float, SKPath>();

        foreach (var line in layer.Lines)
        {
            if (filter is not null && !filter(line.Owner))
            {
                continue;
            }

            if (!byWidth.TryGetValue(line.Width, out var path))
            {
                path = new SKPath();
                byWidth[line.Width] = path;
            }

            path.MoveTo(line.A.X, line.A.Y);
            path.LineTo(line.B.X, line.B.Y);
        }

        cache.Lines.AddRange(byWidth.Select(kv => (kv.Key, kv.Value)));

        foreach (var circle in layer.Circles)
        {
            if (filter is null || filter(circle.Owner))
            {
                (cache.Circles ??= new SKPath()).AddCircle(circle.Center.X, circle.Center.Y, circle.Radius);
            }
        }

        foreach (var polygon in layer.Polygons)
        {
            if ((filter is null || filter(polygon.Owner)) && polygon.Points.Length >= 3)
            {
                var path = cache.Polygons ??= new SKPath { FillType = SKPathFillType.Winding };
                path.MoveTo(polygon.Points[0].X, polygon.Points[0].Y);
                for (int i = 1; i < polygon.Points.Length; i++)
                {
                    path.LineTo(polygon.Points[i].X, polygon.Points[i].Y);
                }

                path.Close();
            }
        }

        return cache;
    }

    private static ColorRgba Brighten(ColorRgba c) =>
        new((byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60), 255);

    private static SKColor ToSk(ColorRgba c) => new(c.R, c.G, c.B, c.A);

    private static SKMatrix ToSk(Transform2D t) =>
        new((float)t.A, (float)t.C, (float)t.Tx, (float)t.B, (float)t.D, (float)t.Ty, 0, 0, 1);

    private static void DisposeCaches(Dictionary<LayerGeometry, LayerCache>? caches)
    {
        if (caches is null)
        {
            return;
        }

        foreach (var cache in caches.Values)
        {
            cache.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeCaches(_cache);
            DisposeCaches(_highlight);
            _cache.Clear();
            _highlight = null;
            _stroke.Dispose();
            _fill.Dispose();
            _layerPaint.Dispose();
        }
    }

    private sealed class LayerCache(int version) : IDisposable
    {
        public int Version { get; } = version;

        public List<(float Width, SKPath Path)> Lines { get; } = [];

        public SKPath? Circles { get; set; }

        public SKPath? Polygons { get; set; }

        public bool IsEmpty => Lines.Count == 0 && Circles is null && Polygons is null;

        public void Dispose()
        {
            foreach (var (_, path) in Lines)
            {
                path.Dispose();
            }

            Circles?.Dispose();
            Polygons?.Dispose();
        }
    }
}
