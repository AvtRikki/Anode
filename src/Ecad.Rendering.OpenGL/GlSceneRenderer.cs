using System.Diagnostics;
using System.Runtime.InteropServices;
using Ecad.Geometry;
using Ecad.KiCad;
using Silk.NET.OpenGL;

namespace Ecad.Rendering.OpenGL;

public readonly record struct GlRenderStats(double LastUploadMs, long GpuBytes, int DrawCalls);

/// <summary>
/// Draws a <see cref="BoardScene"/> with OpenGL 3.3 / ES 3.0. Each layer is uploaded once into instance buffers
/// (segments, discs) and an indexed triangle buffer (polygons triangulated with Earcut). Move previews reuse the
/// same batches with a transform uniform, so dragging costs no uploads.
/// All methods must be called on the thread that owns the current GL context.
/// </summary>
public sealed unsafe class GlSceneRenderer : IDisposable
{
    private const byte DimAlpha = 70;

    private static readonly float[] QuadCorners = [0, -1, 1, -1, 0, 1, 1, 1];

    private readonly GL _gl;
    private readonly GlProgram _segments;
    private readonly GlProgram _circles;
    private readonly GlProgram _fill;
    private readonly uint _quadVbo;
    private readonly Dictionary<LayerGeometry, GpuBatch> _batches = [];
    private readonly List<(LayerGeometry Layer, GpuBatch Batch)> _preview = [];
    private readonly List<float> _dynamic = [];
    private readonly GpuBatch _grid = new(0);
    private readonly GpuBatch _box = new(0);

    private BoardScene? _scene;
    private Dictionary<LayerGeometry, GpuBatch>? _highlight;
    private (IReadOnlySet<int>? Owners, Net? Net) _highlightKey;
    private IReadOnlyList<LayerGeometry>? _previewSource;
    private double _uploadMs;
    private int _drawCalls;

    public GlSceneRenderer(GL gl, GlslDialect dialect)
    {
        _gl = gl;
        _segments = GlShaders.Build(gl, dialect, GlShaders.SegmentVertex, GlShaders.SegmentFragment,
            (GlShaders.Corner, "a_corner"), (GlShaders.Attr1, "a_a"), (GlShaders.Attr2, "a_b"), (GlShaders.Attr3, "a_hw"));
        _circles = GlShaders.Build(gl, dialect, GlShaders.CircleVertex, GlShaders.CircleFragment,
            (GlShaders.Corner, "a_corner"), (GlShaders.Attr1, "a_c"), (GlShaders.Attr2, "a_r"));
        _fill = GlShaders.Build(gl, dialect, GlShaders.FillVertex, GlShaders.FillFragment, (GlShaders.Attr1, "a_pos"));

        _quadVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)QuadCorners, BufferUsageARB.StaticDraw);
    }

    public string Version => _gl.GetStringS(StringName.Version) ?? "unknown";

    public GlRenderStats Stats => new(_uploadMs, _batches.Values.Sum(b => b.Bytes), _drawCalls);

    /// <summary>Replaces the scene; GPU buffers of the previous one are released.</summary>
    public void SetScene(BoardScene? scene)
    {
        if (ReferenceEquals(scene, _scene))
        {
            return;
        }

        ReleaseBatches(_batches);
        ReleaseHighlight();
        ReleasePreview();
        _scene = scene;
    }

    /// <param name="pixelWidth">Framebuffer width in physical pixels.</param>
    /// <param name="pixelHeight">Framebuffer height in physical pixels.</param>
    public void Render(ViewState view, uint framebuffer, int pixelWidth, int pixelHeight)
    {
        _drawCalls = 0;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.Viewport(0, 0, (uint)pixelWidth, (uint)pixelHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

        var bg = LayerStyle.Background;
        _gl.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        var visible = view.VisibleWorld;
        if (view.ShowGrid)
        {
            DrawGrid(view, visible);
        }

        if (_scene is null)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        bool uploaded = false;
        bool dimmed = view.IsDimmed;

        foreach (var layer in _scene.Layers)
        {
            if (!layer.IsVisible || !layer.Bounds.Intersects(visible))
            {
                continue;
            }

            if (!_batches.TryGetValue(layer, out var batch) || batch.Version != layer.Version)
            {
                batch?.Release(_gl);
                batch = Upload(layer, null);
                _batches[layer] = batch;
                uploaded = true;
            }

            var color = dimmed ? layer.Color.WithAlpha(Math.Min(layer.Color.A, DimAlpha)) : layer.Color;
            Draw(batch, color, view, Transform2D.Identity);
        }

        uploaded |= UpdateHighlight(view);
        if (_highlight is not null)
        {
            foreach (var (layer, batch) in _highlight)
            {
                if (layer.IsVisible)
                {
                    Draw(batch, Brighten(layer.Color), view, Transform2D.Identity);
                }
            }
        }

        uploaded |= UpdatePreview(view);
        foreach (var (layer, batch) in _preview)
        {
            if (_scene.Find(layer.Name)?.IsVisible != false)
            {
                Draw(batch, Brighten(layer.Color), view, view.PreviewTransform);
            }
        }

        if (view.SelectionBox is { } box)
        {
            DrawSelectionBox(view, box);
        }

        if (uploaded)
        {
            _uploadMs = sw.Elapsed.TotalMilliseconds;
        }

        _gl.BindVertexArray(0);
    }

    private void Draw(GpuBatch batch, ColorRgba color, ViewState view, Transform2D xform)
    {
        if (batch.IndexCount > 0)
        {
            Use(_fill, color, view, xform);
            _gl.BindVertexArray(batch.FillVao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)batch.IndexCount, DrawElementsType.UnsignedInt, null);
            _drawCalls++;
        }

        if (batch.CircleCount > 0)
        {
            Use(_circles, color, view, xform);
            _gl.BindVertexArray(batch.CircleVao);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)batch.CircleCount);
            _drawCalls++;
        }

        if (batch.SegmentCount > 0)
        {
            Use(_segments, color, view, xform);
            _gl.BindVertexArray(batch.SegmentVao);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)batch.SegmentCount);
            _drawCalls++;
        }
    }

    private void Use(GlProgram program, ColorRgba color, ViewState view, Transform2D xform)
    {
        var t = view.WorldToScreen;
        double halfW = view.Width / 2, halfH = view.Height / 2;

        // Invert x' = A·x + Tx for the world point that lands in the middle of the viewport.
        double centerX = (halfW - t.Tx) / t.A;
        double centerY = (halfH - t.Ty) / t.D;

        _gl.UseProgram(program.Handle);
        _gl.Uniform2(program.Center, (float)centerX, (float)centerY);
        _gl.Uniform2(program.Scale, (float)t.A, (float)t.D);
        _gl.Uniform2(program.HalfViewport, (float)halfW, (float)halfH);
        if (program.DevPxPerMm >= 0)
        {
            _gl.Uniform1(program.DevPxPerMm, (float)(Math.Abs(t.D) * view.RenderScaling));
        }

        _gl.Uniform4(program.Xform, (float)xform.A, (float)xform.B, (float)xform.C, (float)xform.D);
        _gl.Uniform2(program.XformT, (float)xform.Tx, (float)xform.Ty);
        _gl.Uniform4(program.Color, color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    private void DrawGrid(ViewState view, RectD visible)
    {
        double spacing = view.GridSpacing;
        _dynamic.Clear();
        for (double x = Math.Floor(visible.MinX / spacing) * spacing; x <= visible.MaxX; x += spacing)
        {
            _dynamic.AddRange([(float)x, (float)visible.MinY, (float)x, (float)visible.MaxY, 0f]);
        }

        for (double y = Math.Floor(visible.MinY / spacing) * spacing; y <= visible.MaxY; y += spacing)
        {
            _dynamic.AddRange([(float)visible.MinX, (float)y, (float)visible.MaxX, (float)y, 0f]);
        }

        UploadDynamicSegments(_grid);

        // Grid lines are hairlines: the shader clamps width to half a pixel on each side.
        Draw(_grid, LayerStyle.Grid, view with { RenderScaling = 1 }, Transform2D.Identity);
    }

    private void DrawSelectionBox(ViewState view, RectD box)
    {
        var color = ViewState.SelectionBoxColor(view.SelectionBoxCrossing);
        float x0 = (float)box.MinX, y0 = (float)box.MinY, x1 = (float)box.MaxX, y1 = (float)box.MaxY;

        _dynamic.Clear();
        _dynamic.AddRange([x0, y0, x1, y0, 0f, x1, y0, x1, y1, 0f, x1, y1, x0, y1, 0f, x0, y1, x0, y0, 0f]);
        UploadDynamicSegments(_box);

        if (_box.FillVao == 0)
        {
            _box.FillVao = _gl.GenVertexArray();
            _gl.BindVertexArray(_box.FillVao);
            _box.FillVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _box.FillVbo);
            _gl.EnableVertexAttribArray(GlShaders.Attr1);
            _gl.VertexAttribPointer(GlShaders.Attr1, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);
            _box.FillIbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _box.FillIbo);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)[0, 1, 2, 0, 2, 3], BufferUsageARB.StaticDraw);
            _gl.BindVertexArray(0);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _box.FillVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)[x0, y0, x1, y0, x1, y1, x0, y1], BufferUsageARB.DynamicDraw);
        _box.IndexCount = 6;

        // Translucent fill first (the batch draws fill, then outline segments), outline stays opaque.
        Use(_fill, color.WithAlpha(40), view, Transform2D.Identity);
        _gl.BindVertexArray(_box.FillVao);
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, null);
        Use(_segments, color, view with { RenderScaling = 1 }, Transform2D.Identity);
        _gl.BindVertexArray(_box.SegmentVao);
        _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)_box.SegmentCount);
        _drawCalls += 2;
    }

    private void UploadDynamicSegments(GpuBatch batch)
    {
        if (batch.SegmentVao == 0)
        {
            (batch.SegmentVao, batch.SegmentVbo) = CreateInstanced([], [(GlShaders.Attr1, 2, 0), (GlShaders.Attr2, 2, 2), (GlShaders.Attr3, 1, 4)], 5, BufferUsageARB.DynamicDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, batch.SegmentVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)CollectionsMarshal.AsSpan(_dynamic), BufferUsageARB.DynamicDraw);
        batch.SegmentCount = _dynamic.Count / 5;
    }

    private bool UpdateHighlight(ViewState view)
    {
        var key = (view.SelectedOwners, view.HighlightNet);
        if (ReferenceEquals(key.SelectedOwners, _highlightKey.Owners) && key.HighlightNet == _highlightKey.Net)
        {
            return false;
        }

        ReleaseHighlight();
        _highlightKey = key;
        if (_scene is null || (view.SelectedOwners is not { Count: > 0 } && view.HighlightNet is null))
        {
            return false;
        }

        var scene = _scene;
        var owners = view.SelectedOwners;
        var net = view.HighlightNet;
        bool Matches(int owner) => owners?.Contains(owner) == true || (net is not null && scene.OwnerNet(owner) == net);

        _highlight = [];
        foreach (var layer in scene.Layers)
        {
            var batch = Upload(layer, Matches);
            if (batch.IsEmpty)
            {
                batch.Release(_gl);
            }
            else
            {
                _highlight[layer] = batch;
            }
        }

        return true;
    }

    private bool UpdatePreview(ViewState view)
    {
        if (ReferenceEquals(view.Preview, _previewSource))
        {
            return false;
        }

        ReleasePreview();
        _previewSource = view.Preview;
        if (view.Preview is null)
        {
            return false;
        }

        foreach (var layer in view.Preview)
        {
            _preview.Add((layer, Upload(layer, null)));
        }

        return true;
    }

    private GpuBatch Upload(LayerGeometry layer, Func<int, bool>? filter)
    {
        var batch = new GpuBatch(layer.Version);

        var segments = new List<float>(filter is null ? layer.Lines.Count * 5 : 64);
        foreach (var line in layer.Lines)
        {
            if (filter is null || filter(line.Owner))
            {
                segments.AddRange([line.A.X, line.A.Y, line.B.X, line.B.Y, line.Width / 2]);
            }
        }

        if (segments.Count > 0)
        {
            (batch.SegmentVao, batch.SegmentVbo) = CreateInstanced(CollectionsMarshal.AsSpan(segments),
                [(GlShaders.Attr1, 2, 0), (GlShaders.Attr2, 2, 2), (GlShaders.Attr3, 1, 4)], 5, BufferUsageARB.StaticDraw);
            batch.SegmentCount = segments.Count / 5;
            batch.Bytes += segments.Count * sizeof(float);
        }

        var circles = new List<float>(filter is null ? layer.Circles.Count * 3 : 64);
        foreach (var circle in layer.Circles)
        {
            if (filter is null || filter(circle.Owner))
            {
                circles.AddRange([circle.Center.X, circle.Center.Y, circle.Radius]);
            }
        }

        if (circles.Count > 0)
        {
            (batch.CircleVao, batch.CircleVbo) = CreateInstanced(CollectionsMarshal.AsSpan(circles),
                [(GlShaders.Attr1, 2, 0), (GlShaders.Attr2, 1, 2)], 3, BufferUsageARB.StaticDraw);
            batch.CircleCount = circles.Count / 3;
            batch.Bytes += circles.Count * sizeof(float);
        }

        var vertices = new List<float>();
        var indices = new List<uint>();
        foreach (var polygon in layer.Polygons)
        {
            if ((filter is not null && !filter(polygon.Owner)) || polygon.Points.Length < 3)
            {
                continue;
            }

            // Normally precomputed by SceneTriangulator on a background thread.
            var tris = polygon.Triangles;

            uint baseIndex = (uint)(vertices.Count / 2);
            foreach (var p in polygon.Points)
            {
                vertices.Add(p.X);
                vertices.Add(p.Y);
            }

            foreach (int i in tris)
            {
                indices.Add(baseIndex + (uint)i);
            }
        }

        if (indices.Count > 0)
        {
            batch.FillVao = _gl.GenVertexArray();
            _gl.BindVertexArray(batch.FillVao);

            batch.FillVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, batch.FillVbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)CollectionsMarshal.AsSpan(vertices), BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(GlShaders.Attr1);
            _gl.VertexAttribPointer(GlShaders.Attr1, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);

            batch.FillIbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, batch.FillIbo);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)CollectionsMarshal.AsSpan(indices), BufferUsageARB.StaticDraw);

            _gl.BindVertexArray(0);
            batch.IndexCount = indices.Count;
            batch.Bytes += vertices.Count * sizeof(float) + indices.Count * sizeof(uint);
        }

        return batch;
    }

    private (uint Vao, uint Vbo) CreateInstanced(ReadOnlySpan<float> data, (uint Location, int Size, int Offset)[] attributes, int stride, BufferUsageARB usage)
    {
        uint vao = _gl.GenVertexArray();
        _gl.BindVertexArray(vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        _gl.EnableVertexAttribArray(GlShaders.Corner);
        _gl.VertexAttribPointer(GlShaders.Corner, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);
        _gl.VertexAttribDivisor(GlShaders.Corner, 0);

        uint vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, data, usage);
        foreach (var (location, size, offset) in attributes)
        {
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribPointer(location, size, VertexAttribPointerType.Float, false, (uint)(stride * sizeof(float)), (void*)(offset * sizeof(float)));
            _gl.VertexAttribDivisor(location, 1);
        }

        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    private void ReleaseHighlight()
    {
        if (_highlight is not null)
        {
            ReleaseBatches(_highlight);
            _highlight = null;
        }

        _highlightKey = default;
    }

    private void ReleasePreview()
    {
        foreach (var (_, batch) in _preview)
        {
            batch.Release(_gl);
        }

        _preview.Clear();
        _previewSource = null;
    }

    private void ReleaseBatches(Dictionary<LayerGeometry, GpuBatch> batches)
    {
        foreach (var batch in batches.Values)
        {
            batch.Release(_gl);
        }

        batches.Clear();
    }

    private static ColorRgba Brighten(ColorRgba c) =>
        new((byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60), 255);

    public void Dispose()
    {
        ReleaseBatches(_batches);
        ReleaseHighlight();
        ReleasePreview();
        _grid.Release(_gl);
        _box.Release(_gl);
        _gl.DeleteBuffer(_quadVbo);
        _segments.Dispose();
        _circles.Dispose();
        _fill.Dispose();
    }

    private sealed class GpuBatch(int version)
    {
        public int Version { get; } = version;

        public uint SegmentVao;
        public uint SegmentVbo;
        public int SegmentCount;
        public uint CircleVao;
        public uint CircleVbo;
        public int CircleCount;
        public uint FillVao;
        public uint FillVbo;
        public uint FillIbo;
        public int IndexCount;
        public long Bytes;

        public bool IsEmpty => SegmentCount == 0 && CircleCount == 0 && IndexCount == 0;

        public void Release(GL gl)
        {
            foreach (uint vao in (ReadOnlySpan<uint>)[SegmentVao, CircleVao, FillVao])
            {
                if (vao != 0)
                {
                    gl.DeleteVertexArray(vao);
                }
            }

            foreach (uint buffer in (ReadOnlySpan<uint>)[SegmentVbo, CircleVbo, FillVbo, FillIbo])
            {
                if (buffer != 0)
                {
                    gl.DeleteBuffer(buffer);
                }
            }

            SegmentVao = CircleVao = FillVao = SegmentVbo = CircleVbo = FillVbo = FillIbo = 0;
            SegmentCount = CircleCount = IndexCount = 0;
        }
    }
}
